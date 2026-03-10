using Interfaces;
using Unity.Netcode;
using UnityEngine;

public class Projectile : NetworkBehaviour
{
    private float _damage;
    private float _velocity;
    private float _lifeTime;
    private Vector3 _direction;
    private Vector3 _originPosition;
    private Team _ownerTeam;
    private bool _initialized;

    // ── Lifecycle ──
    void Update()
    {
        if (!_initialized) return;

        float moveDistance = _velocity * Time.deltaTime;

        // only server checks hits to prevent client-side cheating
        if (IsServer)
        {
            if (Physics.Raycast(transform.position, _direction, out RaycastHit hit, moveDistance))
            {
                IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
                if (target != null && !target.IsDead() && target.Team != _ownerTeam)
                {
                    target.TakeDamage(_damage, hit.point, _direction, _originPosition);
                    DespawnProjectile();
                    return;
                }
            }
        }

        // both server and client move for smooth visuals without waiting for sync
        transform.position += _direction * moveDistance;
    }
    
    // ── Public API ──
    
    /// <summary>
    /// Server-only. Call immediately after Spawn().
    /// </summary>
    public void Init(float damage, float velocity, float lifetime, Vector3 direction, Team ownerTeam)
    {
        _damage = damage;
        _velocity = velocity;
        _lifeTime = lifetime;
        _direction = direction;
        _originPosition = transform.position;
        _ownerTeam = ownerTeam;
        _initialized = true;

        // client needs information to replicate
        InitClientRpc(velocity, direction);
        Invoke(nameof(DespawnProjectile), _lifeTime);
    }
    
    public void ResetState()
    {
        _initialized = false;
        _damage = 0;
        _velocity = 0;
        _direction = Vector3.zero;
        _lifeTime = 0;
        _originPosition = Vector3.zero;
        CancelInvoke(nameof(DespawnProjectile));
    }

    // ── Server ──
    private void DespawnProjectile()
    {
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            CancelInvoke(nameof(DespawnProjectile));
            NetworkObject.Despawn(false);
            ProjectilePool.Instance.Return(NetworkObject);
        }
    }

    // ── Client ──
    [ClientRpc]
    private void InitClientRpc(float velocity, Vector3 direction)
    {
        _velocity = velocity;
        _direction = direction;
        _initialized = true;
    }
}