using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// 문지기(CoopButtonNGO)의 "인원수 게이팅"을 이동 가능한 물체에 적용한 버전.
// 트리거로 겹친 Player 수를 세는 방식은 CoopButtonNGO와 동일하다. 애초에는 "실제로
// 목표 방향으로 이동 중인(Rigidbody2D 속도 부호 일치) 플레이어만 센다"는 더 엄격한
// 판정을 시도했으나, FixedUpdate 스크립트 실행 순서와 물리 솔버의 충돌 응답 타이밍에
// 좌우되는 값이라 봇 4~6마리 테스트에서 신뢰도 있게 동작하지 않았다 -- 이 프로젝트에서
// 이미 검증된 CoopButtonNGO의 단순 겹침 카운트 패턴으로 되돌렸다. "그냥 서 있기만
// 해도 카운트된다"는 설계 문서의 우려보다, 애초에 밀어야 할 방향의 좁은 트리거
// 반경 안에 그만큼 서려면 실질적으로 밀고 있을 수밖에 없다는 레벨 지오메트리로
// 대신 보완한다.
// 5봇 테스트로 실측한 근본 원인: 이 블록엔 Rigidbody2D가 없어서 Unity 2D 물리
// 엔진이 이 콜라이더를 "정적(static)"으로 취급한다. 그런데 FixedUpdate에서 매 프레임
// transform.position을 직접 바꿔 움직이면, 정적 콜라이더는 원래 안 움직인다고
// 가정하는 물리 엔진의 충돌 해석이 어긋나 -- 실제로 밀던 인원이 3명을 채워 블록이
// 16.00 -> 20.68까지 움직이다가, 그 다음부터 overlap이 0으로 고정되고 다시는
// 회복되지 않는 걸 서버 로그로 확인했다(밀던 플레이어들이 튕겨/분리된 것으로 추정).
// Kinematic Rigidbody2D + MovePosition()으로 옮기면 물리 엔진이 이 이동을 제대로
// 추적해서 겹친 다이나믹 바디(플레이어)를 정상적으로 밀어낸다 -- Unity 공식 문서가
// 권장하는 "스크립트로 제어하는 무빙 플랫폼" 패턴.
[RequireComponent(typeof(Rigidbody2D))]
public class PushableBlockNGO : NetworkBehaviour
{
    [Header("설정")]
    public int requiredPushers = 3;
    public Transform targetPoint;
    // 한때 이 값을 1.0까지 넓혀봤지만 잘못된 진단이었다: 블록 폭(5)과 구덩이 폭(22~27,
    // 5유닛)이 정확히 일치하게 설계돼 있어서, 목표(24.5)에 못 미친 채로 "도착" 판정을
    // 내리면 그만큼 다리에 실제 틈이 생겨 봇이 구덩이로 떨어진다(관측: threshold=1.0
    // 배포 후 블록 도착 로그는 찍혔지만 그 뒤로 골 클리어가 영원히 안 남 -- 안전망
    // 위에 갇힌 것으로 추정). Kinematic Rigidbody2D + MovePosition() 수정 자체가 원래
    // 겪던 "overlap이 0으로 굳어 다시는 안 미는" 문제의 진짜 원인이었고, 그 수정만으로
    // 이미 5봇 테스트에서 블록이 중간에 멈추지 않고 끝까지(목표 도착 로그까지) 밀렸다 --
    // 그래서 이 값은 "밀기 실패"를 완충하는 용도가 아니라 그냥 부동소수점 오차 정도만
    // 흡수하면 된다.
    public float arriveThreshold = 0.15f;
    public float pushSpeed = 2f;

    private Rigidbody2D rb;

    // BotController가 "미는 자리"로 삼는, 중심에서 미는 반대 방향으로 떨어진 거리.
    // 블록 절반 폭보다 반드시 커야 한다 -- 그보다 작으면 봇이 목표 지점(블록 몸통
    // 안쪽)으로 걸어가려다 솔리드 콜라이더에 막혀, 트리거 겹침이 애매한 경계에서만
    // 걸리는 상태가 된다(실제로 겪은 버그: 절반 폭 2.5인 블록에 오프셋 1을 써서 봇이
    // 항상 블록 안쪽으로 걸어 들어가려다 막혔다).
    public float pushStandoffDistance = 3.5f;

    public readonly NetworkVariable<bool> isInPlace = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly HashSet<Collider2D> overlapping = new HashSet<Collider2D>();

    // 인원수 스케일링: 접속 인원의 60% 이상 필요 (4명->3명, 6명->4명). BotController의
    // UpdatePushTarget()도 동일한 공식으로 "미는 팀" 순위를 정하므로 둘이 항상 일치한다.
    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        isInPlace.OnValueChanged += OnArrivedChanged;

        if (!IsServer) return;
        // ConnectedClientsIds 대신 PlayerSetupNGO.ActivePlayers를 쓴다 -- 인원 부족 시
        // 서버가 직접 스폰하는 채움 봇은 실제 네트워크 접속이 아니라 ConnectedClientsIds에
        // 안 잡히므로, 그 기준으로는 채움 봇 인원만큼 요구치가 부풀려져(실제로는 이미
        // 채워진 인원인데도) 게이팅이 영영 안 풀릴 수 있다.
        int connected = PlayerSetupNGO.ActivePlayers.Count;
        requiredPushers = Mathf.Max(1, Mathf.CeilToInt(connected * 0.6f));
    }

    public override void OnNetworkDespawn()
    {
        isInPlace.OnValueChanged -= OnArrivedChanged;
    }

    private void OnArrivedChanged(bool previousValue, bool newValue)
    {
        if (newValue) AudioManager.Instance?.PlaySfx(SfxId.BlockArrive);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsServer) return;
        if (collision.CompareTag("Player")) overlapping.Add(collision);
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (!IsServer) return;
        overlapping.Remove(collision);
    }

    private void FixedUpdate()
    {
        if (!IsServer || isInPlace.Value || targetPoint == null) return;

        float dx = targetPoint.position.x - transform.position.x;
        if (Mathf.Abs(dx) <= arriveThreshold)
        {
            isInPlace.Value = true;
            Debug.Log($"[PushableBlock] {gameObject.name} 목표 도착");
            return;
        }

        float pushDir = Mathf.Sign(dx);
        overlapping.RemoveWhere(c => c == null);

        bool satisfied = overlapping.Count >= requiredPushers;
        if (satisfied)
        {
            rb.MovePosition(rb.position + new Vector2(pushDir * pushSpeed * Time.fixedDeltaTime, 0f));
        }

        // 요구 인원을 못 채운 채로 오래 멈춰 있는 상태를 서버 로그에서 바로 보이게 한다 --
        // 5봇 테스트에서 이 게이팅이 왜 안 풀리는지 진단하려고 매번 임시 로그를 추가했다
        // 제거하는 걸 반복했는데, 라이브 서버에서도 이 상태가 오래 지속되면 실제로
        // 운영상 알아야 할 정보이므로 상시 남겨둔다(만족 상태에선 조용함).
        if (!satisfied && Time.time >= nextStuckLogTime)
        {
            nextStuckLogTime = Time.time + 3f;
            Debug.Log($"[PushableBlock] {gameObject.name} 대기 중: overlap={overlapping.Count} required={requiredPushers} x={transform.position.x:F2}");
        }
    }

    private float nextStuckLogTime;
}
