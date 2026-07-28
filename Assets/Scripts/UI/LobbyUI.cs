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

    // 카운트다운 숫자가 1초마다 바뀔 때 조용히 텍스트만 갈아끼우면 "똑딱거린다"는
    // 느낌이 안 산다 -- ScoreboardUI의 갱신 펄스와 동일한 패턴으로, 매 초 살짝
    // 튀어오르는 스케일 펄스를 얹는다.
    private const float PulseDuration = 0.18f;
    private const float PulseScale = 1.15f;
    private float pulseStartTime = -1f;

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
            ApplyPulse();
            return;
        }

        // 접속 인원/상태가 바뀐 게 아니라 카운트다운 숫자만 매 초 줄어드는 경우에만
        // "똑딱" 펄스를 건다 -- 상태 전환(대기->카운트다운 등)은 텍스트 자체가 완전히
        // 바뀌므로 매초 펄스와는 다른 신호라 겹치면 오히려 산만하다.
        bool onlyCountdownTicked = state == 0 && lastState == 0 && connected == lastConnected && hasSelection == lastHasSelection;

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

        if (onlyCountdownTicked) pulseStartTime = Time.unscaledTime;
        ApplyPulse();
    }

    private void ApplyPulse()
    {
        if (pulseStartTime < 0f) return;
        float t = Mathf.Clamp01((Time.unscaledTime - pulseStartTime) / PulseDuration);
        float scale = Mathf.Lerp(PulseScale, 1f, t);
        statusText.transform.localScale = Vector3.one * scale;
        if (t >= 1f) pulseStartTime = -1f;
    }
}
