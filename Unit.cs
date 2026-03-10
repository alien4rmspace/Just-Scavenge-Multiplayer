using System;
using System.Collections.Generic;
using Interfaces;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

public class Unit : NetworkBehaviour, IDamageable
{
    public static readonly Color PlayerDamageColor = Color.red;
    public static readonly Color ZombieDamageColor = new Color(1f, 0.6f, 0.2f);
    public static readonly Color ObjectDamageColor = Color.yellow;
    public static event Action<Unit> OnUnitDied;
    
    public static List<Unit> playerUnits = new List<Unit>();
    public static List<Unit> zombieUnits = new List<Unit>();
    
    [HideInInspector] public Unit target;

    // Networked State
    public NetworkVariable<float> networkHealth =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> networkAlive =
        new(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Team> networkTeam = new(Team.Player, NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<float> networkSpeed =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Inspector Fields
    public Team team;
    public float baseSpeed = 3.0f;
    public float baseMaxHealth = 100.0f;
    
    // Private Fields
    private Vector3 _knockbackVelocity;
    private float _knockbackTimer;
    private float _knockbackDuration = 0.1f;

    private Rigidbody[] ragdollBodies;
    private Collider[] ragdollColliders;

    protected NavMeshAgent agent;
    protected Animator _animator;
    protected Combat _combat;

    // Properties
    public NavMeshAgent Agent => agent;
    public Team Team => networkTeam.Value;
    public bool IsDead() => !networkAlive.Value;

    // ── Lifecycle ──
    protected virtual void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        _combat = GetComponent<Combat>();
        agent.speed = baseSpeed;
        
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost)
        {
            if (_animator != null)
                _animator.enabled = false;
        }

        // cache and disable ragdoll
        ragdollBodies = GetComponentsInChildren<Rigidbody>();
        ragdollColliders = GetComponentsInChildren<Collider>();
        SetRagdoll(false);
    }
    
    protected virtual void Update()
    {
        if (!networkAlive.Value) return;

        // knockback runs on server only
        if (IsServer)
        {
            float currentSpeed = agent.velocity.magnitude;
            if (Mathf.Abs(currentSpeed - networkSpeed.Value) > 0.1f)
            {
                networkSpeed.Value = currentSpeed;
            }
            if (_knockbackTimer > 0)
            {
                _knockbackTimer -= Time.deltaTime;
                float fade = _knockbackTimer / _knockbackDuration;
                agent.Move(_knockbackVelocity * (fade * Time.deltaTime));
            }
        }

        byte tier = ZombieSyncManager.GetTier(NetworkObjectId);
        if (tier < 3)
            _animator.SetFloat("Speed", networkSpeed.Value);
    }

    // ── Network Lifecycle ──
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // server initializes networked state
            networkHealth.Value = baseMaxHealth;
            networkAlive.Value = true;
            networkTeam.Value = team;

            RegisterUnit();
        }
        else
        {
            // clients read team from network
            team = networkTeam.Value;
            RegisterUnit();

            // keep client in sync if team changes
            networkTeam.OnValueChanged += (prev, current) => { team = current; };

            // disable navagent on client
            agent.enabled = false;
        }

        // all clients listen for death
        networkAlive.OnValueChanged += OnAliveChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        UnregisterUnit();
        networkAlive.OnValueChanged -= OnAliveChanged;
    }

    // ── Registration ──
    private void RegisterUnit()
    {
        if (team == Team.Zombie)
            zombieUnits.Add(this);
        else if (team == Team.Player)
            playerUnits.Add(this);
    }

    private void UnregisterUnit()
    {
        playerUnits.Remove(this);
        zombieUnits.Remove(this);
    }

    // ── IDamageable Interface ──

    public virtual void TakeDamage(float damage, Vector3 hitPosition, Vector3 hitDirection, Vector3 shooterPosition)
    {
        if (!IsServer) return;

        networkHealth.Value -= damage;
        ApplyKnockback(damage, hitDirection);

        // tell all clients to show effects
        SpawnHitEffectsClientRpc(damage, hitPosition, hitDirection);

        if (networkHealth.Value <= 0)
        {
            Die(damage, hitDirection, hitPosition);
        }
    }
    
    // ── Damage Internals ──

    [ClientRpc]
    private void SpawnHitEffectsClientRpc(float damage, Vector3 hitPosition, Vector3 hitDirection)
    {
        SpawnHitMarker(hitPosition);
        SpawnDamagePopup(damage, hitPosition);
    }

    private void SpawnHitMarker(Vector3 hitPosition)
    {
        GameObject prefab = GameAssets.Instance.bloodSplatterPrefab;
        if (prefab == null) return;

        Instantiate(prefab, hitPosition, Quaternion.identity);
    }

    private void SpawnDamagePopup(float damage, Vector3 hitPosition)
    {
        GameObject prefab = GameAssets.Instance.damagePopupPrefab;
        if (prefab == null) return;

        Vector3 pos = transform.position + Vector3.up * 1.5f;
        pos += new Vector3(Random.Range(-0.3f, 0.3f), 0, Random.Range(-0.3f, 0.3f));

        GameObject popup = Instantiate(prefab, pos, Quaternion.identity);
        TextMeshPro tmp = popup.GetComponentInChildren<TextMeshPro>();
        if (tmp != null)
        {
            tmp.text = damage.ToString("F1");
            Team currentTeam = networkTeam.Value;
            if (currentTeam == Team.Zombie)
                tmp.color = ZombieDamageColor;
            else if (currentTeam == Team.Player)
                tmp.color = PlayerDamageColor;
        }
    }

    protected void ApplyKnockback(float damage, Vector3 hitDirection)
    {
        if (!IsServer) return;

        float force = damage * 0.05f;
        force = Mathf.Clamp(force, 0.5f, 3f);

        _knockbackVelocity = hitDirection.normalized * force;
        _knockbackVelocity.y = 0;
        _knockbackTimer = _knockbackDuration;
    }

    // ── Death ──

    protected virtual void Die(float damage, Vector3 hitDirection, Vector3 hitPosition)
    {
        if (!IsServer) return;

        networkAlive.Value = false;
        UnregisterUnit();
        OnUnitDied?.Invoke(this);

        // change layer on server so physics/targeting ignores this unit
        SetLayerRecursive(gameObject, LayerMask.NameToLayer("Inactive"));

        // tell clients to play ragdoll with the hit info
        DieClientRpc(damage, hitDirection, hitPosition);

        // despawn after delay
        Invoke(nameof(DespawnUnit), 5f);
    }

    [ClientRpc]
    private void DieClientRpc(float damage, Vector3 hitDirection, Vector3 hitPosition)
    {
        HandleDeathVisuals(damage, hitDirection, hitPosition);
    }

    private void OnAliveChanged(bool wasAlive, bool isAlive)
    {
        if (wasAlive && !isAlive)
        {
            // client-side cleanup for unit lists
            UnregisterUnit();
            OnUnitDied?.Invoke(this);
        }
    }

    private void HandleDeathVisuals(float damage, Vector3 hitDirection, Vector3 hitPosition)
    {
        // stop NetworkTransform for ragdoll
        var netTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (netTransform != null)
        {
            netTransform.enabled = false;
        }

        // disable colliders
        Collider col = GetComponent<Collider>();
        if (col != null)
            col.enabled = false;

        SetLayerRecursive(gameObject, LayerMask.NameToLayer("Inactive"));

        if (agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.isStopped = true;
            agent.enabled = false;
        }

        _animator.enabled = false;
        SetRagdoll(true, hitDirection, hitPosition, damage);
    }

    private void DespawnUnit()
    {
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }

    // ── Helpers ──
    private void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private void SetRagdoll(bool active, Vector3 hitDirection = default, Vector3 hitPosition = default, float damage = default)
    {
        if (active && IsServer && !IsHost) return;
        
        foreach (Rigidbody rb in ragdollBodies)
        {
            rb.isKinematic = !active;
            if (active)
            {
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                rb.linearDamping = 1f;
                rb.angularDamping = 1f;
            }
            else
            {
                if (rb.gameObject != gameObject)
                    rb.isKinematic = true;
            }
        }

        foreach (Collider col in ragdollColliders)
        {
            if (!active)
            {
                if (col.gameObject != gameObject)
                    col.enabled = false;
            }
            else
            {
                if (col.gameObject == gameObject)
                    continue;
                col.enabled = active;
            }
        }

        if (active && hitDirection != Vector3.zero)
        {
            float forceMultiplier = 5;  // In place for damage
            float force = 5 * forceMultiplier;
            force = Mathf.Clamp(force, 2f, 50f);

            Vector3 forceDir = hitDirection.normalized + Vector3.down * 0.5f;

            foreach (Rigidbody rb in ragdollBodies)
                rb.AddForce(Vector3.down * 10f, ForceMode.VelocityChange);

            Rigidbody closestBone = null;
            float closestDist = Mathf.Infinity;

            foreach (Rigidbody rb in ragdollBodies)
            {
                float dist = (rb.transform.position - hitPosition).sqrMagnitude;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestBone = rb;
                }
            }

            if (closestBone != null)
            {
                closestBone.AddForceAtPosition(
                    forceDir * force,
                    hitPosition,
                    ForceMode.Impulse);
            }
        }

        if (active)
        {
            foreach (CharacterJoint joint in GetComponentsInChildren<CharacterJoint>())
            {
                joint.enablePreprocessing = false;

                var low = joint.lowTwistLimit;
                low.limit = -20f;
                joint.lowTwistLimit = low;

                var high = joint.highTwistLimit;
                high.limit = 20f;
                joint.highTwistLimit = high;

                var swing1 = joint.swing1Limit;
                swing1.limit = 30f;
                joint.swing1Limit = swing1;

                var swing2 = joint.swing2Limit;
                swing2.limit = 30f;
                joint.swing2Limit = swing2;
            }
        }
    }
}