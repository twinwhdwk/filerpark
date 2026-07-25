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

    private void OnEnable()
    {
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
        if (!newValue || clearBanner == null) return;

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
    }
}
