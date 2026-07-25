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
public class PushableBlockNGO : NetworkBehaviour
{
    [Header("설정")]
    public int requiredPushers = 3;
    public Transform targetPoint;
    public float arriveThreshold = 0.3f;
    public float pushSpeed = 2f;

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
        if (!IsServer) return;
        int connected = NetworkManager.Singleton.ConnectedClientsIds.Count;
        requiredPushers = Mathf.Max(1, Mathf.CeilToInt(connected * 0.6f));
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

        if (overlapping.Count >= requiredPushers)
        {
            transform.position += new Vector3(pushDir * pushSpeed * Time.fixedDeltaTime, 0f, 0f);
        }
    }
}
