# Stage 2 — 돌덩이 운반 (Block Carry)

**테마**: 스택 & 푸시 · **인원**: 4~6명 · **구현 상태**: 구현 완료 (`Assets/Scenes/Stages/Stage02_BlockCarry.unity`), 5봇 GCP 실서버 테스트로 검증됨.

## 개요 (원안과 실제 구현의 차이)

원안(아래 "원래 설계" 참고)은 "돌덩이를 밀어 발판을 만들고 그 위로 스택해서 높은 벽을 넘는다"였다. 실제 구현은 **"돌덩이를 밀어 구덩이 위 다리로 만든다"**로 단순화됐다 — 벽+스택 방식은 `BotController`의 점프가 랜덤 타이머라 다른 플레이어 위에 정밀하게 올라타는 걸 봇으로 신뢰도 있게 검증할 수 없기 때문이다(`bot-coordination.md` 5절이 이미 이 한계를 명시하고 있었다). "여럿이 함께 밀어야 하는 인원수 게이팅"이라는 핵심 협동 기믹은 원안 그대로 유지했다.

구덩이(약 22~27유닛 구간) 왼쪽에서 돌덩이가 시작해, 충분한 인원이 밀면 구덩이 중앙까지 이동해 다리가 되고, 그 위를 걸어서 반대편 골존까지 간다.

## 구현된 컴포넌트: `PushableBlockNGO`

```csharp
public class PushableBlockNGO : NetworkBehaviour
{
    public int requiredPushers = 3;      // OnNetworkSpawn에서 접속 인원의 60%로 재계산
    public Transform targetPoint;        // 도착해야 하는 지점 (BlockTarget 마커)
    public float arriveThreshold = 0.3f;
    public float pushSpeed = 2f;
    public float pushStandoffDistance = 3.5f; // 봇이 "미는 자리"로 삼는 오프셋

    public readonly NetworkVariable<bool> isInPlace = new(...WritePermission.Server);
    private readonly HashSet<Collider2D> overlapping = new();

    // OnTriggerEnter2D/Exit2D로 겹친 Player 콜라이더를 HashSet에 추가/제거한다
    // (CoopButtonNGO와 동일 패턴 -- OnTriggerStay 폴링이 아니다).
    // FixedUpdate: overlapping.Count >= requiredPushers일 때만
    // transform.position을 목표 방향으로 직접 이동시킨다 (Rigidbody2D 힘이 아니라
    // transform 직접 이동 -- NetworkTransform이 결과를 클라이언트에 전파).
}
```

- "밀고 있다"의 판정은 원안대로 물리 힘 기반이 아니라, **트리거 겹침 인원수**로만 판정한다("그냥 서 있기만 해도 카운트된다"는 원안의 우려는 실측으로는 문제가 안 됐다 — 미는 방향의 좁은 트리거 반경 안에 필요 인원만큼 서려면 레벨 지오메트리상 실질적으로 밀 수밖에 없는 위치이기 때문).
- 트리거는 솔리드 콜라이더보다 미는 반대편(왼쪽)으로 크게 잡아야 한다 — 처음엔 좌우 균등하게 잡아서 대기 공간이 좁아 요구 인원(3명)이 물리적으로 다 못 들어가고 2명에서 정체되는 버그를 5봇 테스트로 실측했다. `blockTrigger.offset`으로 중심을 왼쪽으로 밀고 폭을 넉넉히(11유닛) 잡아 해결.
- 블록의 목표 y좌표는 바닥 표면과 정확히 일치해야 한다 — 0.8유닛만 높아도 봇이 점프 없이는 못 넘는 턱이 되어 다리를 건너다 막힌다(랜덤 타이머 점프로는 이 턱을 넘지 못함).

## 인원수 스케일링

`requiredPushers = Mathf.Max(1, Mathf.CeilToInt(접속 인원수 * 0.6f))` — 서버가 `OnNetworkSpawn()`에서 계산 (4명 → 3명, 6명 → 4명). `BotController.UpdatePushTarget()`도 동일한 공식으로 "미는 팀" 인원수를 계산하므로, 서버가 역할을 방송하지 않아도 모든 봇이 같은 결론에 도달한다(아래 "봇 자동 클리어 로직" 참고).

## 레벨 레이아웃 (실제 좌표)

```
[스폰 -6~6]  [돌덩이 x=16]--민다--> [구덩이 22~27, 목표 x=24.5] [안전망 y=-8]   [골존 x=31]
```

- 구덩이 아래 y=-8에 안전망 바닥을 깔아둔다 — 다리를 건너던 봇이 서로 부대껴 밀려 떨어지는 경우를 5봇 테스트로 실측했고(안전망 없이는 Y좌표가 -294802까지 무한히 떨어짐), 떨어져도 복귀 가능하게 한다.

## 승리/실패 조건

- 성공: 전원 골존 도달.
- 실패 조건 없음.

## 봇 자동 클리어 로직

공통 아키텍처는 [bot-coordination.md](bot-coordination.md)를 원안으로 제안했으나, **실제로는 서버 방송형 역할 배정 대신 `BotController.BotMode.StageAuto`가 로컬에서 자기 판별**하는 더 단순한 방식으로 구현됐다 (아래 참고).

- `UpdateStageAuto()`가 씬에서 `PushableBlockNGO`를 찾으면 `UpdatePushTarget()`으로 분기.
- 순위(`GetMyRank()`, 접속한 Player를 `OwnerClientId` 오름차순 정렬한 내 순번)가 `requiredPushers`보다 작은 봇만 미는 팀 — 나머지는 블록이 `isInPlace`가 될 때까지 대기하다가 골로 이동.
- 미는 팀은 "계산된 대기 지점으로 이동"이 아니라 **그냥 미는 방향으로 계속 걷는다** — 각자 다른 좌표를 노리게 하면 서로 부딪혀 정체되는 문제를 실측해서, 넓은 트리거 범위 안에서 솔리드 콜라이더가 알아서 정지시키는 단순한 방식으로 바꿨다.
- **1단계(밀기)는 자동 검증 신뢰도가 높음** — 5봇 테스트로 반복 검증됨.
- **2단계(다리 건너기)는 여전히 가끔 낙오자가 발생** — 다리 위에서 봇끼리 부딪혀 밀려나는 경우가 남아있다(안전망으로 소프트락은 막았지만 "전원 동시 도달"이 매번 성공하진 않음, 후속 조정 필요로 표시됨).
- 원안의 `PushTargetZone`/`HoldButton` 태그 기반 배정, `[StageValidation]` 로그 포맷은 이번 구현에 없다 — 위 로컬 자기판별 방식이 그 역할을 대신한다.

## 원래 설계 (참고용, 구현되지 않음)

아래는 최초 설계 당시의 원안이다 — "벽+스택" 아이디어 자체는 여전히 다른 스테이지나 향후 확장에 참고할 가치가 있어 남겨둔다.

큰 돌덩이를 밀어 목표 지점에 도착시키면 그 자체가 발판이 되어, 그 위로 스택(플레이어끼리 올라타기)해서 원래는 못 넘던 높은 벽을 넘어 골존에 도달하는 구상이었다. 이 스택 구간은 정밀한 점프 타이밍이 필요해 봇 자동화로는 신뢰도 있게 검증할 수 없다는 게 처음부터 알려진 리스크였고, 실제로 그 이유로 구덩이-다리 방식으로 대체됐다.
