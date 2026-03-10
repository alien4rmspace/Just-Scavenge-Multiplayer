using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

public class ZombieUnit : Unit
{
    public float detectionRange = 15f;
    public float wanderRadius = 10f;
    public float wanderTimer = 7f;

    private float _timer;
    private float _updateInterval = 0.25f;
    private float _updateTimer;
    private float _attackAngle;
    private Vector3 _lastKnownThreatPosition;
    private bool _alerted;
    private LayerMask _targetLayerMask;
    private FindTarget _findTarget;

    // ── Lifecycle ──
    protected override void Awake()
    {
        base.Awake();
        team = Team.Zombie;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        _updateTimer = Random.Range(0f, _updateInterval);

        float hash = GetInstanceID() * 0.618f;
        _attackAngle = hash % (1.2f * Mathf.PI);

        _targetLayerMask = LayerMask.GetMask("Player");

        _combat = GetComponent<Combat>();
        _findTarget = GetComponent<FindTarget>();
    }
    
    protected override void Update()
    {
        if (IsDead()) return;

        base.Update();

        // all AI runs on server only
        if (!IsServer) return;

        _timer += Time.deltaTime;
        _updateTimer += Time.deltaTime;
        
        // scale AI frequency by sync tier
        byte tier = ZombieSyncManager.GetTier(NetworkObjectId);
        float interval = tier switch
        {
            1 => 0.25f,   // close/unassigned — full rate
            2 => 1.5f,
            3 => 5.0f,
            _ => 0.25f
        };
        
        if (_updateTimer < interval) return;
        _updateTimer = 0f;

        target = _findTarget.FindClosestInList(transform.position, detectionRange);

        if (target != null)
        {
            _alerted = false;
            agent.stoppingDistance = _combat.weapon.range * 0.9f;
            agent.SetDestination(target.transform.position);
        }
        else if (_alerted)
        {
            agent.SetDestination(_lastKnownThreatPosition);

            if (agent.remainingDistance < 1f)
            {
                _alerted = false;
            }
        }
        else
        {
            Wander();
        }
    }

    // ── Network Lifecycle ──
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            ZombieSyncManager.Register(
                NetworkObjectId,
                GetComponent<NetworkTransform>(),
                this);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (IsServer)
        {
            ZombieSyncManager.Unregister(NetworkObjectId);
        }
    }

    // ── Public Methods ──
    public override void TakeDamage(float damage, Vector3 hitPosition, Vector3 hitDirection, Vector3 shooterPosition)
    {
        if (!IsServer) return;

        // alert before applying damage so the zombie reacts even if it dies
        float inaccuracyRadius = 5f;
        Vector3 randomOffset = Random.insideUnitCircle * inaccuracyRadius;
        randomOffset.y = 0;
        AlertToPosition(shooterPosition + randomOffset);

        base.TakeDamage(damage, hitPosition, hitDirection, shooterPosition);
    }

    public void AlertToPosition(Vector3 position)
    {
        // server only — called by TakeDamage or nearby zombies
        if (!IsServer) return;

        _lastKnownThreatPosition = position;
        _alerted = true;
    }

    // ── Helpers ──
    private void Wander()
    {
        if (_timer >= wanderTimer)
        {
            float chanceThreshold = 0.25f;
            if (Random.value < chanceThreshold)
            {
                Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
                Vector3 randomPoint = transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);
                if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                    agent.SetDestination(hit.position);
            }

            _timer = 0;
        }
    }
}