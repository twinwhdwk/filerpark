#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;

// LobbyReadyFillVerify/HeadlessSmokeTest는 검증이 끝나면 스스로 EditorApplication.Exit()
// 해버려서, 그 사이에 실제 화면 캡처용 클라이언트 프로세스를 띄우고 연결시킬 시간이
// 부족했다(순차적인 여러 툴 호출의 누적 지연만으로도 5초 채움-대기 창을 놓쳤다). 이
// 도구는 StartHost()만 하고 계속 떠 있는다 -- 스스로 종료하지 않으므로, 밖에서 클라이언트
// 프로세스를 원하는 타이밍에 붙이고 필요한 만큼 관찰한 뒤 taskkill로 직접 종료시킨다.
public static class LocalTestHost
{
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";

    private const string KeyActive = "LocalTestHost.Active";
    private const string KeyHostStarted = "LocalTestHost.HostStarted";

    [MenuItem("Tools/Coop Setup/Debug: Local Test Host (stays open)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyHostStarted, false);
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
        if (!SessionState.GetBool(KeyActive, false) || !EditorApplication.isPlaying)
        {
            EditorApplication.update -= Tick;
            return;
        }

        if (!SessionState.GetBool(KeyHostStarted, false) && NetworkManager.Singleton != null)
        {
            bool started = NetworkManager.Singleton.StartHost();
            SessionState.SetBool(KeyHostStarted, started);
            Debug.Log(started ? "[LocalTestHost] StartHost() 성공 -- 계속 떠 있습니다(수동 종료 필요)." : "[LocalTestHost] StartHost() 실패.");
        }
    }
}
#endif
