# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Pico Park-style 2D co-op puzzle platformer built in Unity, using Netcode for GameObjects (NGO) with a dedicated server hosted on GCP.

- Unity: **6000.5.5f1** (`ProjectSettings/ProjectVersion.txt`) — migrated up from an original 2021.3.45f2 baseline; see the Unity 6 migration note below.
- Netcode: `com.unity.netcode.gameobjects` 2.12.0 + `com.unity.transport` 2.6.0 (`Packages/manifest.json`)
- The single scene is `Assets/scense/main.unity` (folder name is a typo for "Scenes", left as-is — renaming risks breaking asset GUID references for no real benefit). The Player prefab is `Assets/Prefabs/Player.prefab`.

## Commands

There is no CLI build/test/lint pipeline in the traditional sense — it's a Unity project — but scene assembly and compile verification are scripted rather than manual, via `Assets/Editor/NetworkSetupMenu.cs`. There is no automated test suite.

- **Headless compile check** (run after any script/package/ProjectSettings change, before assuming it works): with the Editor fully closed (a second instance on the same project fails fast on the project lock),
  ```
  "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "C:\Users\twinw\Desktop\filerpark" -logFile "<path>.log"
  ```
  The launching process can return **before** Unity actually finishes (package resolution/compilation continues in a detached process) — poll `tasklist` for `Unity.exe` until it's actually gone, then check the log with `grep "error CS"` and confirm `Library/ScriptAssemblies/Assembly-CSharp.dll` exists/was rewritten. A `bee_backend.exe` build-daemon process can outlive Unity itself and hold file locks; `taskkill /F /IM bee_backend.exe` if a cleanup step gets "Device or resource busy".
- **Scene assembly**: `Tools/Coop Setup/1-5` menu items (Editor GUI, or headless via `-executeMethod NetworkSetupMenu.<MethodName>`) build the NetworkManager, Player prefab, camera, test ground, and connect UI programmatically instead of by hand — see "Editor automation" below for why.
- **Server build**: Unity Editor → `File > Build Settings` → platform `Dedicated Server`, Target OS `Linux` → produces a `.x86_64` binary.
- **Client build**: same dialog, platform `Windows, Mac, Linux` (regular player build) → produces a `.exe`. This build doubles as the bot-simulation executable (see below).
- **Deploy to the dedicated server VM**:
  ```
  gcloud compute scp --recurse ServerBuild filerpark-game-server:~ --project=filerpark --zone=asia-northeast3-a
  gcloud compute ssh filerpark-game-server --project=filerpark --zone=asia-northeast3-a
  chmod +x <binary>.x86_64
  nohup ./<binary>.x86_64 -batchmode -nographics &
  ```

## Editor automation (`Assets/Editor/NetworkSetupMenu.cs`)

Scene/prefab assembly (NetworkManager + UnityTransport + NetworkBootstrapper, the Player prefab and all its components, camera setup, a test ground platform, and the connect-button UI) is done via scripted `[MenuItem]` methods under `Tools/Coop Setup`, not by hand-placing GameObjects in the Inspector. Two reasons this matters:

- Hand-editing a `.unity`/`.prefab` YAML file directly is much riskier than it looks — a single missing trailing space once silently broke `TagManager.asset`'s parser. Building objects through Unity's own `AddComponent`/`PrefabUtility` API is the safe alternative to both hand-editing scene YAML and requiring a human to click through the Inspector.
- Because these are just static C# methods, they're also invokable headlessly via `-executeMethod`, so scene assembly doesn't strictly require a human driving the Editor GUI at all.
- The connect-button UI deliberately uses legacy `UnityEngine.UI.Text` + the built-in `LegacyRuntime.ttf` font, not TextMeshPro — TMP needs its Essentials resources imported first or new text renders with no font assigned, and that failure mode can't be caught by a headless compile check.
- NGO 2.x's `NetworkManager` inspector no longer shows the old Start Host/Server/Client buttons in Play Mode (present in 1.x); `Tools/Coop Setup/Debug: Start Host` (Play-mode-only, gated by a validate function) replaces it by calling `NetworkManager.Singleton.StartHost()` directly.

## Unity 6 migration notes

The project was originally scaffolded against Unity 2021.3.45f2 and later moved to 6000.5.5f1; a few things fell out along the way that are worth knowing if `manifest.json` or scripts changed since then start throwing errors again:

- `com.unity.modules.vr` (and the rest of the boilerplate `com.unity.modules.*` block copied from the original Alteruna-derived `manifest.json`) is gone from the Unity 6 package registry — package resolution fails outright if it's present. None of those built-in modules need to be listed explicitly; Unity resolves what it actually needs.
- `com.unity.2d.animation`, `com.unity.2d.spriteshape`, and `com.unity.2d.psdimporter` don't compile on Unity 6000.5's API (`Object.GetInstanceID()`/`TreeView` obsoletions turned into hard errors) and aren't used by this project — removed rather than fixed.
- `Rigidbody2D.velocity` is obsolete in Unity 6; use `linearVelocity` (see `PlayerMovementNGO.cs`).
- `TagManager.asset`'s layer list is YAML `- ` (dash-space) per empty entry; a bare `-` with no trailing space fails to parse and Unity silently falls back to default tags/layers instead of erroring loudly — caught only by actually opening the project, not by eyeballing the diff.

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
