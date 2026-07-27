#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;

// GUI 없이(-batchmode) 실제로 Play 모드에 들어가 호스트를 띄우고 몇 초 관찰한 뒤
// 스스로 종료하는 헤드리스 스모크 테스트. 사람이 Editor를 열고 Play 버튼 -> "Debug:
// Start Host"를 누르는 수동 절차 대신, 커맨드라인에서 "적어도 크래시 없이 Bootstrap
// -> Lobby -> (인원 충족 시) Stage01까지 뜨는가"를 확인하는 용도다. 5~6인 봇 실서버
// 테스트(GCP)를 대체하지 않는다 -- 그건 여전히 사람이 Editor로 직접 해야 한다
// (CLAUDE.md "Bot simulation" 절 참고). 이건 그보다 훨씬 가벼운, "새로 추가한
// Bootstrap 싱글턴들(AudioManager/PauseMenuUI/ConnectionStatusUI/LoadingScreenUI
// 등)이 실제로 Awake/OnNetworkSpawn 시점에 널 참조 없이 초기화되는가"만 잡아내는
// 최소한의 런타임 확인이다.
//
// Edit->Play 전환은 기본 설정상 반드시 도메인 리로드를 거친다 -- Run()에서 등록한
// 이벤트 구독(EditorApplication.update/playModeStateChanged)은 그 리로드와 함께
// 전부 사라진다. 처음 이 도구를 만들 때 그 사실을 놓쳐서, Play 모드에는 실제로
// 들어갔지만 아무 콜백도 다시 안 불려 StartHost()도 안 되고 타임아웃도 안 걸려
// 프로세스가 영원히 멈춰있는 걸 실제로 겪었다. SessionState(도메인 리로드에도 남는
// 에디터 세션 값)에 진행 상태를 저장해두고, 리로드 직후 다시 실행되는
// [InitializeOnLoadMethod]에서 그 값을 보고 콜백을 다시 걸어야 한다.
public static class HeadlessSmokeTest
{
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
    private const float ObserveSeconds = 10f;

    private const string KeyActive = "HeadlessSmokeTest.Active";
    private const string KeyHostStarted = "HeadlessSmokeTest.HostStarted";
    private const string KeyStartTime = "HeadlessSmokeTest.StartTime";

    // Finish()가 배치 프로세스를 끝내려고 EditorApplication.Exit()을 부르는데,
    // Unity 문서 자체가 이건 커맨드라인(배치) 모드에서만 쓰라고 명시한다 -- 사람이
    // Editor를 열어둔 채 이 메뉴를 실수로 눌러도 그대로 실행되면, 관찰 시간이 끝나는
    // 순간 저장 안 한 씬/애셋 변경사항까지 통째로 Editor가 종료되며 날아간다. 배치
    // 모드가 아니면 아예 시작하지 않는다.
    [MenuItem("Tools/Coop Setup/Debug: Headless Host Smoke Test")]
    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("[SmokeTest] 이 도구는 -batchmode로 실행한 Editor에서만 동작합니다 " +
                "(Finish()가 EditorApplication.Exit()으로 프로세스 자체를 종료하므로, 평소 열어둔 " +
                "Editor에서 실행하면 저장 안 한 변경사항까지 날아갑니다). 헤드리스로 " +
                "-executeMethod HeadlessSmokeTest.Run 을 쓰세요.");
            return;
        }

        EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyHostStarted, false);
        SessionState.EraseFloat(KeyStartTime);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void ReattachAfterDomainReload()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;

        EditorApplication.update -= Tick; // 혹시 이미 걸려있다면 중복 등록 방지.
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
            Finish("Play 모드가 이미 종료된 상태를 감지");
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
            Debug.Log(started ? "[SmokeTest] StartHost() 성공." : "[SmokeTest] StartHost() 실패.");
        }

        float startTime = SessionState.GetFloat(KeyStartTime, (float)EditorApplication.timeSinceStartup);
        if (EditorApplication.timeSinceStartup - startTime > ObserveSeconds)
        {
            Finish("관찰 시간 종료");
        }
    }

    private static void Finish(string reason)
    {
        SessionState.SetBool(KeyActive, false);
        EditorApplication.update -= Tick;
        Debug.Log($"[SmokeTest] {reason} -- 배치 프로세스를 닫습니다.");
        EditorApplication.Exit(0);
    }
}
#endif
