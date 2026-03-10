using Unity.Netcode;
using UnityEngine;

public class FindTarget : NetworkBehaviour
{
    private readonly Collider[] _detectionResults = new Collider[200];

    // ── Player FindTarget ──
    public Unit FindClosestWithOverlapSphere(float detectionRange, LayerMask targetLayerMask)
    {
        if (!IsServer) return null;

        int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRange, _detectionResults, targetLayerMask);

        float closestDistance = Mathf.Infinity;
        Unit closest = null;

        for (int i = 0; i < count; i++)
        {
            float dist = (transform.position - _detectionResults[i].transform.position).sqrMagnitude;
            if (dist < closestDistance)
            {
                closestDistance = dist;
                closest = _detectionResults[i].GetComponentInParent<Unit>();
            }
        }
        return closest;
    }
    
    // ── Zombie FindTarget ──
    // Separate find target logic from player units.
    // Increases efficiency since there aren't many player units compared to zombies.
    public Unit FindClosestInList(Vector3 position, float range)
    {
        if (!IsServer) return null;

        float rangeSqr = range * range;
        float closestDist = float.MaxValue;
        Unit closest = null;

        foreach (Unit player in Unit.playerUnits)
        {
            if (player == null || player.IsDead()) continue;
            float distSqr = (position - player.transform.position).sqrMagnitude;
            if (distSqr < closestDist && distSqr < rangeSqr)
            {
                closestDist = distSqr;
                closest = player;
            }
        }

        return closest;
    }
}