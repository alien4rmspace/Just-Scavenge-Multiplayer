# Just Scavenge — Multiplayer Systems


https://github.com/user-attachments/assets/2d396fcf-123f-4a04-8fa1-3a79c4493217


Public scripts from a top-down co-op zombie survival extraction game 
built with Unity Netcode for GameObjects and PlayFab cloud servers.

> Full game is in a private repository. This repo contains the core 
> networking, AI, and server infrastructure scripts.

---

## Architecture Overview
```
Client
  → PlayFab Login & Matchmaking
    → Azure Function (find/allocate server)
      → PlayFab Dedicated Server (Docker container)
        → Unity NGO (server-authoritative gameplay)
```

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
| `Unit.cs` | Base NetworkBehaviour — health, death, ragdoll, knockback, damage popups |
| `PlayerUnit.cs` | Ownership-aware input, ServerRpc movement, formation logic |
| `ZombieUnit.cs` | Server-authoritative AI — target finding, NavMesh, alert system, wander |

### AI
| Script | Description |
|---|---|
| `FindTarget.cs` | OverlapSphereNonAlloc physics query and playerUnit list-based target search |

### Optimization
| Script | Description |
|---|---|
| `ZombieSyncManager.cs` | Custom 3-tier LOD — manages NetworkTransform sync rate and AI update frequency for 300+ simultaneous zombies |

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

## License
GPL-3.0 — see LICENSE file
```

---
