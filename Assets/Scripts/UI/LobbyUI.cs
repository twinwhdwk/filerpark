using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// Lobby 씬에 배치. GameFlowManager의 NetworkVariable을 그냥 폴링해서 화면에
// 보여주기만 하는 순수 UI -- 게임 로직에는 관여하지 않는다.
public class LobbyUI : MonoBehaviour
{
    public Text statusText;
    public Button readyButton;
    public Text readyButtonLabel;

    private string primaryHex;
    private string iceHex;

    // 이 클라이언트가 준비 버튼을 눌렀는지 로컬로만 추적한다(서버 상태의 정답은
    // GameFlowManager.readyCount지만 그건 총원 숫자만 보여주지 "나"의 상태는 안
    // 알려준다). Lobby 씬은 매 사이클 언로드/재로드되므로 Awake()에서 false로
    // 시작하는 게 항상 맞다 -- 새 라운드는 항상 다시 준비를 눌러야 한다.
    private bool localReady;

    // 화면에 실제로 보이는 값(초 단위로 반올림한 카운트다운, 접속/준비 인원, 상태)이
    // 안 바뀌었으면 Text.text를 다시 대입하지 않는다 -- 예전엔 매 프레임(초당 60번)
    // 문자열을 새로 보간해서 대입했는데, 카운트다운은 초당 1번만 화면상 값이
    // 바뀌므로 나머지 프레임은 낭비였다.
    private int lastConnected = -1;
    private int lastReady = -1;
    private int lastCountdownWhole = int.MinValue;
    private int lastState = -1; // 0=인원 채움 대기 중, 1=전원 준비 완료, 2=준비 대기 중
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
        UpdateReadyButtonLabel();
    }

    // 대기실에서 바로 자동 시작되던 걸, 참가자가 명시적으로 눌러야 하는 준비 버튼으로
    // 바꿨다 -- GameFlowManager.RequestReadyServerRpc가 실제 판정을 갖고 있고, 여기서는
    // 로컬 상태만 토글해 서버에 알린다.
    public void OnReadyClicked()
    {
        localReady = !localReady;
        GameFlowManager.Instance?.RequestReadyServerRpc(localReady);
        UpdateReadyButtonLabel();
        AudioManager.Instance?.PlaySfx(SfxId.UIClick);
    }

    private void UpdateReadyButtonLabel()
    {
        if (readyButtonLabel != null)
        {
            readyButtonLabel.text = localReady ? "준비 완료 (취소하려면 클릭)" : "시작 준비";
        }
    }

    private void Update()
    {
        if (statusText == null || GameFlowManager.Instance == null || NetworkManager.Singleton == null)
        {
            return;
        }

        int connected = NetworkManager.Singleton.ConnectedClientsIds.Count;
        int ready = GameFlowManager.Instance.readyCount.Value;
        int required = GameFlowManager.Instance.requiredHeadcountToStart;
        float countdown = GameFlowManager.Instance.lobbyCountdownRemaining.Value;
        int countdownWhole = Mathf.RoundToInt(countdown);
        bool allReady = connected > 0 && ready >= connected;
        int state = countdown > 0f ? 0 : (allReady ? 1 : 2);
        bool hasSelection = GameFlowManager.Instance.selectedStageIndexPreview.Value >= 0;

        if (connected == lastConnected && ready == lastReady && countdownWhole == lastCountdownWhole && state == lastState && hasSelection == lastHasSelection)
        {
            ApplyPulse();
            return;
        }

        // 접속/준비 인원이나 상태가 바뀐 게 아니라 카운트다운 숫자만 매 초 줄어드는
        // 경우에만 "똑딱" 펄스를 건다 -- 상태 전환(대기->카운트다운 등)은 텍스트 자체가
        // 완전히 바뀌므로 매초 펄스와는 다른 신호라 겹치면 오히려 산만하다.
        bool onlyCountdownTicked = state == 0 && lastState == 0 && connected == lastConnected && ready == lastReady && hasSelection == lastHasSelection;

        lastConnected = connected;
        lastReady = ready;
        lastCountdownWhole = countdownWhole;
        lastState = state;
        lastHasSelection = hasSelection;

        string baseText = state == 0
            ? $"준비 완료 <color=#{primaryHex}><b>{ready}/{connected}</b></color>명 -- 인원이 부족해 <color=#{iceHex}><size=56><b>{countdownWhole}</b></size></color>초 후 봇이 자동으로 참가합니다"
            : state == 1
                ? $"전원 준비 완료! 곧 시작합니다..."
                : $"준비 완료 <color=#{primaryHex}><b>{ready}/{connected}</b></color>명\n모두 [시작 준비]를 누르면 시작됩니다 (스테이지를 깨려면 최소 {required}명 필요, 부족하면 봇이 채워줍니다)";

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
