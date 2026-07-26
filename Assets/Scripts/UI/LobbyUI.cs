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

    // 화면에 실제로 보이는 값(초 단위로 반올림한 카운트다운, 접속 인원, 상태)이
    // 안 바뀌었으면 Text.text를 다시 대입하지 않는다 -- 예전엔 매 프레임(초당 60번)
    // 문자열을 새로 보간해서 대입했는데, 카운트다운은 초당 1번만 화면상 값이
    // 바뀌므로 나머지 프레임은 낭비였다.
    private int lastConnected = -1;
    private int lastCountdownWhole = int.MinValue;
    private int lastState = -1; // 0=카운트다운 중, 1=곧 시작, 2=대기 중
    private bool lastHasSelection;

    private void Awake()
    {
        primaryHex = ColorUtility.ToHtmlStringRGB(UITheme.ColorPrimary);
        iceHex = ColorUtility.ToHtmlStringRGB(UITheme.ColorIce);
        AudioManager.Instance?.PlayLobbyMusic();
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
        int countdownWhole = Mathf.RoundToInt(countdown);
        int state = countdown > 0f ? 0 : (connected >= minPlayers ? 1 : 2);
        bool hasSelection = GameFlowManager.Instance.selectedStageIndexPreview.Value >= 0;

        if (connected == lastConnected && countdownWhole == lastCountdownWhole && state == lastState && hasSelection == lastHasSelection)
        {
            return;
        }
        lastConnected = connected;
        lastCountdownWhole = countdownWhole;
        lastState = state;
        lastHasSelection = hasSelection;

        string baseText = state == 0
            ? $"접속 인원 <color=#{primaryHex}><b>{connected}</b></color>명\n<color=#{iceHex}><size=64><b>{countdownWhole}</b></size></color>초 후 스테이지 시작"
            : state == 1
                ? $"접속 인원 <color=#{primaryHex}><b>{connected}</b></color>명\n곧 시작합니다..."
                : $"접속 인원 <color=#{primaryHex}><b>{connected}</b></color>명\n{minPlayers}명이 모이면 자동으로 시작됩니다";

        statusText.text = hasSelection
            ? baseText + $"\n<color=#{iceHex}>다음 스테이지가 선택되었습니다</color>"
            : baseText;
    }
}
