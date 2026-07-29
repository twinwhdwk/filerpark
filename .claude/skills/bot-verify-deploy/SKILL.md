---
name: bot-verify-deploy
description: Run the accumulated changes in this Unity/NGO co-op game through headless compile check, scene regen, smoke test, and bot-based functional verification, then (only if everything passes) build and deploy to the live GCP dedicated server. Use when a meaningful batch of gameplay/network/editor-script changes has piled up uncommitted-or-undeployed and it's time to check "does this actually still work" before shipping it, or when the user explicitly asks to verify-and-deploy / run the bot test and deploy / 봇 테스트하고 배포해줘. Do not use for a single trivial UI-text or comment change — the full pipeline (rebuild client, run bots, deploy) takes real time and hits the production server.
tools: Bash, Read, Edit, Write, Grep, Glob, AskUserQuestion
---

# Bot Verify → Deploy

This project (`C:\Users\twinw\Desktop\filerpark`) has no CI. This skill is the
manual substitute: a fixed pipeline from "pile of changed scripts" to "verified
and live on the GCP dedicated server," built from the exact gotchas hit while
developing this project (see root `CLAUDE.md` — read it if anything below is
unclear, it is the source of truth for individual commands).

**This deploys to the real production server** (`filerpark-game-server`,
`34.50.24.161:7777`) that is the one and only environment for this solo-dev
project. There's no staging environment to fall back on, so the safety comes
from the verification gates below, not from blast-radius limiting. Follow the
gates in order; do not skip ahead to deploy on a hunch that "it's probably
fine."

## 0. Should this run at all?

Compare current `HEAD` against the last recorded deploy:

```bash
cat .claude/DEPLOY_STATE.md 2>/dev/null   # has "Last deployed commit: <sha>"
git log <that-sha>..HEAD --oneline
```

If `.claude/DEPLOY_STATE.md` doesn't exist yet, treat this as the first run —
proceed unconditionally.

- If the user explicitly asked for this skill by name/description in this
  turn: always run the full pipeline regardless of how small the diff is —
  an explicit ask overrides the heuristic.
- If you're proactively considering this mid-session because changes have
  piled up: only run the full pipeline when the accumulated commits touch
  `Assets/Scripts/**`, `Assets/Editor/**`, `ProjectSettings/**`, or
  `Assets/Scenes/**` (gameplay/network/editor-tooling/scene-affecting code) —
  not pure doc/comment-only changes — and there are several such commits, not
  one. For a single small change, just do the normal compile-check workflow
  instead of the full deploy pipeline.

## 1. Pre-flight safety

```bash
tasklist | grep -i unity.exe
```

Must be empty — a second Unity instance on this project fails fast on the
project lock. If something's running that you didn't start, stop and figure
out what before touching the project (could be a concurrent session's
in-progress work — see CLAUDE.md's "concurrent multi-session editing" notes;
never kill a Unity process you didn't launch without checking first).

```bash
git status
```

If there are uncommitted changes you didn't just make yourself, stop and
investigate rather than sweeping them into this run — `git add` only files
you know you authored, never a blanket `git add -A`.

## 2. Headless compile check

With Unity fully closed:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "C:\Users\twinw\Desktop\filerpark" -logFile "<scratchpad>\compile.log"
```

The launcher returns before Unity is actually done — poll `tasklist` for
`Unity.exe` until it's gone (and `bee_backend.exe`; `taskkill /F /IM
bee_backend.exe` if a later step gets "resource busy"). Then:

```bash
grep -i "error CS" "<scratchpad>\compile.log"
```

Any hit = **STOP**, report the errors, do not proceed. Also sanity-check
`Library/ScriptAssemblies/Assembly-CSharp.dll`'s mtime moved.

## 3. Scene regen (conditional)

Only if the accumulated diff touches `Assets/Editor/GameFlowSceneSetup.cs` or
any file under `Assets/Scenes/`:

```bash
... -batchmode -nographics -quit -projectPath "C:\Users\twinw\Desktop\filerpark" -executeMethod GameFlowSceneSetup.RunAll -logFile "<scratchpad>\scenegen.log"
```

This is destructive-by-overwrite (discards manual scene tweaks) — that's
expected and fine, scenes are generated output in this project. Check the log
for `error CS` / exceptions same as step 2.

## 4. Headless smoke test

```bash
... -batchmode -nographics -projectPath "C:\Users\twinw\Desktop\filerpark" -executeMethod HeadlessSmokeTest.Run -logFile "<scratchpad>\smoke.log"
```

**No `-quit`** — the script exits the process itself after ~10s. Poll
`tasklist` until `Unity.exe` is gone on its own, then:

```bash
grep -iE "nullreferenceexception|exception|error CS" "<scratchpad>\smoke.log"
```

Any hit = **STOP**, report, do not proceed. This catches null-ref/exception
bugs a compile check can't, cheaply, before spending time on the much slower
bot pass below.

## 5. Rebuild the bot-test client (mandatory, every time)

`ClientBuild/filerpark.exe` goes stale the instant scenes/prefabs change —
this exact staleness previously masked itself as a fake "Stage 2 stall"
regression (see CLAUDE.md). **Never trust a bot-verify run against a client
you didn't just rebuild this pass:**

```bash
... -batchmode -nographics -quit -projectPath "C:\Users\twinw\Desktop\filerpark" -executeMethod BuildScript.BuildWindowsClient -logFile "<scratchpad>\buildclient.log"
```

Confirm `ClientBuild\filerpark.exe`'s mtime just moved.

## 6. Bot functional verification

```bash
... -batchmode -nographics -projectPath "C:\Users\twinw\Desktop\filerpark" -executeMethod FullRotationBotVerify.Run -logFile "<scratchpad>\botverify.log"
```

No `-quit` (self-exits). 8-minute timeout — poll `tasklist`, don't sleep-loop
short intervals; use Bash `run_in_background` and wait for the notification,
or Monitor with a long until-loop.

**Reading the result — a known false alarm to not over-react to:** every
local bot-verify tool here uses `StartHost()`, which spawns a phantom idle
player permanently at rank 0 (see CLAUDE.md's "Stage-3 rank-0" root-cause
writeup). `BotController.UpdateKeyRelay()` requires rank 0 to fetch Stage 3's
key, so **a stall exactly at Stage 3 (Key Relay) with zero exceptions and no
`[KeyDoor]` log lines is a known artifact of local `StartHost()` testing, not
a regression** — this local tool structurally cannot verify Stage 3's
key-fetch logic, full stop. Do not block on this specific signature.

Anything else is real signal and blocks deployment:
- Any exception/`NullReferenceException`/`error CS` anywhere in the log.
- A stall at Stage 1, Stage 2, Stage 4, or Stage 5.
- Repeated-warning log spam (e.g. thousands of identical lines) — that
  pattern previously indicated a real per-tick busy-loop bug, not noise.

If Stage 1/2 clear cleanly and the only stall is the Stage-3 known artifact:
proceed, but note in the final report that Stage 3 specifically will only be
confirmed by the post-deploy real-bot check in step 9, not by this step.

If anything else failed: **STOP**, report the specific failure to the user in
Korean (this project's established language for status reports), do not
deploy.

## 7. Build server + deploy artifacts

```bash
... -batchmode -nographics -quit -projectPath "C:\Users\twinw\Desktop\filerpark" -executeMethod BuildScript.BuildDedicatedServerLinux -logFile "<scratchpad>\buildserver.log"
```

(The Linux build is the actual deploy target; the Windows server build method
exists only for local dev-loop testing and isn't needed here.) Confirm no
`error CS` and the `ServerBuild` output changed.

## 8. Deploy to GCP

Back up the currently-live build on the VM first so a bad deploy is a
one-command rollback, not a manual rebuild-and-redeploy under pressure:

```bash
gcloud compute ssh filerpark-game-server --project=filerpark --zone=asia-northeast3-a --command="pkill -f '\.x86_64' || true; rm -rf ~/ServerBuild.backup; mv ~/ServerBuild ~/ServerBuild.backup 2>/dev/null || true"
```

Upload the new build (absolute remote path — PuTTY/pscp under `gcloud compute
scp` doesn't expand `~`, and the destination dir is created from the local
folder's own name, so scp `ServerBuild` to `/home/twinw`, not
`/home/twinw/ServerBuild`):

```bash
gcloud compute scp --recurse ServerBuild filerpark-game-server:/home/twinw --project=filerpark --zone=asia-northeast3-a
```

Start it in its own SSH connection (the known gotcha: `nohup ... & disown`
inside a single `gcloud compute ssh --command="..."` call routinely hangs that
same call even though the remote process detaches fine — don't treat that
command's own hang/timeout as a start failure):

```bash
gcloud compute ssh filerpark-game-server --project=filerpark --zone=asia-northeast3-a --command="cd ~/ServerBuild && chmod +x *.x86_64 && nohup ./*.x86_64 -batchmode -nographics > server.log 2>&1 & disown"
```

Then reconnect **separately** to confirm it's actually up:

```bash
gcloud compute ssh filerpark-game-server --project=filerpark --zone=asia-northeast3-a --command="sleep 3 && ps aux | grep x86_64 | grep -v grep && tail -20 ~/ServerBuild/server.log"
```

Look for `[Server] 서버가 성공적으로 열렸습니다!` in the tail. If the process
isn't there or the log shows `[Server] 서버 열기 실패`: the deploy failed —
roll back immediately (`rm -rf ~/ServerBuild && mv ~/ServerBuild.backup
~/ServerBuild` then repeat the start step) and report to the user in Korean.
Do not leave the server down.

## 9. Post-deploy real-bot check (closes the Stage-3 gap step 6 couldn't)

This is the only step that runs against the actual dedicated server
(`StartServer()`, no phantom rank-0 host), so it's the real confirmation for
anything step 6 flagged as inconclusive:

```powershell
Start-Process "ClientBuild\filerpark.exe" -ArgumentList "-bot -serverip 34.50.24.161 -serverport 7777"
```

Launch 2-4 of these. Give it a few minutes, then check server-side log via a
fresh SSH connection for stage-clear evidence (`GameFlowManager` phase
transitions, `[KeyDoor]` open lines for Stage 3 specifically, no exceptions).
Stop the bot processes afterward (`taskkill /F /IM filerpark.exe` locally —
this also kills any other running copy of that exact build, same caveat as
the in-editor bot tooling).

If this step finds a real problem: the bad build is already live. Roll back
per step 8's rollback path immediately, then report — don't leave a broken
build serving real connections while investigating.

## 10. Record + report

Update (create if missing) `.claude/DEPLOY_STATE.md`:

```markdown
# Deploy State

Last deployed commit: <HEAD sha>
Deployed: <date>
Verification: FullRotationBotVerify <pass/known-Stage3-artifact-only>, post-deploy real-bot check <pass/fail>
```

Commit and push this file (per this user's standing preference — see project
memory — commit+push after each unit of work). Then report to the user **in
Korean**: what changed since the last deploy, what the verification found
(including explicitly calling out if step 6 hit the known Stage-3 artifact
vs. a real failure), and confirmation the live server is up on the new build.

If any gate stopped the pipeline early, report exactly which one and why —
never silently fall back to "looks fine, deploying anyway."
