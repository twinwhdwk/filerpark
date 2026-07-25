# Stage 4 — 탈출 카운트다운 (Escape Countdown)

**테마**: 공동 생존 · **인원**: 4~6명 · **구현 상태**: 구현 완료 (`Assets/Scenes/Stages/Stage04_EscapeCountdown.unity`), 배선까지 끝났지만 Stage 1-2와 달리 아직 end-to-end 봇 검증 기록은 없음(원 구현 커밋 메시지에 명시).

## 개요

원안은 "용암 또는 벽" 둘 다 동등하게 제시했는데, **실제로는 벽 쪽으로 구현**됐다 — 수평으로만 전진하는 벽은 "누가 뒤처졌는지"가 그대로 "누가 벽에 가장 먼저 닿는지"가 되어, "제일 느린 사람에게 팀 전체가 맞춰야 한다"는 핵심을 지형에 단차를 만들 필요 없이 직접 구현하기 때문(`RisingHazardNGO.cs` 자체 주석 참고). `riseSpeed` 필드명은 원안 그대로 유지하되 X축 전진 속도로 해석한다. 전원이 발맞춰 골까지 가야 하며, 누군가 뒤처져 벽에 닿으면 스테이지 실패로 전원 재도전 — 다른 3개 스테이지와 달리 **명시적인 실패 조건과 타임 프레셔가 있는 유일한 스테이지**.

## 신규 컴포넌트

### `RisingHazardNGO` (실제 구현)
```csharp
public class RisingHazardNGO : NetworkBehaviour
{
    public float riseSpeed = 0.3f;
    public float startDelay = 3f;
    public StageManagerNGO stageManager; // 씬 생성 스크립트가 서로 양방향으로 연결

    // FixedUpdate (서버): startDelay 경과 후 transform.position을 +X로 riseSpeed만큼
    // 이동, NetworkTransform으로 전파. OnTriggerEnter2D가 Player와 닿으면
    // stageManager.NotifyFailure() 호출.
    public void ResetHazard(Vector3 spawnPosition) { ... } // 실패 시 위치 초기화
}
```
경고색(빨강/주황, 알파 0.85)은 UI Style Guide 팔레트 범위 밖의 의도적 예외 — "닿으면 죽는다"는 신호는 브랜드 그린/블루로는 전달되지 않는다는 판단.

### `StageManagerNGO` 확장 (실제 구현)
원안대로 `hasFailCondition`(기본 false) + `hazard` 필드 쌍을 추가했다. `NotifyFailure()`가 호출되면 접속한 전원을 `PlayerSetupNGO.MoveToSpawnPoint()`로 스폰 지점에 되돌리고 `hazard.ResetHazard()`로 벽 위치를 초기화한다 — `GameFlowManager`의 점수/로비 상태는 전혀 건드리지 않는다(스테이지 내부 재도전은 로비 레벨 이벤트가 아니라는 설계 그대로).

## 인원수 스케일링

- `riseSpeed`는 인원수와 무관하게 고정 — 대신 **맵 길이**를 인원수에 비례해 늘린다(인원이 많을수록 대열이 길어지고 흩어지기 쉬우므로, 같은 상승 속도라도 체감 난이도가 자연히 올라간다. 별도 파라미터 튜닝 없이 레벨 지오메트리만으로 스케일링).

## 레벨 레이아웃 (실제 좌표)

```
[벽 시작 x=-9] ══ [스폰 -6~6] ══════ 골 방향 ══════ [골존 x=25]
  벽이 startDelay(3초) 후부터 +X로 riseSpeed만큼 전진, 스폰보다 뒤에서 시작해
  대형을 갖출 여유를 준다.
```

- 단순 직선 통로(폭 40유닛) — 원안의 "중간중간 살짝 높은 턱" 지형 디테일은 이번 구현에 없다.

## 진행 흐름

1. 전원 스폰. `startDelay`(3초) 동안 대형을 갖출 시간.
2. 용암 상승 시작. 전원 골 방향으로 이동.
3. 낙오자가 용암에 닿으면 즉시 실패 -> 전원 리셋.
4. 전원 골존 도달 시 성공.

## 승리/실패 조건

- 성공: 전원 골존 도달.
- 실패: 1명이라도 `RisingHazardNGO`에 접촉 -> 즉시 전원 실패, 리셋.

## 봇 자동 클리어 로직 (실제 구현)

이 스테이지는 원안이 상정한 "역할 배정"이 애초에 필요 없는 유일한 케이스라, [bot-coordination.md](bot-coordination.md)가 제안한 서버 방송형 배정과 실제로 쓰인 `BotController.BotMode.StageAuto`(로컬 자기판별) 사이의 차이가 가장 적게 드러난다 — 둘 중 어느 쪽으로 구현했어도 결과는 같았을 것이다.

- `UpdateStageAuto()`가 씬에서 `RisingHazardNGO`를 찾으면(다른 어떤 기믹보다 먼저 검사) `UpdateEscape()`로 분기 — 전원 동일 로직.
- 매 프레임 전원의 x좌표로 팀 최소 진행도(`teamMinX`)를 구하고, `myLead = 내 x − teamMinX`가 `allowedLead`(2.5유닛)를 넘으면 전진을 멈춰 낙오자를 기다린다("제일 느린 사람에게 맞춘다").
- **핵심 수정**: 골에 안전히 들어오면(`내 x ≥ 골중심 − 2`) 멈춰서 대기한다. 초기 구현은 목표 없이 계속 오른쪽으로만 걸어서 골존을 지나쳐 맵 오른쪽 끝(안전망 없음)에서 낭떠러지로 떨어져 영구 소프트락에 빠졌고, 클리어는 봇 무리가 골을 통과하는 찰나에 우연히 될 뿐이었다. 이제 전원이 골 안에서 멈춰 뭉치므로 "전원 동시 도달"이 안정적으로 성립한다.
- **아직 end-to-end 봇 검증 기록은 없다** — Stage 1-2처럼 5봇 GCP 실서버 테스트로 반복 확인된 상태는 아니고, 코드 재설계 + 컴파일 검증까지 완료된 상태다.
- **사람 플레이테스트가 필요한 부분**: "정말 재밌는 압박감을 주는 `riseSpeed` 값"은 봇으로는 알 수 없다 — 봇은 항상 동일한 로직으로 반응하므로 체감 긴장감/재미는 결국 사람이 판단해야 한다.
