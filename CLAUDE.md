# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Pico Park-style 2D co-op puzzle platformer built in Unity, using Netcode for GameObjects (NGO) with a dedicated server hosted on GCP. There is no Unity Editor scene/prefab content committed yet — only C# scripts and `ProjectSettings`/`Packages` config. Scenes, prefabs (Player, Button, Door), and the `NetworkManager` GameObject still need to be assembled by hand in the Unity Editor before the project can run.

- Unity: **2021.3.45f2** (`ProjectSettings/ProjectVersion.txt`)
- Netcode: `com.unity.netcode.gameobjects` 1.9.1 + `com.unity.transport` 2.2.1 (`Packages/manifest.json`)

## Commands

There is no CLI build/test/lint pipeline in this repo — it's a Unity project, worked on primarily through the Unity Editor. There is no test suite.

- **Server build**: Unity Editor → `File > Build Settings` → platform `Dedicated Server`, Target OS `Linux` → produces a `.x86_64` binary.
- **Client build**: same dialog, platform `Windows, Mac, Linux` (regular player build) → produces a `.exe`.
- **Deploy to the dedicated server VM**:
  ```
  gcloud compute scp --recurse ServerBuild filerpark-game-server:~ --project=filerpark --zone=asia-northeast3-a
  gcloud compute ssh filerpark-game-server --project=filerpark --zone=asia-northeast3-a
  chmod +x <binary>.x86_64
  nohup ./<binary>.x86_64 -batchmode -nographics &
  ```

## Architecture

**Server-authoritative throughout.** The server (or host) owns physics and game-state decisions; clients send input and render whatever the server decides. This is deliberate — Pico Park-style stacking/pushing between players only works reliably if one place resolves the physics.

- **`Assets/Scripts/Network/NetworkBootstrapper.cs`** — entry point attached to the `NetworkManager` GameObject. Branches on the `UNITY_SERVER` compile flag: a Dedicated Server build calls `NetworkManager.Singleton.StartServer()` in `Start()` automatically; a regular client build waits and calls `StartClient()` from `ConnectToServer()`, wired to a UI button's `OnClick`. `startMenuUI` (assigned in the Inspector) is hidden once the client connects.
- **`Assets/Scripts/Player/PlayerMovementNGO.cs`** — movement/jump. The owning client writes raw horizontal input into a `NetworkVariable<float>` (`WritePermission.Owner`); only the server reads it in `FixedUpdate()` and applies it to `Rigidbody2D.velocity`. Jump is a `ServerRpc` the server validates against a ground-check `OverlapCircle` before applying. On `OnNetworkSpawn()`, the Rigidbody2D is set `Kinematic` on every non-server instance so local physics never fights the server's authoritative position — `NetworkTransform` is the only thing that should move a non-server instance. Ground detection expects a child `groundCheck` Transform and objects on the `Ground` layer.
- **`Assets/Scripts/Player/PlayerSetupNGO.cs`** — on `OnNetworkSpawn()`, the server places each player at a scene object tagged `SpawnPoint`, indexed by `OwnerClientId % spawnPoints.Length` (so it wraps if there are more players than spawn points).
- **`Assets/Scripts/Coop/CoopButtonNGO.cs`** / **`CoopDoorNGO.cs`** — the shared co-op mechanic. A button counts overlapping `Player`-tagged colliders server-side only (`if (!IsServer) return;` guards both trigger callbacks) and fires `OnButtonPress`/`OnButtonRelease` `UnityEvent`s, wired in the Inspector to a door's `AddPress()`/`RemovePress()`. The door tracks `requiredButtons` vs. currently-pressed count and writes the result into a `NetworkVariable<bool> isOpen` (`WritePermission.Server`), whose `OnValueChanged` callback drives the open/closed visuals (`Collider2D.enabled` + sprite alpha) identically on every client, including late joiners. Both scripts require a `NetworkObject` component on their GameObject to function — without one, `IsServer`/the `NetworkVariable` won't resolve correctly.

### Bot simulation (solo-dev multiplayer testing)

Because co-op mechanics can't be validated solo without a second real player, `-bot` client processes stand in for human partners:

- **`Assets/Scripts/Network/BotProcess.cs`** — static class that parses `Environment.GetCommandLineArgs()` once at startup (`RuntimeInitializeOnLoadMethod`). Sets `BotProcess.IsBot` from the `-bot` flag, and optional `-serverip <addr>` / `-serverport <port>` overrides.
- **`Assets/Scripts/Network/NetworkBootstrapper.cs`** — in the non-`UNITY_SERVER` branch, if `BotProcess.IsBot` is true it overrides the `UnityTransport` connection address (defaults to `127.0.0.1` so bots connect to a local Editor Play-mode host) and immediately calls `ConnectToServer()` instead of waiting for a UI click.
- **`Assets/Scripts/Player/BotController.cs`** — a `NetworkBehaviour` on the Player prefab. In `OnNetworkSpawn()`, disables itself unless `IsOwner && BotProcess.IsBot`. When active, generates `HorizontalInput`/`JumpRequested` every frame instead of reading the keyboard, using one of two modes: `Patrol` (bounce within `patrolHalfWidth` of spawn, useful for repeatedly walking onto buttons) or `FollowNearest` (chase the closest other `Player`-tagged object, useful for stacking/pushing tests). Jump timing is randomized between `jumpIntervalMin`/`jumpIntervalMax`.
- **`Assets/Scripts/Player/PlayerMovementNGO.cs`** — `Update()` checks for a sibling `BotController`; if present and enabled, it feeds `horizontalInput`/jump from the bot instead of `Input`. Everything downstream (the `NetworkVariable`, the `ServerRpc`) is identical to the human path, so bots exercise real network code, not a mock.

**Workflow**: build a regular client once (`File > Build Settings` → `Windows, Mac, Linux`) → `Tools/Coop Setup/Bot Simulation/Set Bot Client Build Path...` to point at the `.exe` → enter Play mode and `Tools/Coop Setup/Debug: Start Host` → `Tools/Coop Setup/Bot Simulation/Launch 1/2/3 Bot Client(s)` to spawn headless partners that auto-connect over `127.0.0.1:7777`. `Stop All Bot Clients` kills processes by matching the exe's filename (note: this also kills any other running copy of that same build). Bots only need the code path above; they don't require the GCP server and aren't wired into it.

### Physics / tag / layer config (`ProjectSettings/`)

These were hand-edited (not generated by the Editor) and are load-bearing:
- `Physics2DSettings.asset` — the `Player` layer's self-collision bit is explicitly re-enabled (it ships disabled in the Alteruna template this `ProjectSettings` baseline was copied from). Without it, players cannot stack/push each other, which breaks the core co-op mechanic.
- `TagManager.asset` — defines the `Player` and `SpawnPoint` tags, and a `Ground` layer (used by `PlayerMovementNGO`'s ground check).

## Deployment (GCP)

Dedicated server runs on Compute Engine, not Cloud Run — NGO's default `UnityTransport` uses raw UDP, which Cloud Run's HTTP(S)-only ingress cannot carry.

- Project: `filerpark` (billing enabled)
- VM: `filerpark-game-server`, `e2-small`, zone `asia-northeast3-a`, running 24/7
- Static IP: `34.50.24.161` (reserved as `filerpark-server-ip`)
- Firewall: `allow-ngo-udp` — ingress UDP `7777` from `0.0.0.0/0`, targets instances tagged `game-server`
- The client's `NetworkManager` → `UnityTransport` → Connection Data must point `Address` at `34.50.24.161`, `Port` `7777`.

## `ref/` directory

`ref/` (gitignored, never committed) holds shallow clones of external repos kept only as local reference while building this project: `alteruna-platformer`, `mirror-2d-platformer`, `fishnet`, and `survival_tile` (a separate, unrelated Node.js/Socket.io + Phaser 3 project by the same author — its GCP/Cloud Run deployment pattern was reference material, not something this project depends on or imports code from).
