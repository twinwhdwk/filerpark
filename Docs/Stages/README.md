# Stage Design Docs

4~6인 협동 플레이를 기준으로 설계한 4개 스테이지(미션)의 상세 설계문서. 각 스테이지는 Pico Park 시리즈에서 반복되는 협동 기믹 축(테마) 하나씩을 중심으로 짜여 있다.

| # | 스테이지 | 테마 | 상태 |
|---|---|---|---|
| 1 | [문지기 (Gatekeeper)](01-gatekeeper.md) | 동시 입력 | 기존 컴포넌트로 구현 가능 |
| 2 | [돌덩이 운반 (Block Carry)](02-block-carry.md) | 스택 & 푸시 | 신규 컴포넌트 필요 (`PushableBlockNGO`) |
| 3 | [열쇠 릴레이 (Key Relay)](03-key-relay.md) | 운반 & 균형 | 신규 컴포넌트 필요 (`CarryableKeyNGO`, `SeesawPlatformNGO`) |
| 4 | [탈출 카운트다운 (Escape Countdown)](04-escape-countdown.md) | 공동 생존 | 신규 컴포넌트 필요 (`RisingHazardNGO`, `StageManagerNGO` 확장) |

## 공통 설계 원칙

- **서버 권위**: 모든 스테이지 기믹은 `CoopButtonNGO`/`CoopDoorNGO`와 동일하게 `if (!IsServer) return;`로 서버에서만 판정하고, 결과만 `NetworkVariable`로 클라이언트에 뿌린다. `CLAUDE.md`의 "Server-authoritative throughout" 원칙을 그대로 따른다.
- **인원수 스케일링**: 4명 기준으로 난이도를 잡고, 5~6명일 때는 여유 인원이 생기므로 요구 조건(버튼 개수, 필요 무게 등)을 인원수에 비례해 올린다 — 하드코딩된 상수가 아니라 `NetworkManager.ConnectedClients.Count` 기반으로 계산해야 인원이 바뀌어도 재조정 없이 동작한다.
- **봇 호환성**: 이 프로젝트는 `BotController`(Patrol/FollowNearest)로 사람 없이 기믹을 반복 테스트할 수 있는 봇 시뮬레이션이 이미 있다 ([[bot-simulation]] 참고, `CLAUDE.md`의 "Bot simulation" 절). 각 스테이지 문서에는 어떤 봇 모드 조합으로 자동 검증이 가능한지, 어디가 봇으로는 검증 불가능해 실제 사람 플레이테스트가 꼭 필요한지 명시한다.
- **UI 테마**: 스테이지 이름/설명 표시는 모두 `UITheme.cs` 토큰(Dosis 영문 로고, M PLUS 1p 한글 본문, PICO PARK 2 색상)을 따른다.
