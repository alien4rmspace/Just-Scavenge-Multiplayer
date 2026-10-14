


# Just Scavenge — Multiplayer Systems

Public scripts from a top-down co-op zombie survival extraction game built in Unity.

> This project was built to study multiplayer networking and dedicated server 
> performance in a real game environment. The focus is on designing a 
> server-authoritative architecture, profiling gameplay systems, and testing 
> ways to reduce server CPU usage, network traffic, and simulation overhead.

> Full game is in a private repository. This repo contains selected core 
> networking, AI, and server infrastructure scripts.
> No API/secret keys are exposed in either repository.

## Architecture Overview
```
Client
  → PlayFab Login & Matchmaking
    → Azure Function (hide api keys from public and find/allocate server)
      → PlayFab Dedicated Server (Docker container)
        → Unity NGO (server-authoritative gameplay)
```
https://github.com/user-attachments/assets/2cb66ee2-95bf-4b0e-9161-4768dece1be6
> Gameplay demo showing the current in-game systems, including player movement, combat, enemy behavior, and core mechanics.
---

## Tech Stack

| System | Technology |
|---|---|
| Multiplayer | Unity Netcode for GameObjects |
| Cloud Servers | PlayFab Multiplayer Servers |
| Server Lifecycle | PlayFab GSDK |
| Containerization | Docker (Linux) |
| Backend | Azure Functions |
| Authentication | PlayFab Client API |

---

## Scripts

### Networking
| Script | Description |
|---|---|
| `ServerBootstrap.cs` | GSDK lifecycle, PlayFab allocation, NGO server startup, CPU throttling |

### Units
| Script | Description |
|---|---|
| `Unit.cs` | Base NetworkBehaviour: health, death, ragdoll, knockback, damage popups |
| `PlayerUnit.cs` | Ownership-aware input, ServerRpc movement, formation logic |
| `ZombieUnit.cs` | Server-authoritative AI: target finding, NavMesh, alert system, wander |

### AI
| Script | Description |
|---|---|
| `FindTarget.cs` | OverlapSphereNonAlloc physics query and playerUnit list-based target search |

### Optimization
| Script | Description |
|---|---|
| `ZombieSyncManager.cs` | Custom 3-tier LOD: manages NetworkTransform sync rate and AI update frequency for 300+ simultaneous zombies |

### Combat
| Script | Description |
|---|---|
| `Projectile.cs` | Server-authoritative hit detection with client-side visual prediction |
| `ProjectilePool.cs` | Object pooling integrated with NGO for projectile reuse |

---

## Key Systems

### Server-Authoritative Architecture
All gameplay logic runs on the dedicated server. Clients send 
inputs via ServerRpc and receive state updates via NetworkVariables 
and ClientRpc. Projectile hit detection, damage, and death are 
all server-side.

### Zombie LOD System (ZombieSyncManager)
Custom level-of-detail system that reduces CPU and bandwidth 
overhead for large zombie counts based on the distance from players:
```
Tier 1  → full AI rate (4hz), NetworkTransform enabled
Tier 2  → reduced AI rate (0.6hz), NetworkTransform enabled  
Tier 3  → minimal AI rate (0.2hz), NetworkTransform disabled
```

Tier assignments are batched across frames to avoid single-frame 
spikes. Profiled at 0.15ms for 203 simultaneous zombies.

### Cloud Server Pipeline
```
Unity Linux build → Docker container → PlayFab build
  → GSDK heartbeat → allocation callback
    → port resolution → NGO StartServer
      → client connects via Azure Function matchmaking
        → empty server timeout → Application.Quit
```

### Server Performance (D2asv4, 2 vCPU)
```
CPU per instance:    ~18% (top) with 300 zombies
Memory per instance: ~300MB
Safe instances/VM:   8-12 depending on workload
```

---

## Planned Systems
- Chunk instancing for large maps (1000+ zombies)
- Player LOD (NetworkTransform tier system)
- Burst compiled Jobs for spatial queries
- A* Pathfinding Pro (multithreaded pathfinding)

---
## Optimizations

### Chunk-Based Loot Activation

- Problem: Keeping every loot container active in a large open world would make loot scanning and player interactions less scalable.
- Change: Added chunk-aware loot sockets so each chunk activates only a limited number of loot containers based on ratio, min, and max settings.
- Result: The world can contain many loot containers while only a controlled subset becomes active per chunk.
- Evidence: `activeSocketRatioPerChunk`, `minActiveSocketsPerChunk`, and `maxActiveSocketsPerChunk` control active loot density.
- Relevant files: `Assets/Scripts/GameAssets/LootSocketManager.cs`, `Assets/Scripts/GameAssets/LootSocket.cs`

### Timestamp-Based Loot Restocking

- Problem: Continuously polling active chunks for restock timers wastes work and can cause loot to respawn while players are still nearby.
- Change: Replaced active restock polling with a per-chunk `RestockReadyTime` timestamp that is only checked when a chunk reloads.
- Result: Restock timing is cheap to track and avoids repeated active-chunk respawn checks.
- Evidence: Restock uses `Time.unscaledTimeAsDouble` plus a stored ready timestamp instead of per-container timers or coroutines.
- Relevant files: `Assets/Scripts/GameAssets/LootSocketManager.cs`

### Reload-Only Loot Respawn

- Problem: Respawning loot in active chunks can feel artificial and requires more runtime checks around player presence.
- Change: Loot only refills after a depleted chunk unloads and later reloads, once its restock timestamp has matured.
- Result: Loot respawn work happens at natural chunk boundaries instead of during active gameplay.
- Evidence: Chunk state tracks whether it unloaded after the restock timer was scheduled before allowing refill.
- Relevant files: `Assets/Scripts/GameAssets/LootSocketManager.cs`

### Simplified Loot Socket Cooldowns

- Problem: Per-socket cooldowns created extra state and duplicated the chunk restock pacing system.
- Change: Removed individual loot socket respawn timers so restock timing is handled at the chunk level.
- Result: Less state to track per container and simpler restock behavior.
- Evidence: Loot sockets no longer maintain their own cooldown timers; chunk timing controls restock eligibility.
- Relevant files: `Assets/Scripts/GameAssets/LootSocket.cs`, `Assets/Scripts/GameAssets/LootSocketManager.cs`

### Zombie Chunk Snapshot Clearing For Fresh Hosts

- Problem: Old saved chunk snapshots could restore stale zombie state when starting a new host session.
- Change: Added host-start clearing for saved zombie chunk snapshot files.
- Result: Host sessions start fresh without loading unnecessary persisted zombie data from previous runs.
- Evidence: `clearSnapshotsOnHostStart` deletes saved chunk snapshot JSON files before loading persistence.
- Relevant files: `Assets/Scripts/ZombieChunkManager.cs`

### Chunk-Gated Zombie Spawning

- Problem: Zombie spawners used proximity trigger colliders, which could activate independently of the chunk simulation system.
- Change: Replaced activation-radius spawning with active chunk checks, using spawn points and final NavMesh positions that must be in valid zombie spawn chunks.
- Result: Spawning now scales with active chunks around players instead of arbitrary trigger overlap, reducing unnecessary spawn attempts outside relevant gameplay areas.
- Evidence: Added `IsWorldPositionInZombieSpawnChunk(...)`; `ZombieSpawner` now filters spawn points through that query.
- Relevant files: `Assets/Scripts/ZombieSpawner.cs`, `Assets/Scripts/ZombieChunkManager.cs`

### Disabled Inactive Zombie Spawners

- Problem: Thousands of zombie spawners could still run cheap per-frame checks even when their chunks were not eligible for spawning.
- Change: Disabled inactive `ZombieSpawner` components and moved spawner activation refresh into `ZombieChunkManager` on the existing chunk refresh interval.
- Result: Only spawners with valid spawn points in active, non-player chunks run `Update()`, improving CPU scalability for large maps.
- Evidence: Spawners cache spawnable points and are enabled or disabled by chunk state instead of scanning their spawn points every frame.
- Relevant files: `Assets/Scripts/ZombieSpawner.cs`, `Assets/Scripts/ZombieChunkManager.cs`

### Per-Frame Chunk Query Cache

- Problem: Multiple spawners can query active chunks in the same frame, risking repeated chunk-set recalculation.
- Change: Added `_lastChunkSetRefreshFrame` and `RefreshChunkSetsForQuery()` so chunk sets refresh at most once per frame for query calls.
- Result: Reduces redundant CPU work when many zombie spawners ask whether their spawn points are active.
- Evidence: Chunk queries now call `RefreshChunkSetsForQuery()` before checking `_activeChunks`, `_currentChunks`, and `_graceChunks`.
- Relevant files: `Assets/Scripts/ZombieChunkManager.cs`

### Player-Chunk Spawn Exclusion

- Problem: Zombies could spawn very close to players when spawn points were in adjacent or border-near chunks.
- Change: Blocked zombie spawning in player-occupied chunks and border grace chunks.
- Result: Reduces immediate close-range spawn pressure while keeping nearby chunks active for simulation/restoration.
- Evidence: `IsWorldPositionInZombieSpawnChunk(...)` excludes `_currentChunks` and `_graceChunks`; grace distance was tuned to `17f`.
- Relevant files: `Assets/Scripts/ZombieChunkManager.cs`, `Assets/Scripts/ZombieSpawner.cs`

### Larger Zombie Chunks

- Problem: Smaller 25-unit chunks caused more frequent chunk boundary transitions and chunk bookkeeping.
- Change: Increased default zombie chunk size from `25f` to `30f`.
- Result: Fewer chunk transitions and a larger active area per chunk update, reducing churn in chunk activation/snapshot logic.
- Evidence: `chunkSize` default changed to `30f`.
- Relevant files: `Assets/Scripts/ZombieChunkManager.cs`

### NavMesh Exclusion For Loot Prefabs

- Problem: Loot container geometry could contribute walkable NavMesh surfaces, creating unnecessary or invalid pathable islands.
- Change: The loot container prefab creator now automatically adds a `NavMeshModifier` set to `Not Walkable` with `applyToChildren = true`.
- Result: Cleaner NavMesh bakes with fewer unwanted walkable prop surfaces, reducing bad pathfinding targets and NavMesh sampling mistakes.
- Evidence: `EnsureNotWalkableNavMeshModifier(...)` added to prefab creation flow.
- Relevant files: `Assets/Scripts/Editor/LootContainerPrefabCreatorEditor.cs`

### Single-Unit Loot Lock

- Problem: Multiple units could loot the same container at the same time, causing duplicate interaction work and competing server-side loot processing.
- Change: Added a server-owned `networkLootingUnitId` lock on each loot container.
- Result: Only one unit can process loot from a container at a time, preventing redundant concurrent loot timers and inventory operations.
- Evidence: Added `TryBeginLooting(...)`, `EndLooting(...)`, and interactor-side lock checks/release paths.
- Relevant files: `Assets/Scripts/GameAssets/LootContainer.cs`, `Assets/Scripts/Inventory/LootContainerInteractor.cs`

### Flow Field Proximity Lookup

- Problem: Idle zombies checked every propagation origin, creating active-zombies x alerted-zombies work every tick.
- Change: Reworked flow-field coverage checks to use precomputed/indexed field data instead of scanning every origin.
- Result: Reduced zombie flow-field eligibility checks toward constant-time lookup.
- Evidence: Removed the quadratic proximity loop identified in `CanUseFlowField`.
- Relevant files: `Assets/Scripts/ZombieFlowFieldManager.cs`

### Spatial Zombie Targeting

- Problem: Player targeting scanned every active zombie and performed line-of-sight checks on a 0.25s tick.
- Change: Added active zombie spatial indexing so target scans query nearby candidates first.
- Result: Reduced broad zombie iteration and unnecessary LOS raycasts as zombie counts scale.
- Evidence: Target search now uses spatially narrowed zombie candidates.
- Relevant files: `Assets/Scripts/FindTarget.cs`, `Assets/Scripts/Unit.cs`

### Cached Move Indicator Paths

- Problem: Move indicators recalculated full NavMesh paths every frame for selected units.
- Change: Cached NavMesh paths and rebuilt only on a timer or when unit/destination movement exceeded thresholds.
- Result: Lower client CPU and NavMesh pathfinding cost while keeping the indicator responsive.
- Evidence: Path rebuild interval and distance thresholds now gate `NavMesh.CalculatePath`.
- Relevant files: `Assets/Scripts/GameAssets/MoveIndicator.cs`

### O(1) Inventory Owner Lookup

- Problem: Inventory lookup scanned all spawned `NetworkObject`s and called `GetComponent` repeatedly.
- Change: Registered `PlayerInventory` instances in a static dictionary by owner client id on spawn/despawn.
- Result: Owner inventory lookups became O(1), reducing repeated combat and loot lookup cost.
- Evidence: `FindByOwnerClientId` now reads from `Dictionary<ulong, PlayerInventory>`.
- Relevant files: `Assets/Scripts/Inventory/PlayerInventory.cs`

### Cached Loot Progress Anchor

- Problem: Loot progress UI recomputed child renderers/colliders and bounds every visible frame.
- Change: Cached target anchor bounds when the loot target changes.
- Result: Removed repeated `GetComponentsInChildren` work and per-frame allocations while looting.
- Evidence: Anchor renderer/collider lists are cached per target.
- Relevant files: `Assets/Scripts/Inventory/LootContainerInteractor.cs`

### Optimized Aura Tick Processing

- Problem: Aura effects allocated fresh lists and used repeated linear `Contains` checks each tick.
- Change: Reused collections and switched membership checks to hash-based tracking.
- Result: Reduced per-tick allocations and avoided O(n^2) behavior with multiple buffed units.
- Evidence: Aura tick now uses cached/scratch collections instead of rebuilding lists each pass.
- Relevant files: `Assets/Scripts/Perks/Effects/BoostAuraEffect.cs`

### Cached Perk Controller Access

- Problem: `MaxHealth` repeatedly called `GetComponent<SurvivorPerkController>()` in common damage, healing, UI, and spawn paths.
- Change: Cached the perk controller in `Unit.Awake`.
- Result: Removed repeated component lookups from hot gameplay paths.
- Evidence: `MaxHealth` now reads cached `_cachedPerkController`.
- Relevant files: `Assets/Scripts/Unit.cs`

### Local Animation Speed Derivation

- Problem: Unit movement animation speed was synced through a network variable.
- Change: Removed synced `networkSpeed` and derived animation speed locally from received transform movement.
- Result: Reduced network variable traffic while preserving remote movement animation.
- Evidence: `LocalMovementSpeed` now comes from local position delta or server agent velocity.
- Relevant files: `Assets/Scripts/Unit.cs`, `Assets/Scripts/ZombieUnit.cs`, `Assets/Scripts/GameAssets/MoveIndicator.cs`

### Owner-Only Player State Replication

- Problem: Inventory, hunger/thirst, ammo/reload, effective range, and loot progress state were replicated to every client.
- Change: Switched private player state to owner-readable NetworkVariables/NetworkLists and exposed only a small public equipped-weapon id for remote visuals.
- Result: Reduced replicated state sent to non-owner clients.
- Evidence: Inventory, survival, ammo, reload, range, and loot progress variables now use `NetworkVariableReadPermission.Owner`.
- Relevant files: `Assets/Scripts/Inventory/PlayerInventory.cs`, `Assets/Scripts/PlayerUnit.cs`, `Assets/Scripts/Combat.cs`, `Assets/Scripts/Inventory/LootContainerInteractor.cs`, `Assets/Scripts/WeaponIkDriver.cs`

### Unreliable Cosmetic RPCs

- Problem: Cosmetic shot, reload, hit marker, and damage popup RPCs used reliable delivery, adding pressure to reliable network queues.
- Change: Moved cosmetic-only RPCs to unreliable delivery.
- Result: Cosmetic packet loss no longer blocks gameplay-critical reliable traffic.
- Evidence: Shot/reload and hit-effect RPCs now use `RpcDelivery.Unreliable`.
- Relevant files: `Assets/Scripts/Combat.cs`, `Assets/Scripts/Unit.cs`

### Batched Combat Effects

- Problem: Final-shot reloads and rapid hit effects could send multiple cosmetic RPCs in the same frame.
- Change: Batched shot + reload effects into one RPC and batched up to four hit marker/damage popup events per unit per frame.
- Result: Reduced cosmetic RPC count during burst fire, shotguns, and multi-hit combat.
- Evidence: Hit effects now queue and flush in `LateUpdate`; final-shot reload uses one combined combat effect RPC.
- Relevant files: `Assets/Scripts/Combat.cs`, `Assets/Scripts/Unit.cs`

### Reduced Zombie Transform Packet Noise

- Problem: Very small rotation changes could generate frequent zombie transform updates.
- Change: Increased zombie NetworkTransform rotation threshold.
- Result: Reduced unnecessary rotation sync packets for zombies.
- Evidence: Zombie prefab rotation threshold was raised from extremely sensitive values.
- Relevant files: `Assets/Prefabs/Zombie_Male_01.prefab`

### Chunk-Based Zombie Simulation

- Problem: Zombie CPU and network cost scaled with the total number of spawned zombies, even when most were far away from players.
- Change: Added a `ZombieChunkManager` that divides the world into chunks and keeps only chunks near players active. Introduced `activeZombieUnits`, and off-chunk zombies now enter a dormant state that clears targets, stops movement and AI work, and forces them into the far-sync tier.
- Result: Reduced CPU work and network sync load by limiting AI, targeting, combat, flow-field participation, and tier evaluation to nearby zombies only.
- Evidence: `ZombieSyncManager`, `FindTarget`, and `ZombieFlowFieldManager` were switched from iterating all zombies to iterating `activeZombieUnits`; inactive zombies call `SetChunkActive(false)` and stop `NavMeshAgent` activity.
- Relevant files: `Assets/Scripts/ZombieChunkManager.cs`, `Assets/Scripts/ZombieUnit.cs`, `Assets/Scripts/Unit.cs`, `Assets/Scripts/ZombieSyncManager.cs`, `Assets/Scripts/FindTarget.cs`, `Assets/Scripts/Combat.cs`, `Assets/Scripts/ZombieFlowFieldManager.cs`

### Shared Horde Flow Field

- Problem: Large zombie groups required repeated long-range navigation decisions, which scales poorly when many agents move toward the same threat.
- Change: Added a server-side `ZombieFlowFieldManager` that builds a shared flow field from alert and threat positions and lets untargeted zombies follow shared directions instead of individually solving long-range pursuit.
- Result: Lowered long-range pathfinding cost and improved scalability for horde movement by replacing many redundant decisions with one shared field.
- Evidence: `ZombieUnit.TryFollowFlowField()` reuses one field across many zombies, and the manager is auto-attached through `ZombieSyncManager` so the system stays centralized.
- Relevant files: `Assets/Scripts/ZombieFlowFieldManager.cs`, `Assets/Scripts/ZombieUnit.cs`, `Assets/Scripts/ZombieSyncManager.cs`

### Cached Flow-Field Topology

- Problem: The initial flow-field rebuild re-sampled every cell with `NavMesh.SamplePosition` and re-ran `NavMesh.Raycast` neighbor checks on every update, even though the map obstacles were static.
- Change: Refactored the flow field to cache walkable cells and neighbor connectivity (`connectivityMask`) and only recompute dynamic costs and directions when threat seeds change.
- Result: Reduced repeated NavMesh work and lowered CPU spikes from periodic flow-field rebuilds.
- Evidence: Static topology is rebuilt only when bounds or settings change; normal updates reuse cached cells and run only dynamic reseeding and propagation.
- Relevant files: `Assets/Scripts/ZombieFlowFieldManager.cs`

### Tier-2 Target Scan Reduction

- Problem: Medium-distance zombies were still performing fresh target-acquisition scans even when they were outside practical engagement range, causing unnecessary list iteration and line-of-sight checks.
- Change: Added a `CanAcquireNewTarget()` gate so tier 2 zombies keep valid existing aggro but stop doing new `FindClosestPlayerInList()` scans; stale targets are cleared cleanly and the zombie falls back to alert, flow-field, or wander behavior.
- Result: Reduced target-search overhead and LOS raycast pressure for medium-range zombies without breaking combat continuity.
- Evidence: `ZombieUnit.Update()` now validates existing targets first and only allows new player scans for non-tier-2 zombies.
- Relevant files: `Assets/Scripts/ZombieUnit.cs`, `Assets/Scripts/FindTarget.cs`

### Dormant Zombie Combat Suppression

- Problem: Far zombies could still incur per-frame combat update overhead even when they should not be participating in gameplay.
- Change: Added a chunk-active guard in `Combat.Update()` so dormant zombies return before running reload, attack, and rotation logic.
- Result: Removed unnecessary CPU work from inactive zombies and made chunk-based sleeping more effective.
- Evidence: `Combat.Update()` now exits immediately when the owning `ZombieUnit` is not chunk-active.
- Relevant files: `Assets/Scripts/Combat.cs`, `Assets/Scripts/ZombieUnit.cs`

### Batched Projectile Shot Events

- Problem: Every ranged shot created a networked projectile lifecycle on each client, including spawn, initialization, and despawn traffic. That added unnecessary packet volume and scaled poorly with high fire rates, shotgun-style weapons, and split projectiles.
- Change: Replaced per-shot network object spawning with a batched `ShotEvent` flow. `Combat` now creates `ProjectileLaunchData`, the server queues shots in `ProjectilePool`, and NGO sends them as a named custom message batch using `UnreliableSequenced`. Clients replay the shots locally as visual-only projectiles.
- Result: Reduced per-bullet network overhead, cut projectile-related message fanout, and made combat replication scale with compact shot batches instead of one network object lifecycle per projectile.
- Evidence: `RangedAttack` no longer calls `NetworkObject.Spawn()` and now uses `ProjectilePool.Instance.SpawnProjectile(...)`; shot batching is implemented through `ProjectileShotBatch` and `CustomMessagingManager.SendNamedMessageToAll(..., NetworkDelivery.UnreliableSequenced)`.
- Relevant files: `Assets/Scripts/Combat.cs`, `Assets/Scripts/ProjectilePool.cs`, `Assets/Scripts/Projectile.cs`

### Local Projectile Simulation and Pooling

- Problem: Projectiles were treated as networked objects even though their flight was mostly local simulation, which added network-object bookkeeping and lifecycle overhead beyond what the gameplay required.
- Change: Converted `Projectile` from a `NetworkBehaviour` to a plain `MonoBehaviour`, added `Authority` and `VisualOnly` simulation modes, and refactored `ProjectilePool` to pool `Projectile` instances directly instead of pooling `NetworkObject`s. Server projectiles handle damage, status effects, pierce, bounce, and split logic locally while clients only render the visual result.
- Result: Lowered CPU and object-lifecycle overhead per shot, reduced churn from repeatedly creating and tearing down networked projectile state, and preserved authoritative gameplay without paying full network-object overhead for every bullet.
- Evidence: `Projectile` now runs as a local `MonoBehaviour` with `ProjectileSimulationMode` and `Initialize(...)`, while `ProjectilePool` now uses `ObjectPool<Projectile>` and local `Return(Projectile)` rather than `ObjectPool<NetworkObject>`.
- Relevant files: `Assets/Scripts/Projectile.cs`, `Assets/Scripts/ProjectilePool.cs`, `Assets/Scripts/Combat.cs`

## License
GPL-3.0 — see LICENSE file
```

---
