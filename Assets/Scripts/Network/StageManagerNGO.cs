using UnityEngine;

// 스테이지 씬 안에 배치하는 컴포넌트 (그 스테이지의 GoalZoneNGO를 참조). 이 스테이지
// 다음에 무엇이 오는지는 전혀 모른다 -- 클리어를 감지하면 그냥
// GameFlowManager.Instance.NotifyStageCleared()만 부르고, 로비 복귀/다음 스테이지
// 진행/스코어 갱신은 전부 GameFlowManager(Bootstrap 씬, 항상 로드되어 있음)가
// 결정한다. 이 덕분에 스테이지 씬을 통째로 복사/추가해도 이 스크립트는 손댈 필요가
// 없다 (이식성).
public class StageManagerNGO : MonoBehaviour
{
    [Header("연결")]
    public GoalZoneNGO goalZone;

    private void OnEnable()
    {
        if (goalZone != null)
        {
            goalZone.stageCleared.OnValueChanged += HandleStageCleared;
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
        if (GameFlowManager.Instance == null)
        {
            Debug.LogWarning("[StageManager] GameFlowManager.Instance가 없어 클리어를 알리지 못했습니다.");
            return;
        }
        GameFlowManager.Instance.NotifyStageCleared();
    }
}
