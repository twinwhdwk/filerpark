# 봇 협업 아키텍처 (Stage Bot Coordination)

4개 스테이지 문서 모두가 참조하는 공통 설계. 목표는 "봇 N마리를 띄우고 호스트만 Start Host를 누르면, 사람 개입 없이 봇들이 역할을 나눠 스테이지를 자동으로 깬다" — 솔로 개발자가 기믹을 고칠 때마다 사람 파트너를 구하지 않고도 회귀 검증을 할 수 있어야 한다는 게 이 프로젝트의 봇 시뮬레이션 취지([[bot-simulation]], `CLAUDE.md`의 "Bot simulation" 절)를 스테이지 단위로 확장한 것.

기존 `BotController`(Patrol/FollowNearest)는 "그럴듯하게 돌아다니기"는 되지만 "이 버튼은 내가 맡을게" 같은 **역할 분담**이 없다. 이번 설계의 핵심은 그 역할 분담을 서버가 결정해서 각 봇에게 통보하는 것 — 봇끼리 합의(consensus)할 필요 없이, `CLAUDE.md`의 "Server-authoritative throughout" 원칙을 그대로 봇 AI에도 적용한다.

## 1. 봇이 자기 자신을 서버에 알린다

지금은 서버가 "이 클라이언트가 봇인지 사람인지" 알 방법이 없다 (`BotProcess.IsBot`은 그 프로세스 로컬에서만 보이는 값). 필요한 추가:

```csharp
// PlayerSetupNGO 또는 새 BotIdentityNGO에 추가
private readonly NetworkVariable<bool> isBot = new NetworkVariable<bool>(
    false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

public override void OnNetworkSpawn()
{
    if (IsOwner) isBot.Value = BotProcess.IsBot;
}
```

서버(및 다른 클라이언트)는 이제 `GameObject.FindGameObjectsWithTag("Player")`로 얻은 목록에서 `GetComponent<BotIdentityNGO>().IsBot`로 봇/사람을 구분할 수 있다.

## 2. 서버가 역할을 배정한다 — `StageBotDirectorNGO`

`StageManagerNGO`에 붙는 신규 서버 전용 컴포넌트. 스테이지 시작 시(`GoalZoneNGO`가 리셋되거나 씬 로드 직후) 한 번:

1. `IsBot == true`인 플레이어를 모아 `OwnerClientId` 오름차순으로 정렬한다 — **정렬 기준이 고정**이어야 매 실행마다 같은 봇이 같은 역할을 맡아서 재현 가능한(deterministic) 테스트가 된다. 랜덤 배정은 "이번엔 우연히 깼는데 다음엔 실패"를 만들어서 회귀 테스트 신뢰도를 떨어뜨린다.
2. 현재 `StageDefinition`(각 스테이지 설계 문서에 있는 `역할 배정표`)에서 접속 인원수에 맞는 역할 목록을 가져온다.
3. 정렬된 봇 리스트에 역할을 순서대로 1:1 배정하고, 각 봇의 `BotController`에 있는 `NetworkVariable<int> assignedRole`(WritePermission.Server)에 써준다.
4. `BotController`는 `assignedRole.OnValueChanged`로 이 값을 받아서 그 역할에 맞는 `BotMode`와 목표 오브젝트(태그로 탐색)를 스스로 세팅한다.

사람 플레이어가 섞여 있으면(봇 3 + 사람 1처럼) 역할 목록에서 사람 몫은 그냥 비워두고 사람이 알아서 채우면 된다 — 자동검증은 "전원 봇"일 때만 의미가 있고, 혼합 플레이는 애초에 사람이 지켜보는 세션이라 문제없다.

## 3. `BotController`에 추가해야 하는 모드

기존 `BotMode.Patrol` / `BotMode.FollowNearest`에 다음을 추가한다. 전부 "태그로 목표를 찾고, 그 목표에 대해 정해진 행동을 한다"는 동일한 패턴이라 `FollowNearest`가 이미 쓰는 `GameObject.FindGameObjectsWithTag` 관례를 그대로 확장한다.

| BotMode | 목표를 찾는 방법 | 행동 |
|---|---|---|
| `HoldButton` | 태그 `StageButton` 오브젝트 중 `assignedTargetIndex`번째 | 그 위치로 이동, 도달하면 `HorizontalInput = 0`으로 멈춰서 버튼 위에 계속 서 있음 |
| `PushTarget` | 태그 `PushTargetZone` (블록이 도달해야 할 위치) | `PushableBlockNGO`의 현재 위치를 읽어, 블록 기준 목표 반대편으로 이동한 뒤 블록 방향으로 계속 `HorizontalInput` 입력(=미는 힘 보탬). 블록이 목표 존에 들어가면(`PushableBlockNGO.isInPlace`) 종료 |
| `CarryRelay` | 태그 `KeyPickup`/`KeyHandoff` 웨이포인트 배열(`assignedWaypointIndex`로 자기 구간만) | 웨이포인트 시작점 이동 -> `CarryableKeyNGO.RequestPickupServerRpc()` -> 끝점 이동 -> `RequestDropServerRpc()` |
| `GroupAdvance` | 태그 `Player` 전체 | 자기 진행도(x좌표)가 **가장 뒤처진 팀원보다 앞서지 않도록** 속도 제한 -- `HorizontalInput`을 팀 전체의 최소 진행도에 맞춰 조절. 혼자 질주해서 대열이 끊기는 걸 방지 |
| `GoToGoal` | `GoalZoneNGO` (씬에 하나) | 목표 위치로 직진, 도달 후 정지 |

`assignedRole`은 `(BotMode 모드, int 파라미터)`를 함께 인코딩하는 간단한 정수 열거(예: 역할 0~2 = HoldButton 0~2번, 역할 3 = Runner/GoToGoal)로 두고, 어떤 정수가 어떤 역할인지는 `StageDefinition`이 정의한다.

## 4. 자동 판정 & 로그

`StageManagerNGO`가 스테이지 성공/실패를 이미 판정하므로(골존 도달), 여기에 봇 자동검증 전용 로그 한 줄만 추가하면 사람이 나중에 배치 로그를 grep해서 결과를 확인할 수 있다:

```
Debug.Log($"[StageValidation] stage={stageId} result=success elapsedSeconds={elapsed:F1} botCount={botCount}");
```

`-batchmode` 헤드리스 실행 + 이 로그 포맷을 고정해두면, `CLAUDE.md`에 이미 있는 헤드리스 컴파일 체크와 같은 방식으로 "봇 4마리 띄우고 90초 안에 success 로그가 뜨는지" 같은 형태의 회귀 검증을 나중에 스크립트화할 수 있다 (지금 당장 구현 범위는 아니고, 로그 포맷만 지금 정해서 나중에 걸림돌이 없게 해두는 것).

## 5. 봇으로 검증 가능한 것 / 안 되는 것

역할 배정 + 위 모드들로 자동 검증되는 건 "정해진 스크립트대로 움직이면 풀리는 퍼즐"이다. 반대로 다음은 봇으로는 신뢰도 있게 검증되지 않는다 — 각 스테이지 문서에 해당 시 명시:

- **타이밍 재량이 필요한 조작** (예: 정확한 순간에 점프해서 다른 플레이어 위에 착지) — `PlayerMovementNGO`의 점프는 `BotController`의 랜덤 타이머로만 트리거되므로, 정밀 타이밍이 필요한 스택은 우연에 의존한다.
- **동적으로 바뀌는 목표 판단** (예: "지금 이 순간 어느 쪽 시소가 가벼운지 보고 반대편으로 이동") — `HoldButton`/`PushTarget`처럼 목표가 고정된 태그가 아니라 실시간 판단이 필요하면 별도 로직이 필요하고, 정확도가 떨어질 수 있다.
- **사람 플레이어와 섞인 상황** — 역할 배정은 "전원 봇"을 전제로 하므로, 혼합 플레이에서는 자동검증이 아니라 그냥 사람 보조용 협력자로만 동작한다.

이런 지점은 각 스테이지 문서의 "봇 자동 클리어 로직" 절 마지막에 "사람 플레이테스트 필요" 항목으로 명시한다.
