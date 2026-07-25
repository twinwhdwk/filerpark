# Stage 3 — 열쇠 릴레이 (Key Relay)

**테마**: 운반 & 균형 · **인원**: 4~6명 · **구현 상태**: 신규 컴포넌트 필요 — `CarryableKeyNGO`, `SeesawPlatformNGO`.

## 개요

공유 열쇠 하나가 있어야 문을 열 수 있는데, 열쇠를 든 사람은 건널 수 없는 좁은 틈이 중간에 있다. 열쇠 없는 사람이 먼저 틈을 건너간 뒤, 열쇠 든 사람이 틈 앞에서 열쇠를 놓으면(또는 던지면) 건너간 사람이 주워서 마저 운반한다. 그 다음 구간은 양쪽 무게가 맞아야 기울지 않는 시소 다리 — 인원을 양쪽에 적절히 나눠 서야 통과할 수 있다.

## 신규 컴포넌트

### `CarryableKeyNGO`
```csharp
public class CarryableKeyNGO : NetworkBehaviour
{
    private readonly NetworkVariable<ulong> carrierClientId = new(...WritePermission.Server); // 0 = 아무도 안 듦(NGO의 서버 clientId 0과 겹치지 않게 별도 sentinel 필요 -- ulong.MaxValue 등)
    // 서버가 검증하는 ServerRpc: 플레이어가 열쇠 트리거 반경 안에 있을 때만 Pickup 허용
    [ServerRpc(RequireOwnership = false)] public void RequestPickupServerRpc(ServerRpcParams p) { ... }
    [ServerRpc(RequireOwnership = false)] public void RequestDropServerRpc(ServerRpcParams p) { ... }
    // 들고 있는 동안 FixedUpdate에서 캐리어의 손 위치로 transform을 따라가게 함 (서버 계산, NetworkTransform으로 전파)
}
```
`CoopDoorNGO`는 `requiredButtons` 대신(또는 추가로) "carrierClientId가 유효하고 문 앞 트리거 안에 있는가"를 여는 조건에 추가하는 변형이 필요 — 기존 door 스크립트를 상속/확장하거나 별도 `KeyDoorNGO`로 분리.

### `SeesawPlatformNGO`
```csharp
public class SeesawPlatformNGO : NetworkBehaviour
{
    public Transform leftSide, rightSide;
    public float tiltThreshold = 1.5f; // 이 차이 이상 인원 불균형이면 기울어짐

    // 서버: leftSide/rightSide 트리거 안 Player 콜라이더 수를 세고,
    // 차이가 임계값을 넘으면 플랫폼 Rigidbody2D를 회전시켜 무거운 쪽이 내려가게 함.
    // 차이가 임계값 이하면 수평 유지 -> 건널 수 있음.
}
```

## 인원수 스케일링

- 열쇠 릴레이 구간의 "틈" 폭은 고정(인원수와 무관) — 이 구간은 순서/타이밍 문제라 인원이 늘어도 난이도가 크게 안 변함.
- 시소는 인원이 많을수록 왼쪽/오른쪽 배분 조합이 다양해져 자연히 쉬워지므로, `tiltThreshold`를 `Mathf.Max(1f, 접속 인원수 * 0.3f)`처럼 인원수에 비례해 살짝 낮춰 상대적 난이도를 유지한다(인원이 많다고 너무 쉬워지지 않게).

## 레벨 레이아웃 (개념도)

```
[스폰]  [열쇠]   [틈]   [문(열쇠 필요)]   [시소 다리]   [골존]
          │        └ 열쇠 든 채로는
          │          점프 실패하도록
          │          이동속도 페널티
          └ 최초 위치, 아무나 주울 수 있음
```

## 진행 흐름

1. 한 명이 열쇠를 줍는다 (`RequestPickupServerRpc`) — 든 상태에서는 이동속도/점프력 페널티(무거운 물건이라는 설정)로 틈을 못 건넘.
2. 열쇠 없는 인원이 먼저 틈을 건넌다.
3. 열쇠 든 사람이 틈 앞에서 `RequestDropServerRpc` — 반대편 사람이 주움.
4. 열쇠 들고 문 앞으로 이동 -> 문 열림 -> 전원 통과.
5. 시소 다리에서 인원을 양쪽에 나눠 서서 수평 유지하며 건넘.
6. 골존 도달.

## 승리/실패 조건

- 성공: 전원 골존 도달.
- 실패 조건 없음. 단, 열쇠를 떨어뜨릴 수 없는 위치(틈 사이)에 갇히는 소프트 락 가능성이 있어 — 열쇠에 "일정 시간 방치되면 자동으로 가장 가까운 안전지대로 리셋" 안전장치를 넣는 걸 권장 (설계상 필요, 구현 시 반영).

## 봇 자동 클리어 로직

공통 아키텍처: [bot-coordination.md](bot-coordination.md). 이 스테이지가 4개 중 **자동화 신뢰도가 가장 낮다** — 아래 배정표는 "이상적인 경우"이고, 실질적으로는 사람 검증 비중이 크다.

| 역할 번호 | BotMode | 대상 | 인원 |
|---|---|---|---|
| 0 | `CarryRelay` (구간 A: 스폰 -> 틈 앞) | `KeyPickup` -> `KeyHandoff-A` | 1명 |
| 1 | `CarryRelay` (구간 B: 틈 건너편 -> 문) | `KeyHandoff-A` -> `KeyHandoff-B`(문 앞) | 1명 |
| 2 ~ N-1 | `HoldButton`류를 시소 좌/우 존에 응용 (`SeesawLeft`/`SeesawRight` 태그) | 좌우 인원 균형 맞춰 서기 | 나머지 |

- `CarryRelay`는 정확한 좌표(웨이포인트)에서 pickup/drop 타이밍이 맞아야 하는데, 봇의 이동은 `PlayerMovementNGO`의 물리 기반이라 정지 위치에 프레임 단위 오차가 있다 — **틈 폭에 여유(픽업/드롭 판정 반경을 넉넉하게)를 둬야 봇 릴레이가 실패하지 않는다.** 이건 봇 검증을 위한 타협이 아니라, 애초에 사람이 해도 프레임 퍼펙트를 요구하면 안 되는 디자인이라 방향은 같다.
- 시소 좌우 배분은 `assignedRole`을 절반씩 `SeesawLeft`/`SeesawRight`로 고정 배정하면 되므로 이 부분은 자동 검증 신뢰도가 나쁘지 않음.
- **사람 플레이테스트 필수 영역**: 열쇠 든 채 이동속도 페널티를 받는 상태에서 정확히 틈 앞까지 가서 드롭하는 전체 릴레이 시퀀스. 실패 시 소프트 락(열쇠가 아무도 못 줍는 곳에 떨어짐) 여부는 사람이 일부러 이상한 지점에서 드롭해보며 확인해야 한다.
