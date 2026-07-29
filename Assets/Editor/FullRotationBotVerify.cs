#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;
using Debug = UnityEngine.Debug;

// Stage05BotVerify가 확인한 건 "Stage5를 단독으로 첫 스테이지로 넣었을 때 봇 2개로
// 클리어되는가"였다 -- Stage1~4를 먼저 거치는 자연스러운 순환 속에서 Stage5까지
// 도달했을 때도 똑같이 동작하는지는 아직 확인된 적이 없다(Stage1~4가 받은 4~6봇
// GCP 실서버 순환 테스트 수준에는 못 미침). 이 도구는 GameFlowManager.stageSceneNames를
// 바꿔치기하지 않고 진짜 5개 순서 그대로, 봇 4개로 Stage1->2->3->4->5를 자연스럽게
// 순환시켜 Stage5(currentStageIndex==4)가 phase==Results에 도달하는지 관찰한다.
public static class FullRotationBotVerify
{
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
    private const string ClientExePath = "ClientBuild/filerpark.exe";
    private const int BotCount = 4;
    // 과거 "5봇 연속 순환 ~9분" 실측은 GCP 전용 서버 + 별도 머신의 봇 클라이언트
    // 조합이었다. 이 도구는 배치 모드 Editor 호스트와 봇 클라이언트 4개(각각 별도
    // 프로세스)를 전부 한 개발 머신에서 동시에 돌리므로, 특히 이 세션처럼 몇 시간째
    // Unity를 연속으로 띄우고 내린 뒤라면 리소스 경합으로 그보다 훨씬 오래 걸릴 수
    // 있다 -- 480초로는 Stage1~2를 통과하고 Stage3가 막 로드된 시점에 컷오프되는
    // 걸 실제로 관측했다(스테이지 자체가 멈춘 게 아니라 예산이 부족했던 것). 로컬
    // 환경의 실제 성능 한계를 반영해 넉넉하게 늘린다.
    private const float TimeoutSeconds = 900f;
    private const int Stage5Index = 4; // stageSceneNames의 5번째 원소(0-indexed).

    private const string KeyActive = "FullRotationBotVerify.Active";
    private const string KeyHostStarted = "FullRotationBotVerify.HostStarted";
    private const string KeyBotsLaunched = "FullRotationBotVerify.BotsLaunched";
    private const string KeyHostReadySent = "FullRotationBotVerify.HostReadySent";
    private const string KeyStartTime = "FullRotationBotVerify.StartTime";
    private const string KeyMaxStageSeen = "FullRotationBotVerify.MaxStageSeen";

    [MenuItem("Tools/Coop Setup/Debug: Full Rotation Bot Verify (1-5)")]
    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("[FullRotationVerify] -batchmode에서만 동작합니다. -executeMethod FullRotationBotVerify.Run 사용.");
            return;
        }

        EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);

        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyHostStarted, false);
        SessionState.SetBool(KeyBotsLaunched, false);
        SessionState.SetBool(KeyHostReadySent, false);
        SessionState.EraseFloat(KeyStartTime);
        SessionState.SetInt(KeyMaxStageSeen, -1);
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
            Debug.Log(started ? "[FullRotationVerify] StartHost() 성공." : "[FullRotationVerify] StartHost() 실패.");
            if (!started)
            {
                Finish("StartHost 실패", false);
                return;
            }
        }

        if (SessionState.GetBool(KeyHostStarted, false) && !SessionState.GetBool(KeyBotsLaunched, false))
        {
            LaunchBots(BotCount);
            SessionState.SetBool(KeyBotsLaunched, true);
        }

        // StartHost()는 서버+클라이언트를 겸하므로 호스트 자신의 플레이어 오브젝트도
        // 스폰된다(BotController.OnNetworkSpawn 참고). 이 가짜 플레이어는 -bot 프로세스가
        // 아니라 BotController가 비활성 상태로 남아 스스로 준비를 알리지 않으므로,
        // 로비의 "전원 준비" 게이트(GameFlowManager.Update의 allReady)가 봇 4개를
        // 다 채워도 호스트 1명분 때문에 영원히 충족되지 않는다. LobbyReadyFillVerify가
        // 쓰는 것과 같은 방식으로 이 도구가 직접 "준비 버튼 클릭"을 대신 호출한다 --
        // 실제 배포(StartServer(), 호스트 플레이어 없음)에는 없는, 이 로컬 테스트
        // 방식만의 문제다.
        // GameFlowManager는 스테이지가 시작될 때마다 readyClientIds를 초기화하므로
        // (매 라운드 다시 준비해야 함), 호스트도 로비에 돌아올 때마다 다시 한 번
        // 준비를 보내야 한다 -- 한 번만 보내면 Stage1 클리어 후 두 번째 로비부터는
        // 봇 4개가 전부 재준비해도(BotController의 동일한 수정 참고) 호스트 몫 1명이
        // 빠져 영원히 대기하게 된다(실측: 4/5에서 정체). phase가 Lobby가 아니게
        // 되면 플래그를 리셋해 다음 로비 진입 때 다시 보내도록 한다.
        bool inLobbyNow = GameFlowManager.Instance != null && GameFlowManager.Instance.phase.Value == GameFlowManager.GamePhase.Lobby;
        if (!inLobbyNow)
        {
            SessionState.SetBool(KeyHostReadySent, false);
        }
        else if (SessionState.GetBool(KeyHostStarted, false) && !SessionState.GetBool(KeyHostReadySent, false))
        {
            GameFlowManager.Instance.RequestReadyServerRpc(true);
            SessionState.SetBool(KeyHostReadySent, true);
            Debug.Log("[FullRotationVerify] 호스트 자신의 준비 상태도 전송(가짜 호스트 플레이어가 준비 게이트를 막지 않도록).");
        }

        if (GameFlowManager.Instance != null)
        {
            int currentIndex = GameFlowManager.Instance.currentStageIndex.Value;
            int maxSeen = SessionState.GetInt(KeyMaxStageSeen, -1);
            if (currentIndex > maxSeen)
            {
                SessionState.SetInt(KeyMaxStageSeen, currentIndex);
                // 로그 줄 수는 스팸(예: 이전에 찾은 fall-recovery 버그) 유무에 따라 실제
                // 경과 시간과 전혀 비례하지 않아 진행 속도를 오판하게 만들었다 -- 실제
                // 경과 초를 직접 찍어야 "스테이지 하나에 몇 초 걸렸는지"를 신뢰할 수 있다.
                float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(KeyStartTime, (float)EditorApplication.timeSinceStartup);
                Debug.Log($"[FullRotationVerify] 진행 상황: currentStageIndex={currentIndex}에 도달 (경과 {elapsed:F1}초).");
            }

            if (currentIndex == Stage5Index && GameFlowManager.Instance.phase.Value == GameFlowManager.GamePhase.Results)
            {
                Finish("Stage5까지 자연 순환으로 도달 후 클리어 감지 (currentStageIndex==4, phase==Results)", true);
                return;
            }
        }

        float startTime = SessionState.GetFloat(KeyStartTime, (float)EditorApplication.timeSinceStartup);
        if (EditorApplication.timeSinceStartup - startTime > TimeoutSeconds)
        {
            int maxSeen = SessionState.GetInt(KeyMaxStageSeen, -1);
            Finish($"타임아웃 -- Stage5에 도달하지 못함 (도달한 최대 currentStageIndex={maxSeen})", false);
        }
    }

    private static void LaunchBots(int count)
    {
        string fullPath = Path.GetFullPath(ClientExePath);
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[FullRotationVerify] 클라이언트 빌드를 찾을 수 없습니다: {fullPath} -- 먼저 " +
                "Tools/Coop Setup/Build/Windows Client를 실행하세요.");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fullPath,
                Arguments = "-bot -serverip 127.0.0.1 -serverport 7777",
                UseShellExecute = true,
            };
            Process.Start(startInfo);
        }

        Debug.Log($"[FullRotationVerify] 봇 클라이언트 {count}개 실행.");
    }

    private static void Finish(string reason, bool success)
    {
        SessionState.SetBool(KeyActive, false);
        EditorApplication.update -= Tick;
        Debug.Log($"[FullRotationVerify] {(success ? "성공" : "실패")}: {reason} -- 배치 프로세스를 닫습니다.");
        KillBotProcesses();
        EditorApplication.Exit(success ? 0 : 1);
    }

    private static void KillBotProcesses()
    {
        string processName = Path.GetFileNameWithoutExtension(ClientExePath);
        foreach (Process p in Process.GetProcessesByName(processName))
        {
            try { p.Kill(); }
            catch (System.Exception) { /* 이미 종료됐으면 무시 -- 정리가 목적이지 실패가 검증 결과에 영향을 주면 안 된다. */ }
        }
    }
}
#endif
