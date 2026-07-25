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
        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            NetworkManager.Singleton.SceneManager.OnUnloadEventCompleted -= HandleUnloadEventCompleted;
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
            lobbyCountdownRemaining.Value = Mathf.Max(0f, countdownEndsAt - Time.time);
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

    private void StartNextStage()
    {
        if (stageSceneNames.Length == 0)
        {
            Debug.LogError("[GameFlow] stageSceneNames가 비어 있어 스테이지를 시작할 수 없습니다.");
            return;
        }

        int nextIndex = currentStageIndex.Value + 1;
        if (nextIndex >= stageSceneNames.Length)
        {
            nextIndex = 0;
        }

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
        currentStageIndex.Value = -1;
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
