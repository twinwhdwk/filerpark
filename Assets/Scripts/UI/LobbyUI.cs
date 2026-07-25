using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// Lobby 씬에 배치. GameFlowManager의 NetworkVariable을 그냥 폴링해서 화면에
// 보여주기만 하는 순수 UI -- 게임 로직에는 관여하지 않는다.
public class LobbyUI : MonoBehaviour
{
    public Text statusText;

    private string primaryHex;
    private string iceHex;

    private void Awake()
    {
        primaryHex = ColorUtility.ToHtmlStringRGB(UITheme.ColorPrimary);
        iceHex = ColorUtility.ToHtmlStringRGB(UITheme.ColorIce);
    }

    private void Update()
    {
        if (statusText == null || GameFlowManager.Instance == null || NetworkManager.Singleton == null)
        {
            return;
        }

        int connected = NetworkManager.Singleton.ConnectedClientsIds.Count;
        int minPlayers = GameFlowManager.Instance.minPlayersToStart;
        float countdown = GameFlowManager.Instance.lobbyCountdownRemaining.Value;

        statusText.text = countdown > 0f
            ? $"접속 인원 <color=#{primaryHex}><b>{connected}</b></color>명\n<color=#{iceHex}><size=64><b>{countdown:F0}</b></size></color>초 후 스테이지 시작"
            : connected >= minPlayers
                ? $"접속 인원 <color=#{primaryHex}><b>{connected}</b></color>명\n곧 시작합니다..."
                : $"접속 인원 <color=#{primaryHex}><b>{connected}</b></color>명\n{minPlayers}명이 모이면 자동으로 시작됩니다";
    }
}
