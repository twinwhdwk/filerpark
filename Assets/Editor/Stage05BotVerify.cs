#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;
using Debug = UnityEngine.Debug;

// Stage 5(쌍둥이 문지기)가 실제로 봇 2개로 클리어되는지 확인하는 일회성 헤드리스
// 검증 도구. HeadlessSmokeTest와 같은 SessionState 패턴(Edit->Play 도메인 리로드에도
// 진행 상태가 살아남게)을 쓰되, StartHost()만 확인하는 게 아니라 실제로 빌드된
// 클라이언트 2개를 봇으로 띄워 GameFlowManager.phase가 Results로 바뀌는지(=
// NotifyStageCleared()가 실제로 호출됐는지) 관찰한다.
//
// GameFlowManager.stageSceneNames를 GameFlowSceneSetup.cs의 "진짜" 5개짜리 배열
// 대신 Stage5 하나로 바꿔치기해서(자연 순환이면 Stage1~4를 먼저 다 깨야 Stage5까지
// 도달한다), Bootstrap.unity 파일 자체는 전혀 건드리지 않고 검증한다 -- Play 모드
// 진입은 씬을 디스크에서 다시 읽어오지 않고 이미 메모리에 있는(에디터에서 방금 연)
// 씬 오브젝트 상태를 그대로 이어받으므로, OpenScene 직후 필드를 바꿔치기하면
// 그 값 그대로 Play가 시작된다.
public static class Stage05BotVerify
{
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
    private const string ClientExePath = "ClientBuild/filerpark.exe";
    private const float TimeoutSeconds = 90f;

    private const string KeyActive = "Stage05BotVerify.Active";
    private const string KeyHostStarted = "Stage05BotVerify.HostStarted";
    private const string KeyBotsLaunched = "Stage05BotVerify.BotsLaunched";
    private const string KeyStartTime = "Stage05BotVerify.StartTime";

    [MenuItem("Tools/Coop Setup/Debug: Stage05 Bot Verify")]
    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("[Stage05Verify] -batchmode에서만 동작합니다 (HeadlessSmokeTest와 동일한 이유 -- " +
                "Finish()가 EditorApplication.Exit()으로 프로세스를 통째로 종료함). -executeMethod Stage05BotVerify.Run 사용.");
            return;
        }

        EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
        GameObject flowObj = GameObject.Find("GameFlowManager");
        GameFlowManager flow = flowObj != null ? flowObj.GetComponent<GameFlowManager>() : null;
        if (flow == null)
        {
            Debug.LogError("[Stage05Verify] Bootstrap 씬에서 GameFlowManager를 찾지 못했습니다.");
            EditorApplication.Exit(1);
            return;
        }
        flow.stageSceneNames = new[] { "Stage05_TwinGatekeeper" };

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
            Debug.Log(started ? "[Stage05Verify] StartHost() 성공." : "[Stage05Verify] StartHost() 실패.");
            if (!started)
            {
                Finish("StartHost 실패", false);
                return;
            }
        }

        if (SessionState.GetBool(KeyHostStarted, false) && !SessionState.GetBool(KeyBotsLaunched, false))
        {
            LaunchBots(2);
            SessionState.SetBool(KeyBotsLaunched, true);
        }

        if (GameFlowManager.Instance != null && GameFlowManager.Instance.phase.Value == GameFlowManager.GamePhase.Results)
        {
            Finish("스테이지 클리어 감지 (phase == Results)", true);
            return;
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
            Debug.LogError($"[Stage05Verify] 클라이언트 빌드를 찾을 수 없습니다: {fullPath} -- 먼저 " +
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

        Debug.Log($"[Stage05Verify] 봇 클라이언트 {count}개 실행.");
    }

    private static void Finish(string reason, bool success)
    {
        SessionState.SetBool(KeyActive, false);
        EditorApplication.update -= Tick;
        Debug.Log($"[Stage05Verify] {(success ? "성공" : "실패")}: {reason} -- 배치 프로세스를 닫습니다.");
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
