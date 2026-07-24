using UnityEngine;
using Unity.Netcode;
using TMPro;

// Canvas 아래 상태 텍스트(TextMeshProUGUI)에 부착할 필요 없이, 이 스크립트가 붙은
// 오브젝트의 Inspector에 그 Text를 드래그해서 연결한다. 서버/클라이언트 접속 이벤트를
// 구독해서 현재 접속 인원을 화면에 표시하는 순수 UI 스크립트 -- 게임 로직에는 관여하지 않는다.
public class NetworkStatusUI : MonoBehaviour
{
    [Header("UI 연결")]
    public TMP_Text statusText;

    private void OnEnable()
    {
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        NetworkManager.Singleton.OnServerStarted += RefreshStatus;
        NetworkManager.Singleton.OnClientStarted += RefreshStatus;

        RefreshStatus();
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        NetworkManager.Singleton.OnServerStarted -= RefreshStatus;
        NetworkManager.Singleton.OnClientStarted -= RefreshStatus;
    }

    private void HandleClientConnected(ulong clientId) => RefreshStatus();
    private void HandleClientDisconnected(ulong clientId) => RefreshStatus();

    private void RefreshStatus()
    {
        if (statusText == null) return;

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || (!manager.IsServer && !manager.IsClient))
        {
            statusText.text = "서버 접속 대기 중...";
            return;
        }

        int playerCount = manager.ConnectedClientsIds.Count;
        statusText.text = manager.IsServer
            ? $"서버 가동 중 · 접속 {playerCount}명"
            : $"접속됨 · 함께 {playerCount}명";
    }
}
