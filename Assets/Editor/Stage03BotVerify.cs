#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;
using Debug = UnityEngine.Debug;

// 진단용 -- FullRotationBotVerify(4봇, 실제 5스테이지 순환)가 Stage3(열쇠 릴레이)에서
// 744초 넘게 KeyDoor가 단 한 번도 안 열린 채(=열쇠가 문 근처에 간 적이 없음) 막히는
// 걸 실측했다. Stage05BotVerify/Stage02BotVerify와 같은 패턴으로 Stage3 하나만
// 격리해서 빠르게 재현/진단한다.
public static class Stage03BotVerify
{
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
    private const string ClientExePath = "ClientBuild/filerpark.exe";
    private const float TimeoutSeconds = 180f;

    private const string KeyActive = "Stage03BotVerify.Active";
    private const string KeyHostStarted = "Stage03BotVerify.HostStarted";
    private const string KeyBotsLaunched = "Stage03BotVerify.BotsLaunched";
    private const string KeyStartTime = "Stage03BotVerify.StartTime";
    private const string KeyLastPosLogTime = "Stage03BotVerify.LastPosLogTime";

    [MenuItem("Tools/Coop Setup/Debug: Stage03 Bot Verify")]
    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("[Stage03Verify] -batchmode에서만 동작합니다. -executeMethod Stage03BotVerify.Run 사용.");
            return;
        }

        EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
        GameObject flowObj = GameObject.Find("GameFlowManager");
        GameFlowManager flow = flowObj != null ? flowObj.GetComponent<GameFlowManager>() : null;
        if (flow == null)
        {
            Debug.LogError("[Stage03Verify] Bootstrap 씬에서 GameFlowManager를 찾지 못했습니다.");
            EditorApplication.Exit(1);
            return;
        }
        flow.stageSceneNames = new[] { "Stage03_KeyRelay" };

        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyHostStarted, false);
        SessionState.SetBool(KeyBotsLaunched, false);
        SessionState.EraseFloat(KeyStartTime);
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
            Debug.Log(started ? "[Stage03Verify] StartHost() 성공." : "[Stage03Verify] StartHost() 실패.");
            if (!started)
            {
                Finish("StartHost 실패", false);
                return;
            }
        }

        if (SessionState.GetBool(KeyHostStarted, false) && !SessionState.GetBool(KeyBotsLaunched, false))
        {
            LaunchBots(4);
            SessionState.SetBool(KeyBotsLaunched, true);
        }

        if (GameFlowManager.Instance != null && GameFlowManager.Instance.phase.Value == GameFlowManager.GamePhase.Results)
        {
            Finish("스테이지 클리어 감지 (phase == Results)", true);
            return;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            float lastLog = SessionState.GetFloat(KeyLastPosLogTime, -100f);
            if (EditorApplication.timeSinceStartup - lastLog > 5f)
            {
                SessionState.SetFloat(KeyLastPosLogTime, (float)EditorApplication.timeSinceStartup);

                CarryableKeyNGO key = Object.FindAnyObjectByType<CarryableKeyNGO>();
                if (key != null)
                {
                    Debug.Log($"[Stage03Verify][diag] key pos=({key.transform.position.x:F2},{key.transform.position.y:F2}) carrier={key.carrierClientId.Value}");
                }

                foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
                {
                    if (kvp.Value.PlayerObject == null) continue;
                    Vector3 pos = kvp.Value.PlayerObject.transform.position;
                    Debug.Log($"[Stage03Verify][diag] client={kvp.Key} pos=({pos.x:F2},{pos.y:F2})");
                }
            }
        }

        float startTime = SessionState.GetFloat(KeyStartTime, (float)EditorApplication.timeSinceStartup);
        if (EditorApplication.timeSinceStartup - startTime > TimeoutSeconds)
        {
            Finish("타임아웃 -- 스테이지가 클리어되지 않음", false);
        }
    }

    private static void LaunchBots(int count)
    {
        string fullPath = Path.GetFullPath(ClientExePath);
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[Stage03Verify] 클라이언트 빌드를 찾을 수 없습니다: {fullPath}");
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

        Debug.Log($"[Stage03Verify] 봇 클라이언트 {count}개 실행.");
    }

    private static void Finish(string reason, bool success)
    {
        SessionState.SetBool(KeyActive, false);
        EditorApplication.update -= Tick;
        Debug.Log($"[Stage03Verify] {(success ? "성공" : "실패")}: {reason} -- 배치 프로세스를 닫습니다.");
        KillBotProcesses();
        EditorApplication.Exit(success ? 0 : 1);
    }

    private static void KillBotProcesses()
    {
        string processName = Path.GetFileNameWithoutExtension(ClientExePath);
        foreach (Process p in Process.GetProcessesByName(processName))
        {
            try { p.Kill(); }
            catch (System.Exception) { }
        }
    }
}
#endif
