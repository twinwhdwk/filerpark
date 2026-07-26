using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// 정상적인 종료(사용자가 직접 일시정지 메뉴에서 "나가기"를 누른 경우)가 아니라
// 네트워크 문제로 접속이 끊겼을 때, 화면이 아무 설명 없이 그대로 멈춘 것처럼
// 보이는 대신 명확한 안내 오버레이를 띄운다. 재접속은 사용자가 직접 버튼을
// 눌러야 시도되도록 한다 -- 자동 재시도 루프는 서버가 계속 죽어있는 상태에서
// 무한 재시도 스팸이 될 수 있어, 사용자가 인지하고 다시 시도하는 편이 낫다.
public class ConnectionStatusUI : MonoBehaviour
{
    public GameObject overlayPanel;
    public Text messageText;

    private void OnEnable()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleDisconnected;
        NetworkManager.Singleton.OnClientConnectedCallback += HandleConnected;
        if (overlayPanel != null) overlayPanel.SetActive(false);
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleDisconnected;
        NetworkManager.Singleton.OnClientConnectedCallback -= HandleConnected;
    }

    private void HandleConnected(ulong clientId)
    {
        if (overlayPanel != null) overlayPanel.SetActive(false);
    }

    private void HandleDisconnected(ulong clientId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.IsServer) return; // 데디케이티드 서버/호스트 자신은 이 UI 대상이 아님.
        if (clientId != manager.LocalClientId) return; // 다른 플레이어의 접속 해제는 스코어보드로 이미 드러남.

        if (overlayPanel != null) overlayPanel.SetActive(true);
        if (messageText != null)
        {
            messageText.text = "서버와의 연결이 끊어졌습니다.";
        }
    }

    public void OnRetryClicked()
    {
        AudioManager.Instance?.PlaySfx(SfxId.UIClick);
        if (overlayPanel != null) overlayPanel.SetActive(false);

        NetworkBootstrapper bootstrapper = Object.FindFirstObjectByType<NetworkBootstrapper>();
        if (bootstrapper != null)
        {
            bootstrapper.ConnectToServer();
        }
    }

    public void OnQuitClicked()
    {
        AudioManager.Instance?.PlaySfx(SfxId.UIClick);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
