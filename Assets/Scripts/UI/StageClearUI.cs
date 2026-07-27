using UnityEngine;
using UnityEngine.UI;

// Canvas 아래 배너 오브젝트에 부착. GoalZoneNGO의 stageCleared를 지켜보다가
// 클리어되는 순간 배너를 띄운다 -- 게임 로직에는 관여하지 않는 순수 UI.
// 레거시 UI.Text를 쓴다 (TMP_Text가 아님) -- 이 프로젝트는 TMP Essentials를 임포트한
// 적이 없어서 TMP_Text로 렌더링하면 폰트 없이 빈 텍스트로 나온다 (CLAUDE.md 참고).
public class StageClearUI : MonoBehaviour
{
    [Header("연결")]
    public GoalZoneNGO goalZone;
    public GameObject clearBanner;
    public Text clearText;
    public Text clearSubText;
    public Text clearTimeText;

    // BuildStageUI가 넘겨주는 스테이지 라벨("Stage 1 · Gatekeeper" 등)을 그대로
    // PlayerPrefs 키로 쓴다 -- 이미 스테이지마다 고유하므로 별도 ID 체계가 필요 없다.
    // 순수 로컬 개인 기록이라 서버 동기화가 필요 없다: 클라이언트마다 자기 화면에
    // 뜨는 시간만 재면 되고("이 판이 몇 초 걸렸나"), 누가 더 빠른지 겨루는 랭킹
    // 기능이 아니다.
    public string stageId;

    private float stageStartTime;

    private string BestTimeKey => $"BestTime_{stageId}";

    private void OnEnable()
    {
        stageStartTime = Time.time;

        if (goalZone != null)
        {
            goalZone.stageCleared.OnValueChanged += HandleStageCleared;
        }
        if (clearBanner != null)
        {
            clearBanner.SetActive(false);
        }
    }

    private void OnDisable()
    {
        if (goalZone != null)
        {
            goalZone.stageCleared.OnValueChanged -= HandleStageCleared;
        }
    }

    private void HandleStageCleared(bool previousValue, bool newValue)
    {
        if (!newValue) return;

        AudioManager.Instance?.PlaySfx(SfxId.StageClear);
        if (clearBanner == null) return;

        clearBanner.SetActive(true);
        if (clearText != null)
        {
            clearText.text = "CLEAR!";
        }
        // "CLEAR!"만 뜨고 몇 초간 아무 안내 없이 멈춰 있으면 멎은 것처럼 보인다 --
        // GameFlowManager.resultsDisplaySeconds 동안 실제로는 로비로 돌아갈 준비를
        // 하고 있다는 걸 알려준다. 정확한 초 단위 카운트다운은 아니다 (StageClearUI는
        // GameFlowManager를 몰라도 되게 만든 의도적 설계라, resultsDisplaySeconds 값을
        // 끌어오지 않는다) -- 단순 안내 문구로 충분하다.
        if (clearSubText != null)
        {
            clearSubText.text = "대기실로 돌아갑니다...";
        }

        ReportClearTime();
    }

    private void ReportClearTime()
    {
        if (clearTimeText == null || string.IsNullOrEmpty(stageId)) return;

        float elapsed = Time.time - stageStartTime;
        float best = PlayerPrefs.GetFloat(BestTimeKey, float.MaxValue);
        bool isNewRecord = elapsed < best;

        if (isNewRecord)
        {
            PlayerPrefs.SetFloat(BestTimeKey, elapsed);
            PlayerPrefs.Save();
            clearTimeText.text = $"기록: {FormatTime(elapsed)} - NEW RECORD!";
        }
        else
        {
            clearTimeText.text = $"기록: {FormatTime(elapsed)} (최고 기록: {FormatTime(best)})";
        }
    }

    private static string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        float remainder = seconds - minutes * 60f;
        return $"{minutes}:{remainder:00.0}";
    }
}
