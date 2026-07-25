# Stage Design Docs

4~6인 협동 플레이를 기준으로 설계한 4개 스테이지(미션)의 상세 설계문서. 각 스테이지는 Pico Park 시리즈에서 반복되는 협동 기믹 축(테마) 하나씩을 중심으로 짜여 있다.

| # | 스테이지 | 테마 | 상태 |
|---|---|---|---|
| 1 | [문지기 (Gatekeeper)](01-gatekeeper.md) | 동시 입력 | 구현 완료, 5봇 GCP 실서버 테스트로 검증됨 |
| 2 | [돌덩이 운반 (Block Carry)](02-block-carry.md) | 스택 & 푸시 | 구현 완료(원안에서 단순화됨), 5봇 GCP 실서버 테스트로 검증됨 |
| 3 | [열쇠 릴레이 (Key Relay)](03-key-relay.md) | 운반 & 균형 | 구현 완료(원안에서 단순화됨), end-to-end 봇 검증은 아직 |
| 4 | [탈출 카운트다운 (Escape Countdown)](04-escape-countdown.md) | 공동 생존 | 구현 완료, end-to-end 봇 검증은 아직 |

각 문서의 "원안과 실제 구현의 차이" 절에 원래 설계에서 실제로 뭐가 달라졌는지 정리해뒀다 — 봇으로 정밀 타이밍을 검증할 수 없는 부분(스택, 다단계 릴레이)은 대부분 더 단순한 방식으로 대체됐다. `bot-coordination.md`가 제안한 서버 방송형 역할 배정도 실제로는 `BotController.BotMode.StageAuto`의 로컬 자기판별로 대체됐다 (자세한 내용은 그 문서 상단 참고).

## 공통 설계 원칙

- **서버 권위**: 모든 스테이지 기믹은 `CoopButtonNGO`/`CoopDoorNGO`와 동일하게 `if (!IsServer) return;`로 서버에서만 판정하고, 결과만 `NetworkVariable`로 클라이언트에 뿌린다. `CLAUDE.md`의 "Server-authoritative throughout" 원칙을 그대로 따른다.
- **인원수 스케일링**: 4명 기준으로 난이도를 잡고, 5~6명일 때는 여유 인원이 생기므로 요구 조건(버튼 개수, 필요 무게 등)을 인원수에 비례해 올린다 — 하드코딩된 상수가 아니라 `NetworkManager.ConnectedClients.Count` 기반으로 계산해야 인원이 바뀌어도 재조정 없이 동작한다.
- **봇 호환성**: 이 프로젝트는 `BotController`로 사람 없이 기믹을 반복 테스트할 수 있는 봇 시뮬레이션이 이미 있다 ([[bot-simulation]] 참고, `CLAUDE.md`의 "Bot simulation" 절). 기본 모드는 `StageAuto`(씬에 있는 기믹 컴포넌트를 스스로 감지해 행동을 고름)이고, `Patrol`/`FollowNearest`는 더 단순한 수동 테스트용으로 남아있다. 각 스테이지 문서에는 어떤 봇 로직으로 자동 검증이 가능한지, 어디가 봇으로는 검증 불가능해 실제 사람 플레이테스트가 꼭 필요한지 명시한다.
- **UI 테마**: 스테이지 이름/설명 표시는 모두 `UITheme.cs` 토큰(Dosis 영문 로고, M PLUS 1p 한글 본문, PICO PARK 2 색상)을 따른다.
