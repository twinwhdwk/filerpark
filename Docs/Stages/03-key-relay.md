# Stage 3 — 열쇠 릴레이 (Key Relay)

**테마**: 운반 & 균형 · **인원**: 4~6명 · **구현 상태**: 구현 완료 (`Assets/Scenes/Stages/Stage03_KeyRelay.unity`), Stage 1-2보다 봇 검증이 덜 됨 (아래 참고).

## 개요 (원안과 실제 구현의 차이)

원안은 "틈을 못 건너는 캐리어가 열쇠를 던지면 반대편 사람이 이어받는 다단계 릴레이"였다. 실제 구현은 **"틈/이동속도 페널티 없이, 순위 0번 봇이 열쇠를 처음부터 문까지 직접 운반"**으로 단순화됐다 — `bot-coordination.md`가 처음부터 "이 스테이지가 4개 중 자동화 신뢰도가 가장 낮다"고 명시했던 정밀 핸드오프 타이밍 문제를 아예 피한 것이다. "공유 자원(열쇠)을 가진 사람만 문을 열 수 있다"는 핵심과 "시소로 무게 균형 맞추기"는 원안 그대로 유지했다.

## 구현된 컴포넌트

### `CarryableKeyNGO`
```csharp
public class CarryableKeyNGO : NetworkBehaviour
{
    private const ulong NoCarrier = ulong.MaxValue; // sentinel -- clientId 0과 안 겹침
    public float pickupRadius = 1f;
    public Vector3 carryOffset = new(0f, 0.9f, 0f);

    public readonly NetworkVariable<ulong> carrierClientId = new(NoCarrier, ...WritePermission.Server);

    [ServerRpc(RequireOwnership = false)] public void RequestPickupServerRpc(ServerRpcParams p = default) { ... } // 거리 검증
    [ServerRpc(RequireOwnership = false)] public void RequestDropServerRpc(ServerRpcParams p = default) { ... }

    // FixedUpdate: 캐리어가 접속 해제되면(ConnectedClients에 없으면) carrierClientId를
    // 자동으로 NoCarrier로 되돌린다 -- 원안에 없던, 실제로 필요했던 방어 코드.
}
```
원안과 달리 **이동속도/점프력 페널티가 없다** — 열쇠를 들어도 평소처럼 움직인다.

### `KeyDoorNGO`
`CoopDoorNGO`(버튼 개수 기반)와 별도 스크립트로 분리했다 — "열쇠가 문 앞 범위(`openRange`) 안에 있는가"만으로 매 프레임 `isOpen`을 재계산하는 상태 비저장(stateless) 방식이라, 카운터 드리프트 걱정이 없다. `CoopDoorNGO`와 동일하게 `UITheme.ColorIce` + tintable 스프라이트를 쓴다.

### `SeesawPlatformNGO` (+ `SeesawSideZone`)
원안대로 구현됨: 좌/우 자식 오브젝트(`SeesawSideZone`)가 각자 겹친 Player 콜라이더 수를 부모에 보고하고, 차이가 `tiltThreshold`를 넘으면 `transform.rotation`을 `tiltAngle`까지 서서히 회전시킨다. 자식으로 분리한 이유: 부모 하나에 트리거 콜라이더 두 개를 붙이면 `OnTrigger` 콜백에서 어느 쪽이 반응했는지 구분할 수 없기 때문.

## 인원수 스케일링

원안의 "틈 폭 고정" 항목은 틈 자체가 없어져 해당 없음. 시소는 원안대로 인원이 많을수록 배분 조합이 다양해져 자연히 쉬워진다.

## 레벨 레이아웃 (실제 좌표)

```
[스폰 -6~6]  [열쇠 x=0]  [문 x=7]  [시소 구간 9~15, 안전망 y=-7]  [골존 x=20]
```

## 진행 흐름 (실제)

1. 순위 0번 봇(또는 가장 먼저 반응한 사람)이 열쇠를 줍는다.
2. 열쇠를 든 채로 문까지 직접 이동 (페널티 없음).
3. 문 앞 범위 안에 열쇠가 들어오면 `isOpen`이 자동으로 true.
4. 시소 구간에서 나머지 인원이 좌/우로 나뉘어 균형을 맞추며 통과.
5. 전원 골존 도달.

## 승리/실패 조건

- 성공: 전원 골존 도달.
- 실패 조건 없음. 시소 아래 안전망(y=-7)으로 소프트락은 방지했지만, 완전한 복귀 경로는 사람 플레이테스트로 확인이 더 필요한 영역으로 남아있다.

## 알려진 이슈 (실측)

열쇠 픽업 판정에서 클라이언트/서버 위치 동기화 지연이 실제로 문제가 됐다 — 캐리어 봇이 로컬에서 계산한 "충분히 가까움"(`pickupRadius * 0.8`) 판정은 통과하는데, `NetworkTransform` 보간으로 인해 실제로는 서버가 보는 위치가 약 0.2유닛 더 멀어서 서버의 `pickupRadius` 검증이 계속 거부하는 상황이 있었다(진단 로그로 `dist=1.19`가 반복 거부되는 걸 확인). `pickupRadius`를 늘려서 이 동기화 오차를 흡수하도록 조정해 해결했다.

## 봇 자동 클리어 로직

공통 아키텍처는 [bot-coordination.md](bot-coordination.md)를 원안으로 제안했으나, Stage 2와 마찬가지로 **서버 방송형 역할 배정 대신 `BotController.BotMode.StageAuto`의 로컬 자기판별**로 구현됐다.

- `UpdateStageAuto()`가 씬에서 `CarryableKeyNGO`를 찾으면 `UpdateKeyRelay()`로 분기.
- 순위 0번 봇만 열쇠를 운반(줍기 → 문으로 이동). 나머지는 문이 열리기 전까지 시소 좌/우(`myRank % 2`로 결정)에서 대기하다가, 문이 열리면 골로 이동.
- 원안의 2단계 릴레이(`KeyPickup`/`KeyHandoff-A/B` 태그, 봇 2마리 이상 필요)는 구현되지 않았다 — 단일 운반자 방식이라 릴레이 자체가 없다.
- **이 스테이지는 4개 중 봇 검증이 가장 덜 됐다** — Stage 1-2는 5봇 GCP 실서버 테스트로 반복 검증됐지만, Stage 3-4는 구현/배선까지만 완료되고 아직 end-to-end 봇 검증 기록이 없다(원 구현 커밋 메시지에 명시).
