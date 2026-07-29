#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;

// "대기실 시작 버튼을 누르고 5초 기다려도 봇이 안 들어온다"는 실제 플레이 리포트를
// 재현/검증하려고 만든 헤드리스 도구 -- HeadlessSmokeTest와 같은 SessionState 재접속
// 패턴(도메인 리로드로 일반 이벤트 구독이 사라지는 문제 회피)을 그대로 쓴다. 사람이
// 직접 버튼을 클릭하는 대신 GameFlowManager.RequestReadyServerRpc(true)를 코드로
// 직접 호출해 "준비 버튼을 눌렀다"를 재현하고, 그 뒤 로그/NetworkVariable을 폴링해서
// (1) fillWaitActive가 실제로 켜지는지, (2) fillWaitSeconds 뒤에 채움 봇이 실제로
// 스폰되는지, (3) 스테이지가 실제로 시작되는지를 명시적으로 pass/fail 로 남긴다.
//
// 주의: StartHost()를 쓰므로(HeadlessSmokeTest와 동일), 호스트 자신도 접속 인원 1명으로
// 잡힌다 -- 이 도구는 "혼자 준비를 누르면 부족한 인원만큼 채움 봇이 자동으로 채워지는가"
// 를 검증하는 것이므로 오히려 정확히 그 시나리오다(솔로 플레이어가 실제로 겪는 경로).
public static class LobbyReadyFillVerify
{
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
    private const float MaxWaitSeconds = 20f;
    private const float SettleSeconds = 3f;

    private const string KeyActive = "LobbyReadyFillVerify.Active";
    private const string KeyHostStarted = "LobbyReadyFillVerify.HostStarted";
    private const string KeyReadySent = "LobbyReadyFillVerify.ReadySent";
    private const string KeyStartTime = "LobbyReadyFillVerify.StartTime";
    private const string KeyLastLoggedPhase = "LobbyReadyFillVerify.LastLoggedPhase";
    private const string KeyPassDetectedAt = "LobbyReadyFillVerify.PassDetectedAt";
    private const string KeyPassResult = "LobbyReadyFillVerify.PassResult";

    [MenuItem("Tools/Coop Setup/Debug: Lobby Ready+Fill Verify")]
    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("[LobbyFillVerify] 이 도구는 -batchmode로 실행한 Editor에서만 동작합니다. " +
                "헤드리스로 -executeMethod LobbyReadyFillVerify.Run 을 쓰세요.");
            return;
        }

        EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyHostStarted, false);
        SessionState.SetBool(KeyReadySent, false);
        SessionState.EraseFloat(KeyStartTime);
        SessionState.EraseString(KeyLastLoggedPhase);
        SessionState.EraseFloat(KeyPassDetectedAt);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void ReattachAfterDomainReload()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(KeyActive, false))
        {
            EditorApplication.update -= Tick;
            return;
        }

        if (!EditorApplication.isPlaying)
        {
            Finish("Play 모드가 이미 종료된 상태를 감지", false);
            return;
        }

        if (SessionState.GetFloat(KeyStartTime, -1f) < 0f)
        {
            SessionState.SetFloat(KeyStartTime, (float)EditorApplication.timeSinceStartup);
        }

        if (!SessionState.GetBool(KeyHostStarted, false) && NetworkManager.Singleton != null)
        {
            bool started = NetworkManager.Singleton.StartHost();
            SessionState.SetBool(KeyHostStarted, started);
            Debug.Log(started ? "[LobbyFillVerify] StartHost() 성공." : "[LobbyFillVerify] StartHost() 실패.");
            if (!started)
            {
                Finish("StartHost() 실패", false);
                return;
            }
        }

        float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(KeyStartTime, 0f);

        // 호스트 스폰 + Lobby 씬 로드가 끝나 GameFlowManager.Instance가 잡히면, 아직
        // 준비 요청을 안 보냈을 때 한 번만 "버튼 클릭"을 재현한다.
        if (!SessionState.GetBool(KeyReadySent, false))
        {
            if (GameFlowManager.Instance != null && GameFlowManager.Instance.phase.Value == GameFlowManager.GamePhase.Lobby)
            {
                Debug.Log($"[LobbyFillVerify] Lobby 진입 확인(경과 {elapsed:F1}초) -- 준비 버튼 클릭을 재현합니다.");
                GameFlowManager.Instance.RequestReadyServerRpc(true);
                SessionState.SetBool(KeyReadySent, true);
            }
        }
        else
        {
            // 준비 요청을 보낸 뒤로는 phase가 바뀔 때마다(또는 5초 대기 창 안에서)
            // 상태를 로그로 남긴다 -- 나중에 로그만 보고도 어느 단계에서 멈췄는지
            // 바로 알 수 있게.
            string phaseNow = GameFlowManager.Instance != null ? GameFlowManager.Instance.phase.Value.ToString() : "null";
            if (phaseNow != SessionState.GetString(KeyLastLoggedPhase, ""))
            {
                SessionState.SetString(KeyLastLoggedPhase, phaseNow);
                int activePlayers = PlayerSetupNGO.ActivePlayers.Count;
                Debug.Log($"[LobbyFillVerify] phase 변경 감지(경과 {elapsed:F1}초): {phaseNow}, ActivePlayers={activePlayers}");
            }

            if (GameFlowManager.Instance != null && GameFlowManager.Instance.phase.Value == GameFlowManager.GamePhase.InStage)
            {
                // phase는 StartNextStage()에서 씬 전환 시퀀스보다 먼저 동기적으로 바뀐다 --
                // 여기서 곧바로 EditorApplication.Exit()하면 아직 진행 중인 Lobby Unload
                // 비동기 작업 도중에 프로세스가 죽어, NGO의 OnSceneUnloaded 콜백이 이미
                // 정리된 상태를 참조하다 ArgumentOutOfRangeException을 던진다(실제 게임
                // 버그 아님, 이 테스트 도구가 너무 성급하게 종료해서 생기는 부작용). 판정
                // 자체는 이 프레임에 확정하고, 실제 종료만 SettleSeconds만큼 늦춘다.
                if (SessionState.GetFloat(KeyPassDetectedAt, -1f) < 0f)
                {
                    int activePlayers = PlayerSetupNGO.ActivePlayers.Count;
                    int required = GameFlowManager.Instance.requiredHeadcountToStart;
                    bool pass = activePlayers >= required;
                    SessionState.SetBool(KeyPassResult, pass);
                    SessionState.SetFloat(KeyPassDetectedAt, (float)EditorApplication.timeSinceStartup);
                    Debug.Log(pass
                        ? $"[LobbyFillVerify] PASS -- 스테이지 시작됨, ActivePlayers={activePlayers} (필요 {required}명) -- 채움 봇이 정상적으로 투입되었습니다."
                        : $"[LobbyFillVerify] FAIL -- 스테이지는 시작됐지만 ActivePlayers={activePlayers} < 필요 {required}명 -- 채움 봇 투입이 실패했습니다.");
                }
                else if (EditorApplication.timeSinceStartup - SessionState.GetFloat(KeyPassDetectedAt, 0f) > SettleSeconds)
                {
                    Finish("스테이지 시작 확인", SessionState.GetBool(KeyPassResult, false));
                    return;
                }
            }
        }

        if (elapsed > MaxWaitSeconds)
        {
            Finish($"FAIL -- {MaxWaitSeconds}초 동안 스테이지가 시작되지 않음(채움 봇 투입 로직이 멈춘 것으로 보임)", false);
        }
    }

    private static void Finish(string reason, bool pass)
    {
        SessionState.SetBool(KeyActive, false);
        EditorApplication.update -= Tick;
        Debug.Log($"[LobbyFillVerify] {(pass ? "PASS" : "결과")}: {reason} -- 배치 프로세스를 닫습니다.");
        EditorApplication.Exit(pass ? 0 : 1);
    }
}
#endif
