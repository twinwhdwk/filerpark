using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// Lobby 씬에 배치. GameFlowManager의 NetworkVariable을 그냥 폴링해서 화면에
// 보여주기만 하는 순수 UI -- 게임 로직에는 관여하지 않는다.
public class LobbyUI : MonoBehaviour
{
    public Text statusText;

    private void Update()
    {
        if (statusText == null || GameFlowManager.Instance == null || NetworkManager.Singleton == null)
        {
            return;
        }

        int connected = NetworkManager.Singleton.ConnectedClientsIds.Count;
        float countdown = GameFlowManager.Instance.lobbyCountdownRemaining.Value;

        statusText.text = countdown > 0f
            ? $"대기실 -- 접속 {connected}명\n{countdown:F0}초 후 스테이지 시작"
            : $"대기실 -- 접속 {connected}명\n인원이 모이면 자동 시작됩니다";
    }
}
