using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Object pool for networked projectiles.
/// Register this pool before any projectiles spawn (e.g. in Awake or OnNetworkSpawn).
///
/// Usage:
///   1. Add this component to a GameObject in the scene
///   2. Assign the projectile prefab (must have NetworkObject)
///   3. Set initialSize to pre-warm the pool
///
/// Spawn() and Despawn() through this handler.
///
/// IMPORTANT: The prefab must be registered with NetworkManager BEFORE any spawns occur.
/// </summary>
public class ProjectilePool : MonoBehaviour
{
    // Instance
    public static ProjectilePool Instance;
    
    // Inspector Fields
    [Header("Pool Settings")]
    public GameObject projectilePrefab;
    public int initialPoolSize = 500;
    
    // Private Fields
    private Queue<NetworkObject> _pool = new Queue<NetworkObject>();
    private uint _prefabHash;

    // ── Lifecycle ──
    void Awake()
    {
        Instance = this;
        
        for (int i = 0; i < initialPoolSize; i++)
        {
            GameObject projectile = Instantiate(projectilePrefab);
            projectile.SetActive(false);
            _pool.Enqueue(projectile.GetComponent<NetworkObject>());
        }
    }

    // ── Public API ──
    /// <summary>
    /// Called instead of Instantiate when spawning this prefab.
    /// Returns a pooled instance or creates a new one if the pool is empty.
    /// </summary>
    public NetworkObject Get(Vector3 position, Quaternion rotation)
    {
        NetworkObject networkObject;

        if (_pool.Count > 0)
        {
            networkObject = _pool.Dequeue();
        }
        else
        {
            GameObject obj = Instantiate(projectilePrefab);
            networkObject = obj.GetComponent<NetworkObject>();
            Debug.Log("Instantiating into pool " + networkObject.PrefabIdHash);

        }
        
        networkObject.transform.SetPositionAndRotation(position, rotation);
        networkObject.gameObject.SetActive(true);
        return networkObject;
    }

    /// <summary>
    /// Called instead of Destroy when despawning this prefab.
    /// Deactivates and returns the instance to the pool.
    /// </summary>
    public void Return(NetworkObject netObj)
    {
        netObj.gameObject.SetActive(false);
        ResetProjectile(netObj);
        _pool.Enqueue(netObj);
    }

    // ── Internal ──
    private void ResetProjectile(NetworkObject networkObject)
    {
        Projectile projectile = networkObject.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.ResetState();
        }
    }
}
