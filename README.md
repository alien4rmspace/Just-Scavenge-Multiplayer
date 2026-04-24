


# Just Scavenge — Multiplayer Systems

Public scripts from a top-down co-op zombie survival extraction game built in Unity.

> This project was built to study multiplayer networking and dedicated server 
> performance in a real game environment. The focus is on designing a 
> server-authoritative architecture, profiling gameplay systems, and testing 
> ways to reduce server CPU usage, network traffic, and simulation overhead.

> Full game is in a private repository. This repo contains selected core 
> networking, AI, and server infrastructure scripts.
> No API keys are exposed in either repository, this includes the commit history.

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

## License
GPL-3.0 — see LICENSE file
```

---
