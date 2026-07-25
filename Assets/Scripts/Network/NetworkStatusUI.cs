using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// 이 스크립트가 붙은 오브젝트의 Inspector에 Text를 드래그해서 연결한다. 서버/클라이언트
// 접속 이벤트를 구독해서 현재 접속 인원을 화면에 표시하는 순수 UI 스크립트 -- 게임
// 로직에는 관여하지 않는다.
// 레거시 UI.Text를 쓴다 (TMP_Text가 아님) -- 이 프로젝트는 TMP Essentials를 임포트한
// 적이 없어서 TMP_Text로 렌더링하면 폰트 없이 빈 텍스트로 나온다 (StageClearUI.cs와
// 동일한 이유, CLAUDE.md 참고). 이 컴포넌트는 현재 어떤 씬에도 연결돼 있지 않지만,
// 나중에 실제로 쓰기 시작할 때 이 버그를 다시 밟지 않도록 미리 고쳐둔다.
public class NetworkStatusUI : MonoBehaviour
{
    [Header("UI 연결")]
    public Text statusText;

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
