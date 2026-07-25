# Stage 2 — 돌덩이 운반 (Block Carry)

**테마**: 스택 & 푸시 · **인원**: 4~6명 · **구현 상태**: 신규 컴포넌트 필요 — `PushableBlockNGO`.

## 개요

큰 돌덩이가 길을 막고 있다. 돌덩이는 일정 인원 이상이 동시에 밀어야만 움직인다(가벼우면 1명이 밀어도 계속 움직여서 협동의 의미가 없고, 너무 무거우면 인원수를 다 채워도 안 움직여 막힌다 — "필요 인원수"를 정확히 게이팅하는 게 이 스테이지의 핵심). 돌덩이를 목표 지점까지 밀어 넣으면 그 자체가 발판이 되어, 그 위로 스택해서 원래는 못 넘던 높은 벽을 넘어 골존에 도달한다.

## 신규 컴포넌트: `PushableBlockNGO`

```csharp
public class PushableBlockNGO : NetworkBehaviour
{
    public int requiredPushers = 3;   // 동시에 밀고 있어야 하는 최소 인원
    public Vector3 targetPosition;    // 도착해야 하는 존
    public float arriveThreshold = 0.3f;

    private readonly NetworkVariable<bool> isInPlace = new(...WritePermission.Server);
    private int currentPushers; // OnTriggerStay로 카운트하는 "미는 중"인 콜라이더 수 (CoopButtonNGO와 동일 패턴)

    // FixedUpdate (서버 전용): currentPushers >= requiredPushers 일 때만
    // Rigidbody2D에 밀리는 방향으로 힘을 허용. 그 외에는 위치 고정(kinematic 취급).
}
```

- 감지 방식은 `CoopButtonNGO`와 동일하게 트리거 콜라이더 + `OnTriggerStay2D`/`OnTriggerExit2D`로 "현재 밀고 있는 인원"을 센다 (버튼이 "밟은 인원 수"를 세는 것과 동일한 패턴 재사용).
- "밀고 있다"의 판정: 플레이어가 블록의 특정 면 트리거 안에 있고 + 그 방향으로 `horizontalInput`이 걸려 있어야 함 — 그냥 근처에 서 있기만 해도 카운트되면 "협동"이 아니라 "인원수만 채우면 됨"이 되어버려서 긴장감이 없다.

## 인원수 스케일링

`requiredPushers = Mathf.CeilToInt(접속 인원수 * 0.6f)` 정도로 서버가 스폰 시 계산 (4명 → 3명 필요, 6명 → 4명 필요) — 항상 "전원이 밀 필요는 없지만 과반 이상은 필요"한 긴장감을 유지.

## 레벨 레이아웃 (개념도)

```
[스폰]   [돌덩이]--민다--> [목표 존] [높은 벽]   [골존]
                                    └─ 벽 높이는 플레이어
                                       1명 키로는 못 넘고
                                       돌덩이+스택으로만 도달
```

- 1단계(가로 이동): 돌덩이를 오른쪽 목표 존까지 미는 구간.
- 2단계(수직 이동): 목표 존에 도착한 돌덩이가 발판이 되어, 그 위로 스택(플레이어끼리 올라타기)해서 벽을 넘는 구간.

## 승리/실패 조건

- 성공: 전원 골존(벽 너머) 도달.
- 실패 조건 없음 — 돌덩이가 잘못된 방향으로 밀려도(막다른 곳 등) 다시 반대로 밀면 되므로 소프트 락은 나지 않게 목표 존 앞뒤에 여유 공간을 둔다.

## 봇 자동 클리어 로직

공통 아키텍처: [bot-coordination.md](bot-coordination.md).

| 역할 번호 | BotMode | 대상 | 인원 |
|---|---|---|---|
| 0 ~ requiredPushers-1 | `PushTarget` | `PushTargetZone` 태그 (돌덩이 목표 존) | `requiredPushers`명 |
| 나머지 | `HoldButton`류 대기 후 `GoToGoal` | 돌덩이가 `isInPlace == true`가 될 때까지 대기, 이후 이동 | 나머지 인원 |

- **1단계(밀기)는 자동 검증 가능** — `PushTarget` 모드로 지정된 봇 수가 `requiredPushers`와 일치하도록 스테이지 시작 시 서버가 역할을 배정하면, 매번 동일한 조건으로 재현 가능하게 블록이 밀린다.
- **2단계(스택으로 벽 넘기)는 봇으로 신뢰도 있게 검증되지 않는다** — [bot-coordination.md](bot-coordination.md) 5절에서 명시한 "정밀 타이밍 필요한 조작"의 대표 사례. `BotController`의 점프는 랜덤 타이머라 다른 봇 위에 정확히 올라타는 걸 보장 못 한다.
  - **완화 방안(로드맵)**: 2단계만 따로, 스택 성공 여부와 무관하게 "블록이 목표 존에 도착했는가"까지만 자동검증 대상으로 삼고 `[StageValidation]` 로그를 그 시점에 남긴다. 벽 넘기 이후 골존 도달까지는 사람 플레이테스트로 커버.
  - 더 정교하게 하려면 `BotController`에 "내 앞의 다른 봇 콜라이더 위에 겹쳐 있으면 점프" 같은 근접 트리거 기반 점프 로직을 추가할 수 있지만, 이건 이번 설계 범위 밖 — 필요해지면 별도 `BotMode.ClimbStack`으로 확장한다.
