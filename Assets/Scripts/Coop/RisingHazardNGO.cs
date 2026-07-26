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
    }
}
