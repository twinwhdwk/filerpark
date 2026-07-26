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
        // 데디케이티드 서버/호스트 자신은 이 UI 대상이 아님. LocalClientId와 비교하는
        // 방식은 쓰지 않는다 -- 접속 해제 처리 도중 LocalClientId가 언제 리셋되는지
        // NGO 내부 타이밍에 좌우되어, 정작 감지하려는 "내 접속이 끊긴" 상황 자체를
        // 놓칠 수 있다. 클라이언트 빌드에서 이 콜백은 사실상 자기 자신의 접속
        // 해제에만 발생하므로 IsServer만으로 충분하다.
        if (manager == null || manager.IsServer) return;

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
