using UnityEngine;
using Unity.Netcode;

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

    [Header("실패 조건 (선택 -- 탈출 카운트다운처럼 낙오가 있는 스테이지만 사용)")]
    public bool hasFailCondition;
    public RisingHazardNGO hazard;
    private Vector3 hazardSpawnPosition;

    private void Awake()
    {
        if (hazard != null) hazardSpawnPosition = hazard.transform.position;
        AudioManager.Instance?.PlayStageMusic();
    }

    // RisingHazardNGO가 낙오를 감지하면 호출한다. 전원 스폰 지점으로 리셋 + 해저드
    // 위치 초기화만 하고 GameFlowManager에는 알리지 않는다 -- "재도전"은 이 스테이지
    // 안에서만 벌어지는 로컬 이벤트라 로비 복귀/스코어와 무관하다. 실패 조건이 없는
    // 다른 3개 스테이지는 hasFailCondition이 꺼져 있어 이 메서드가 아무 일도 안 한다.
    public void NotifyFailure()
    {
        if (!hasFailCondition) return;

        Debug.Log("[StageManager] 낙오 발생 -- 전원 스폰 지점으로 리셋");

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) continue;
            if (client.PlayerObject == null) continue;
            PlayerSetupNGO setup = client.PlayerObject.GetComponent<PlayerSetupNGO>();
            if (setup != null) setup.MoveToSpawnPoint();
        }

        if (hazard != null) hazard.ResetHazard(hazardSpawnPosition);
    }

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
