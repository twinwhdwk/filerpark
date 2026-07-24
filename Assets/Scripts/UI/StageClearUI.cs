using UnityEngine;
using TMPro;

// Canvas 아래 배너 오브젝트에 부착. GoalZoneNGO의 stageCleared를 지켜보다가
// 클리어되는 순간 배너를 띄운다 -- 게임 로직에는 관여하지 않는 순수 UI.
public class StageClearUI : MonoBehaviour
{
    [Header("연결")]
    public GoalZoneNGO goalZone;
    public GameObject clearBanner;
    public TMP_Text clearText;

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
    }
}
