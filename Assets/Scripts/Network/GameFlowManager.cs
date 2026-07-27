using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

// Bootstrap 씬에 (NetworkManager와는 별개의 오브젝트로) 씬 배치된 NetworkObject.
// 서버 권위로 Lobby <-> Stage를 addititve 로드/언로드하며 오간다.
//
// 이식성/격리 원칙:
//  - 스테이지 목록은 stageSceneNames 배열 하나뿐이다. 새 스테이지 추가 = 씬 파일 하나 +
//    이 배열에 이름 한 줄 + Build Settings에 씬 등록. 다른 스크립트는 손댈 필요 없다.
//  - 각 스테이지 씬은 자기 SpawnPoint/기믹만 알면 되고, "이 스테이지 다음에 뭐가 오는지"는
//    전혀 몰라도 된다 -- StageManagerNGO는 그냥 GameFlowManager.Instance.NotifyStageCleared()를
//    부르기만 하면 끝이다.
//  - Bootstrap 씬은 절대 언로드되지 않고, 액티브 씬도 계속 Bootstrap으로 유지한다
//    (SetActiveScene을 따로 안 부름) -- 그래야 Instantiate/Spawn되는 오브젝트(플레이어 등)가
//    항상 Bootstrap에 남아서, Lobby/Stage를 Additive Unload해도 플레이어가 같이 사라지지 않는다.
//
// 4~6봇 테스트에서 실제로 걸렸던 함정: Unload와 Load를 같은 프레임에 연달아 호출하면
// NGO가 "이미 씬 이벤트가 진행 중"이라며 뒤의 호출을 조용히 무시한다(반환값을 안
// 보면 에러도 없이 그냥 멈춘 것처럼 보인다). 그래서 반드시 OnUnloadEventCompleted를
// 기다렸다가 다음 LoadScene을 호출한다 -- pendingLoadSceneName이 그 대기 상태를 담는다.
public class GameFlowManager : NetworkBehaviour
{
    public enum GamePhase { Lobby, InStage, Results }

    public static GameFlowManager Instance { get; private set; }

    // 월드맵 UI(StageIntroUI.OnStartPressed -> RequestSelectStageServerRpc)는 여기
    // 있는 이름과 Assets/StageData/*.asset의 StageDefinition.sceneName을 문자열로
    // 매칭한다 -- 이 배열에서 스테이지를 추가/이름 변경/제거하면
    // NetworkSetupMenu.CreateStageCatalog()(Tools/Coop Setup/7)도 다시 실행해서
    // 카탈로그를 맞춰야 한다. 안 맞으면 월드맵의 "시작" 버튼이 콘솔 경고만 남기고
    // 조용히 아무 일도 안 한다 -- 다만 그래도 로비 자동 순환(순서대로 진행)은 이
    // 배열만으로 그대로 동작하므로 게임 자체가 멈추지는 않는다.
    [Header("스테이지 목록 (씬 이름, 이 배열만 수정하면 스테이지 추가/순서 변경 가능)")]
    public string[] stageSceneNames = { "Stage01_Gatekeeper" };

    [Header("씬 이름")]
    public string lobbySceneName = "Lobby";

    [Header("로비 시작 조건")]
    public int minPlayersToStart = 1;
    public float lobbyCountdownSeconds = 5f;

    [Header("결과 화면 표시 시간")]
    public float resultsDisplaySeconds = 4f;

    public readonly NetworkVariable<GamePhase> phase = new NetworkVariable<GamePhase>(
        GamePhase.Lobby, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> currentStageIndex = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<float> lobbyCountdownRemaining = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 로비의 월드맵 UI에서 플레이어가 직접 스테이지를 고르면 이 값이 채워진다(-1=미선택).
    // 아무도 안 고르면 예전처럼 순서대로 자동 진행되므로, 이 기능이 없어도 이미 검증된
    // 자동 순환 흐름은 그대로 동작한다 -- StartNextStage()의 순서 로직 위에 "다음 한
    // 번만" 우선하는 선택을 얹을 뿐이다. 서버는 자기 자신의 NetworkVariable을 그냥
    // 직접 읽으면 되므로(네트워크 왕복이 필요 없음), 별도의 private 미러 필드를 두지
    // 않는다 -- 예전엔 뒀었는데, 두 값이 항상 같이 바뀌어야 하는데도 서로 어긋날 수
    // 있는 불필요한 이중 상태였다.
    public readonly NetworkVariable<int> selectedStageIndexPreview = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly Dictionary<ulong, int> scores = new Dictionary<ulong, int>();

    private bool lobbySceneLoaded;
    private bool countdownActive;
    private float countdownEndsAt;

    // Unload 완료를 기다렸다가 이 이름의 씬을 로드한다. 비어있으면 대기 중인 로드가 없다는 뜻.
    private string pendingLoadSceneName;

    public override void OnNetworkSpawn()
    {
        Instance = this;

        if (!IsServer) return;

        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
        NetworkManager.Singleton.SceneManager.OnUnloadEventCompleted += HandleUnloadEventCompleted;

        BeginLoadScene(lobbySceneName);
        lobbySceneLoaded = true;
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;

        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            NetworkManager.Singleton.SceneManager.OnUnloadEventCompleted -= HandleUnloadEventCompleted;
        }
    }

    // scores에서 접속 해제된 clientId를 안 지우면, 24/7 떠 있는 GCP 데디케이티드
    // 서버에서 사람들이 들어왔다 나갈 때마다 유령 항목이 영원히 쌓인다 -- NGO는
    // clientId를 재사용하지 않으므로 재접속으로도 덮어써지지 않는다. 스코어보드에
    // 접속 해제된 플레이어가 계속 표시되는 게 눈에 보이는 증상이다.
    private void HandleClientDisconnected(ulong clientId)
    {
        if (scores.Remove(clientId))
        {
            BroadcastScoreboard();
        }
    }

    private void BeginLoadScene(string sceneName)
    {
        SceneEventProgressStatus status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
        Debug.Log($"[GameFlow] LoadScene({sceneName}) 요청 -> {status}");
    }

    private void BeginUnloadScene(Scene scene, string pendingNextLoad)
    {
        pendingLoadSceneName = pendingNextLoad;
        SceneEventProgressStatus status = NetworkManager.Singleton.SceneManager.UnloadScene(scene);
        Debug.Log($"[GameFlow] UnloadScene({scene.name}) 요청 -> {status} (완료 후 로드 대기: {pendingNextLoad})");
    }

    // 씬 로드가 서버+접속된 모든 클라이언트에 걸쳐 실제로 끝난 시점에만 스폰 지점이
    // 확실히 존재한다고 볼 수 있다 -- LoadScene 호출 직후가 아니라 이 콜백에서
    // 플레이어들을 재배치한다.
    private void HandleLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        Debug.Log($"[GameFlow] 씬 로드 완료: {sceneName} (완료 {clientsCompleted.Count}명, 타임아웃 {clientsTimedOut.Count}명)");

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            RepositionPlayer(clientId);
        }
    }

    // Unload가 실제로 끝난 뒤에만 다음 씬을 로드한다 -- 같은 프레임에 Load를 바로
    // 부르면 NGO가 진행 중인 이벤트가 있다며 조용히 무시하는 걸 실측으로 확인했다.
    private void HandleUnloadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        Debug.Log($"[GameFlow] 씬 언로드 완료: {sceneName}");

        if (string.IsNullOrEmpty(pendingLoadSceneName)) return;

        string nextScene = pendingLoadSceneName;
        pendingLoadSceneName = null;
        BeginLoadScene(nextScene);
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!scores.ContainsKey(clientId))
        {
            scores[clientId] = 0;
        }

        if (phase.Value == GamePhase.Lobby)
        {
            RepositionPlayer(clientId);
        }

        BroadcastScoreboard();
    }

    private void RepositionPlayer(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return;
        if (client.PlayerObject == null) return;

        PlayerSetupNGO setup = client.PlayerObject.GetComponent<PlayerSetupNGO>();
        if (setup != null)
        {
            setup.MoveToSpawnPoint();
        }
    }

    private void Update()
    {
        if (!IsServer) return;
        if (phase.Value != GamePhase.Lobby) return;

        int connected = NetworkManager.Singleton.ConnectedClientsIds.Count;
        if (connected >= minPlayersToStart)
        {
            if (!countdownActive)
            {
                countdownActive = true;
                countdownEndsAt = Time.time + lobbyCountdownSeconds;
                Debug.Log($"[GameFlow] 시작 카운트다운 개시 ({lobbyCountdownSeconds}초, 접속 {connected}명)");
            }
            // NetworkVariable에 대입할 때마다(값이 미세하게라도 다르면) NGO가 다음 네트워크
            // 틱에 모든 클라이언트로 그 변경을 복제한다 -- 매 프레임(초당 수십 번) 정확한
            // 실수값을 대입하면 정작 유일한 소비처인 LobbyUI.cs가 반올림해서 초 단위로만
            // 보여주는 값을 위해 그만큼의 트래픽을 매 프레임 뿌리는 셈이다. 화면에 실제로
            // 보이는 정수 초가 바뀔 때만 써서 복제 빈도를 초당 1회 수준으로 줄인다.
            float remaining = Mathf.Max(0f, countdownEndsAt - Time.time);
            if (Mathf.RoundToInt(remaining) != Mathf.RoundToInt(lobbyCountdownRemaining.Value))
            {
                lobbyCountdownRemaining.Value = remaining;
            }
            if (Time.time >= countdownEndsAt)
            {
                StartNextStage();
            }
        }
        else if (countdownActive)
        {
            countdownActive = false;
            lobbyCountdownRemaining.Value = 0f;
            Debug.Log("[GameFlow] 인원 부족으로 카운트다운 취소");
        }
    }

    // 월드맵 UI(Lobby 씬의 WorldMapUI/StageIntroUI)에서 스테이지 카드의 "시작" 버튼을
    // 누르면 호출된다. 실제 로드는 여기서 하지 않는다 -- 로비 카운트다운이 끝나는
    // 시점에 StartNextStage()가 이 값을 읽어가도록 예약만 해둔다. RequireOwnership을
    // false로 둔 이유는 아무 접속 클라이언트나(호스트가 아니어도) 다음 스테이지를
    // 제안할 수 있게 하기 위함 -- 소규모 친구 파티 게임이라 악의적 요청을 걱정할
    // 필요가 없다.
    [ServerRpc(RequireOwnership = false)]
    public void RequestSelectStageServerRpc(string sceneName, ServerRpcParams rpcParams = default)
    {
        if (phase.Value != GamePhase.Lobby) return;

        for (int i = 0; i < stageSceneNames.Length; i++)
        {
            if (stageSceneNames[i] == sceneName)
            {
                selectedStageIndexPreview.Value = i;
                Debug.Log($"[GameFlow] 플레이어가 다음 스테이지로 {sceneName}을 선택했습니다.");
                return;
            }
        }

        Debug.LogWarning($"[GameFlow] 알 수 없는 스테이지 선택 요청: {sceneName}");
    }

    private void StartNextStage()
    {
        if (stageSceneNames.Length == 0)
        {
            Debug.LogError("[GameFlow] stageSceneNames가 비어 있어 스테이지를 시작할 수 없습니다.");
            return;
        }

        int nextIndex;
        int selected = selectedStageIndexPreview.Value;
        if (selected >= 0 && selected < stageSceneNames.Length)
        {
            nextIndex = selected;
        }
        else
        {
            nextIndex = currentStageIndex.Value + 1;
            if (nextIndex >= stageSceneNames.Length)
            {
                nextIndex = 0;
            }
        }
        selectedStageIndexPreview.Value = -1;

        currentStageIndex.Value = nextIndex;
        phase.Value = GamePhase.InStage;
        countdownActive = false;
        lobbyCountdownRemaining.Value = 0f;

        string stageName = stageSceneNames[nextIndex];
        Debug.Log($"[GameFlow] 스테이지 시작 절차 개시: {stageName} (index {nextIndex})");

        if (lobbySceneLoaded)
        {
            Scene lobbyScene = SceneManager.GetSceneByName(lobbySceneName);
            lobbySceneLoaded = false;
            if (lobbyScene.IsValid())
            {
                BeginUnloadScene(lobbyScene, stageName);
                return;
            }
        }

        // 로비가 로드되어 있지 않았다면(이례적인 경우) 바로 로드한다.
        BeginLoadScene(stageName);
    }

    // 스테이지 내부의 StageManagerNGO가 GoalZoneNGO 클리어를 감지하면 이걸 부른다.
    // 스테이지 스크립트는 "다음에 뭐가 오는지" 전혀 몰라도 되도록, 이후 흐름은 전부
    // 여기서만 결정한다.
    public void NotifyStageCleared()
    {
        if (!IsServer || phase.Value != GamePhase.InStage) return;

        Debug.Log($"[GameFlow] 스테이지 클리어: {stageSceneNames[currentStageIndex.Value]}");

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            scores.TryGetValue(clientId, out int current);
            scores[clientId] = current + 1;
        }
        BroadcastScoreboard();

        phase.Value = GamePhase.Results;
        Invoke(nameof(ReturnToLobbyAfterResults), resultsDisplaySeconds);
    }

    private void ReturnToLobbyAfterResults()
    {
        if (!IsServer) return;

        string clearedStageName = stageSceneNames[currentStageIndex.Value];
        Scene stageScene = SceneManager.GetSceneByName(clearedStageName);

        phase.Value = GamePhase.Lobby;
        // currentStageIndex는 여기서 리셋하지 않는다 -- StartNextStage()가
        // "currentStageIndex + 1"로 다음 스테이지를 고르므로, 여기서 -1로 되돌리면
        // 매번 0번(Stage01)부터 다시 시작해 버려 stageSceneNames의 나머지 스테이지가
        // 영원히 실행되지 않는다(1개짜리 배열일 때는 어차피 항상 0번이라 안 드러났던
        // 버그). 순서 진행은 StartNextStage()의 래핑 로직에 맡긴다.
        lobbySceneLoaded = true;

        if (stageScene.IsValid())
        {
            BeginUnloadScene(stageScene, lobbySceneName);
        }
        else
        {
            BeginLoadScene(lobbySceneName);
        }
    }

    private void BroadcastScoreboard()
    {
        int count = scores.Count;
        ulong[] clientIds = new ulong[count];
        int[] values = new int[count];
        int i = 0;
        foreach (KeyValuePair<ulong, int> entry in scores)
        {
            clientIds[i] = entry.Key;
            values[i] = entry.Value;
            i++;
        }

        ScoreboardUpdatedClientRpc(clientIds, values);
    }

    [ClientRpc]
    private void ScoreboardUpdatedClientRpc(ulong[] clientIds, int[] values)
    {
        ScoreboardUI.UpdateScoreboard(clientIds, values);
    }
}
