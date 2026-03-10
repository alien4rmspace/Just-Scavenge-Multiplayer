using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class PlayerUnit : Unit
{
    public float detectionRange;
    public bool isSelected;
    public GameObject selectionIndicator;

    private float _updateInterval = 0.25f;
    private float _updateTimer;
    private LayerMask _targetLayerMask;
    private FindTarget _findTarget;

    // ── Lifecycle ──
    protected override void Awake()
    {
        base.Awake();
        team = Team.Player;

        _findTarget = GetComponent<FindTarget>();
        _targetLayerMask = LayerMask.GetMask("Zombie");
    }

    protected override void Update()
    {
        base.Update();

        // targeting logic runs on server only
        if (!IsServer) return;

        _updateTimer += Time.deltaTime;
        if (_updateTimer < _updateInterval) return;
        _updateTimer = 0f;

        if (_combat != null && _combat.weapon != null)
        {
            detectionRange = _combat.weapon.range;
        }
        else
        {
            detectionRange = 10f;
        }

        if (agent.velocity.sqrMagnitude > 0.1f)
        {
            target = null;
        }
        else
        {
            target = _findTarget.FindClosestWithOverlapSphere(detectionRange, _targetLayerMask);
        }
    }

    // ── Selection (client only) ───
    public void SetSelected(bool selection)
    {
        isSelected = selection;
        if (selectionIndicator != null)
        {
            selectionIndicator.SetActive(selection);
        }
    }

    // ── Movement (Client) ──
    public void MoveTo(Vector3 destination)
    {
        SpawnMoveIndicator(destination);
        MoveToServerRpc(destination);
    }
    
    private void SpawnMoveIndicator(Vector3 position)
    {
        GameObject prefab = GameAssets.Instance.moveIndicatorPrefab;
        if (prefab == null) return;

        // slight offset above ground to avoid z-fighting
        Vector3 spawnPos = position + Vector3.up * 0.05f;
        Instantiate(prefab, spawnPos, Quaternion.Euler(90f, 0f, 0f));
    }

    // ── Movement (Server) ──
    [ServerRpc(RequireOwnership = false)]
    private void MoveToServerRpc(Vector3 destination)
    {
        if (!networkAlive.Value) return;

        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
    }
}