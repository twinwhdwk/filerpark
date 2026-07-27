using UnityEngine;
using Unity.Netcode;

// 설계 문서(04-escape-countdown.md)는 "바닥에서 차오르는 용암 또는 좌측에서 다가오는
// 벽" 둘 다를 동등한 표현으로 제시한다. 이 구현은 벽 쪽을 택했다 -- 수평으로만
// 전진하는 벽은 "누가 뒤처졌는지"가 그대로 "누가 벽에 가장 먼저 닿는지"가 되어
// "제일 느린 사람에게 팀 전체가 맞춰야 한다"는 핵심을 지형에 단차를 만들 필요 없이
// 직접적으로 구현한다(수직 상승이면 한 줄로 동시에 잠기는 지형을 따로 설계해야 함).
// riseSpeed 필드명은 문서의 API를 그대로 따르되 X축 전진 속도로 해석한다.
public class RisingHazardNGO : NetworkBehaviour
{
    public float riseSpeed = 0.3f;
    public float startDelay = 3f;
    public StageManagerNGO stageManager;

    private float stageStartTime;

    // 낙오한 스트래글러들이 무리 지어 한꺼번에 벽에 닿는 게 이 스테이지의 의도된
    // 시나리오다(파일 헤더 주석 참고) -- OnTriggerEnter2D는 겹친 콜라이더 수만큼
    // 매번 따로 불리므로, 이 가드가 없으면 같은 실패 이벤트에 대해
    // PlayFailSfxClientRpc가 N번 브로드캐스트되어 모든 클라이언트에서 같은 프레임에
    // 효과음이 겹쳐 재생된다. ResetHazard()가 다시 false로 돌려 다음 시도에 대비한다.
    private bool failureAlreadyTriggered;

    public override void OnNetworkSpawn()
    {
        stageStartTime = Time.time;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (Time.time - stageStartTime < startDelay) return;

        transform.position += new Vector3(riseSpeed * Time.fixedDeltaTime, 0f, 0f);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsServer) return;
        if (!collision.CompareTag("Player")) return;
        if (failureAlreadyTriggered) return;
        failureAlreadyTriggered = true;

        Debug.Log($"[RisingHazard] {collision.name} 낙오 -- 스테이지 실패");
        if (stageManager != null) stageManager.NotifyFailure();
        PlayFailSfxClientRpc();
    }

    // 실패 판정은 서버만 하는 트리거 이벤트라 NetworkVariable로 자연히 드러나지
    // 않는다 -- PlayerMovementNGO의 점프 SFX와 동일한 이유로 ClientRpc를 쓴다.
    [ClientRpc]
    private void PlayFailSfxClientRpc()
    {
        AudioManager.Instance?.PlaySfx(SfxId.StageFail);
    }

    public void ResetHazard(Vector3 spawnPosition)
    {
        if (!IsServer) return;
        transform.position = spawnPosition;
        stageStartTime = Time.time;
        failureAlreadyTriggered = false;
    }
}
