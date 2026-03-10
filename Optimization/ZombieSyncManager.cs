using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 3-tier NetworkTransform LOD system:
///   Close    → NetworkTransform enabled
///   Medium   → NetworkTransform enabled & throttled
///   Far      → NetworkTransform disabled 7 throttled
///
/// AI tick rate is controlled per-tier via GetTier() in ZombieUnit.
/// Close zombies get full sync + fast AI. Medium get full sync + slower AI, while far gets no sync
/// and since their AI runs less often they barely move, saving bandwidth naturally.
/// </summary>
public class ZombieSyncManager : NetworkBehaviour
{
    public static ZombieSyncManager Instance;

    [Header("Tier Distances")]
    public float closeDist = 40f;
    public float farDist = 60f;

    [Header("Tier Assignment")]
    public float tierUpdateInterval = 1.0f;
    public int tierBatchSize = 125;
    
    private struct ZombieEntry
    {
        public Unit unit;
        public NetworkTransform netTransform;
        public byte currentTier; // 0=unassigned, 1=close, 2=medium, 3=far
    }

    private static readonly Dictionary<ulong, ZombieEntry> _zombieEntries
        = new Dictionary<ulong, ZombieEntry>(300);
    
    private float _tierTimer;
    private int _tierBatchIndex;

    private float _closeDistSqr;
    private float _farDistSqr;

    // ── Lifecycle ──
    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _closeDistSqr = closeDist * closeDist;
        _farDistSqr   = farDist * farDist;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _zombieEntries.Clear();
    }

    void Update()
    {
        if (!IsServer) return;

        _tierTimer += Time.deltaTime;
        float tierFrameInterval = tierUpdateInterval
                                  / Mathf.Max(1f, Mathf.Ceil((float)Unit.zombieUnits.Count / tierBatchSize));

        if (_tierTimer >= tierFrameInterval)
        {
            _tierTimer = 0f;
            ProcessTierBatch();
        }
    }
    
    // ── Public API ──
    public static void Register(ulong netId, NetworkTransform nt, Unit unit)
    {
        _zombieEntries[netId] = new ZombieEntry
        {
            unit = unit,
            netTransform = nt,
            currentTier = 0
        };
    }

    public static void Unregister(ulong netId)
    {
        _zombieEntries.Remove(netId);
    }
    
    public static byte GetTier(ulong networkObjectId)
    {
        if (_zombieEntries.TryGetValue(networkObjectId, out ZombieEntry entry))
            return entry.currentTier;
        return 0;
    }

    // ── Server Functions ──
    private void ProcessTierBatch()
    {
        int count = Unit.zombieUnits.Count;
        if (count == 0)
        {
            _tierBatchIndex = 0;
            return;
        }

        int end = Mathf.Min(_tierBatchIndex + tierBatchSize, count);

        for (int i = _tierBatchIndex; i < end; i++)
        {
            Unit zombie = Unit.zombieUnits[i];
            if (zombie == null || zombie.IsDead()) continue;

            ulong netId = zombie.NetworkObjectId;
            if (!_zombieEntries.TryGetValue(netId, out ZombieEntry entry)) continue;

            float distSqr = ClosestPlayerDistSqr(zombie.transform.position);

            byte newTier;
            if (distSqr < _closeDistSqr)
            {
                newTier = 1;
            }
            else if (distSqr < _farDistSqr)
            {
                newTier = 2;

            }
            else
            {
                newTier = 3;
            }

            if (entry.currentTier == newTier) continue;

            entry.currentTier = newTier;
            _zombieEntries[netId] = entry;

            if (entry.netTransform != null)
                entry.netTransform.enabled = (newTier <= 2);
        }

        _tierBatchIndex = end >= count ? 0 : end;
    }

    // ── Helpers ──
    private float ClosestPlayerDistSqr(Vector3 position)
    {
        float closest = float.MaxValue;

        foreach (Unit player in Unit.playerUnits)
        {
            if (player == null || player.IsDead()) continue;
            float distSqr = (position - player.transform.position).sqrMagnitude;
            if (distSqr < closest)
                closest = distSqr;
        }

        return closest;
    }
}