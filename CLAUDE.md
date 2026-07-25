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

## UI Style Guide

All in-game UI (menus, HUD, buttons, connect screen, any future dialogs) must follow this guide so the look stays consistent as features are added one at a time. Source: the actual `bundle.css` design tokens from the official PICO PARK 2 site (`https://picoparkgame.com/en/pp2/`), extracted directly from its CSS custom properties — not a guess from screenshots. Do not invent alternative colors/fonts/radii for new UI; extend this token set instead.

### Color tokens

| Token | Hex / value | Use |
|---|---|---|
| `color-primary` | `#48ad15` (grass green) | Primary brand color — main buttons, header/footer background, active states. This is specifically PICO PARK **2**'s series color (PP1 is orange `hsl(19 100% 65%)`, Super Pico Park is blue `#0280c1` — don't mix those in, this project follows PP2). |
| `color-ice` | `#4dd6fa` (sky blue) | Secondary accent — secondary buttons, links, highlights, icy/water-themed elements. |
| `color-bg` | `#ffffff` | Page/screen background. Keep UI backgrounds white, not dark — this is a bright, family-friendly palette, not a dark theme. |
| `color-bg-secondary` | `#e9fbdf` (pale mint) | Panel/card backgrounds, subtle section separation on top of white. |
| `color-fg` | `#132e05` (`hsl(100 80% 10%)`, near-black forest green) | Primary text color. Never pure `#000000` — always this warm dark green-black. |
| `color-white` | `#ffffff` | Text/icon color on top of `color-primary` fills. |
| `color-highlight-ring` | `#bfffde` (light mint) | `box-shadow: 0 0 0 2px` inset ring for focus/hover/selected states. |
| `color-shadow-25/50/75` | `rgba(0,0,0,.25)` / `.5` / `.75` | Layered darkening for the chunky "3D block" depth effect (see Shape below), not for flat drop shadows. |

Don't introduce new hues without a reason — variety comes from mixing these tokens' weights/opacities, not from adding new colors.

### Typography

- **Headings**: `Dosis`, weight **800** (extra bold), fallback `'M PLUS 1p', sans-serif`. Always **UPPERCASE**, `letter-spacing: 0.05em`, `line-height: 1.3`. Dosis is tall/condensed and geometric — this is what gives titles their toy-block punch.
- **Body / UI labels**: `M PLUS 1p`, weights 500/700/800. A soft, rounded sans that also covers Hangul reasonably — useful since this codebase's dev comments are in Korean and any dev-facing debug UI may need Korean text.
- **Icons**: `Material Symbols Rounded` (rounded fill style, not sharp/outlined) for any icon glyphs — matches the soft, friendly shape language.
- Both fonts are free on Google Fonts (`Dosis`, `M+PLUS+1p`) — download the `.ttf`/variable files and import as TextMeshPro Font Assets; don't ship Google Fonts CDN links (this is a Unity build, not a web page).
- **Known gap**: the current Connect UI (`Tools/Coop Setup/5. Create Connect UI`) intentionally uses legacy `UnityEngine.UI.Text` + `LegacyRuntime.ttf` as a placeholder (see "Editor automation" above — TMP Essentials weren't imported yet). That UI does **not** yet match this guide. Once TMP Essentials + the Dosis/M PLUS 1p font assets are imported, migrate it to TMP with these fonts rather than leaving it as the odd one out.

### Shape & depth

- **Corner radius**: `6px` is the default for buttons, cards, and pill-shaped nav items. `3px` for small/tight elements. `12px`–`20px` only for large feature panels, never for small controls.
- **Borders**: interactive pill/badge elements (e.g. nav buttons) use a **solid 3px border** in the foreground color, not just a fill — the "badge/sticker" look, not a flat Material-style button.
- **Depth**: avoid soft blurred drop-shadows. Depth comes from **layered flat `rgba(0,0,0,.25/.5/.75)` offsets** stacked to look like a chunky physical block/cube casting a hard-edged shadow — this matches the game's literal cube/box level geometry. A `0 0 0 2px inset` ring in `color-highlight-ring` is the only "glow"-style effect, reserved for hover/focus/selected.
- **No gradients for filling shapes** — flat color fills only. Gradients on the reference site are limited to thin decorative accents (a 2px white "shine" line, edge-fade masks) — not something to reach for by default.

### Layout & tone

- Generous whitespace, center-aligned content, clear vertical sections — not dense or cluttered.
- Bright, clean, minimal-but-playful, cooperative/family-friendly tone throughout. Every UI decision should read as "toy blocks on a white table," not "sci-fi HUD" or "dark fantasy."

### Art style (sprites/characters, for when placeholder art is replaced)

Simplified, round/blobby character shapes with thick clean outlines and flat saturated color fills; simple dot/shape facial features; no complex shading or gradients. Low visual complexity is a feature, not a limitation — it reads clearly at small sizes and is cheap for a solo dev to produce/reproduce consistently across many player-color variants (see `PlayerColorNGO`'s palette).

## Deployment (GCP)

Dedicated server runs on Compute Engine, not Cloud Run — NGO's default `UnityTransport` uses raw UDP, which Cloud Run's HTTP(S)-only ingress cannot carry.

- Project: `filerpark` (billing enabled)
- VM: `filerpark-game-server`, `e2-small`, zone `asia-northeast3-a`, running 24/7
- Static IP: `34.50.24.161` (reserved as `filerpark-server-ip`)
- Firewall: `allow-ngo-udp` — ingress UDP `7777` from `0.0.0.0/0`, targets instances tagged `game-server`
- The client's `NetworkManager` → `UnityTransport` → Connection Data must point `Address` at `34.50.24.161`, `Port` `7777`.

**Gotchas hit deploying the real server (verified via `tcpdump` on the VM, not just "StartServer() returned true"):**
- `UnityTransport.ConnectionData.ServerListenAddress` defaults to `127.0.0.1` regardless of what `Address` is set to. `StartServer()` reports success either way, but nothing outside the VM can reach it unless the server explicitly sets `ServerListenAddress = "0.0.0.0"` before starting (see `NetworkBootstrapper.cs`'s `UNITY_SERVER` branch).
- A freshly built client `.exe` that's never been run has no Windows Firewall rule and gets its inbound UDP (the server's handshake replies) silently dropped, even though its own outbound packets reach the server fine — looks identical to a server-side problem from the client's logs alone. Needs explicit `New-NetFirewallRule` (in + out, UDP, `-Profile Any`) for the exact exe path.
- Deploying via `gcloud compute scp --recurse` from Windows uses PuTTY's `pscp` under the hood, which behaves differently from OpenSSH `scp`: destination must be an absolute remote path (`/home/<user>`, not `~`) — `~` is passed through literally and PuTTY doesn't expand it — and the destination directory is created from the local folder's own name, so scp `ServerBuild` to `/home/<user>` (not `/home/<user>/ServerBuild`, which double-nests).
- `nohup ... &` inside a single `gcloud compute ssh --command="..."` call routinely hangs the SSH session past the point the remote process has actually detached; don't treat that command's own timeout/hang as a failure signal — reconnect with a fresh `ssh --command` to check `ps aux`/logs independently.

## `ref/` directory

`ref/` (gitignored, never committed) holds shallow clones of external repos kept only as local reference while building this project: `alteruna-platformer`, `mirror-2d-platformer`, `fishnet`, and `survival_tile` (a separate, unrelated Node.js/Socket.io + Phaser 3 project by the same author — its GCP/Cloud Run deployment pattern was reference material, not something this project depends on or imports code from).
