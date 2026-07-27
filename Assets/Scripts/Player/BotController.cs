using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// 원격 인간 파트너 없이도 협동 기믹(버튼/문/스택/푸시)을 검증할 수 있도록,
// 소유 클라이언트에서 키보드 대신 이 컴포넌트가 만든 입력을 PlayerMovementNGO에 흘려보낸다.
// 실제 클라이언트가 쓰는 것과 동일한 NetworkVariable/ServerRpc 경로를 그대로 타므로
// 네트워크 동작까지 포함해 진짜에 가까운 시뮬레이션이 된다.
// BotProcess.IsBot(= -bot 커맨드라인 인자로 실행된 프로세스)일 때만 스스로 활성화된다.
//
// StageAuto 모드는 각 스테이지의 "정답 솔루션"을 봇이 그대로 재현하도록 설계됐다.
// 스테이지 판별은 씬에 어떤 기믹 컴포넌트가 로드돼 있는지로 하고(씬 이름/태그 불필요),
// 역할이 필요한 경우엔 GetMyRank()(접속 Player를 OwnerClientId 오름차순 정렬한 순번)로
// 모든 봇이 서버 방송 없이 결정론적으로 같은 배정에 도달한다.
public class BotController : NetworkBehaviour
{
    public enum BotMode
    {
        Patrol,        // 스폰 지점 좌우로 왕복 -- 단순 수동 테스트용
        FollowNearest, // 가장 가까운 다른 플레이어를 따라다님 -- 단순 수동 테스트용
        StageAuto,     // 로드된 스테이지 기믹을 스스로 판별해 그 스테이지의 솔루션대로 움직인다
    }

    [Header("봇 동작")]
    public BotMode mode = BotMode.StageAuto;
    public float patrolHalfWidth = 3f;
    public float followDistance = 1.5f;
    public float jumpIntervalMin = 1.5f;
    public float jumpIntervalMax = 4f;

    public float HorizontalInput { get; private set; }
    public bool JumpRequested { get; private set; }

    // 시소 절반 폭(스프라이트 6유닛 → ±3). 시소 중심 x를 기준으로 좌/우 절반과
    // "다 건넜다" 판정 경계를 계산하는 데 쓴다.
    private const float SeesawHalf = 3f;
    private const float AcrossMargin = 0.5f;
    // 스테이지 4: 팀의 최소 진행도보다 이만큼 이상 앞서면 낙오자를 기다린다.
    private const float AllowedLead = 2.5f;

    private Vector3 spawnPosition;
    private int direction = 1;
    private float nextJumpTime;

    // 스테이지 기믹 참조 캐시. 매 프레임 FindAnyObjectByType를 부르지 않도록
    // 0.5초마다(또는 스테이지 전환으로 goal이 사라졌을 때 즉시) 다시 스캔한다.
    private float nextStageScan;
    private GoalZoneNGO goal;
    private BoxCollider2D goalCollider;
    private PushableBlockNGO block;
    private CarryableKeyNGO key;
    private KeyDoorNGO keyDoor;
    private SeesawPlatformNGO seesaw;
    private RisingHazardNGO hazard;
    private CoopDoorNGO coopDoor;
    private CoopButtonNGO coopButton;
    // Stage 5(쌍둥이 문지기)용 -- 버튼이 여럿인 스테이지에서 각 랭크가 서로 다른
    // 버튼을 맡을 수 있도록, x좌표 오름차순으로 정렬해 캐시한다. FindObjectsByType의
    // 배열 순서는 구현 세부사항이라 클라이언트마다 다를 수 있지만, 모든 클라이언트가
    // 같은 씬 파일을 로드하는 한 x좌표 자체는 항상 동일하므로 정렬 후에는 모든
    // 클라이언트가 같은 결론(같은 랭크 -> 같은 버튼)에 도달한다.
    private CoopButtonNGO[] gatekeeperButtons = System.Array.Empty<CoopButtonNGO>();

    // PlayerSetupNGO.ActivePlayers를 그대로 참조한다(매 프레임 FindGameObjectsWithTag로
    // 새 배열을 할당하지 않도록) -- 하위 로직 다수가 매 StageAuto 프레임 이 목록을 읽는다.
    private List<GameObject> players;
    private float nextPickupRequest;

    // ---- 범용 이동 회복(막힘 탈출) 레이어 ----
    // 스테이지별로 일일이 켜고 끄던 예전 "막힘 점프"를 대체한다. 기본적으로 항상 켜져
    // 있고(전 스테이지 공통), "가로로 가려는데(입력 있음) 목표 방향으로 순수 전진이
    // 일정 시간 이상 없으면" 끼인 것으로 보고 회복 기동을 실행한다. 특정 상황(밀기
    // 대형처럼 일부러 눌러 붙어 정지해야 하는 곳)에서만 recoverySuppressed로 끈다 --
    // opt-in이 아니라 opt-out이라, 새 스테이지 코드가 "켜는 걸 잊어" 소프트락되는
    // 예전 구조적 실패가 원천적으로 사라진다.
    //
    // 정체 판정은 "기준점 대비 순진행량"으로 한다. 예전 방식은 매 프레임 위치 델타(0.05)를
    // 봐서, 서버 권위 위치가 실제로 멈췄어도 NetworkTransform 보간의 좌우 지터만으로
    // 타이머가 매 프레임 리셋돼 점프가 영영 발동하지 않는 구조적 결함이 있었다.
    private const float ProgressEpsilon = 0.3f;   // 이만큼 순전진하면 "진행 중"으로 본다
    private const float StuckDelay = 0.6f;        // 순전진 없이 이 시간 지나면 끼임 확정
    private const float BaseBackupDur = 0.25f;    // 첫 회복 기동의 후퇴 시간
    private const float BackupPerAttempt = 0.12f; // 시도가 반복될수록 후퇴 시간을 늘림
    private const float MaxBackupDur = 0.75f;
    private const float ForwardDur = 0.55f;       // 후퇴 후 도움닫기 전진 시간
    // 팀원에 막힌 걸로 판정해 회복을 억제하는 유예 시간. 이보다 오래 계속되면
    // "일시적 붐빔"이 아니라 "영원히 안 움직이는 팀원"으로 보고 회복을 재개한다.
    private const float TeammateBlockGraceTime = 2.5f;

    private bool recoverySuppressed;
    private float progressAnchorX;
    private float lastProgressTime;
    private float lastDesiredDir;
    private bool recoveryActive;
    private float recoveryStartTime;
    private int recoveryAttempts;
    private bool recoveryBackupJumped;
    private bool recoveryForwardJumped;
    private float teammateBlockedSince = -1f;

    // ---- 사람처럼 보이게 하는 개성치 (스폰 시 한 번만 뽑고 그 뒤로는 고정) ----
    // 매 봇이 정확히 같은 속도로 똑같이 반응하면 "전략은 맞는데 로봇처럼 보인다"는
    // 인상을 준다. 판정 로직/타겟 좌표는 절대 안 건드리고, 실행 감각만 사람처럼
    // 개인차를 준다 -- 이미 검증된 스테이지 클리어 안정성을 건드리지 않기 위해
    // (a) 순수 이동 속도 배율, (b) 도착 판정 여유폭, (c) 상태 변화를 알아챈 뒤
    // 움직이기 시작하기까지의 반응 지연, 이 세 가지로 한정한다.
    private float reflexSpeedScale = 1f;
    private float arrivalTolerance = 0.2f;
    private float reactionDelay;
    private float doorOpenSince = -1f;
    private float blockInPlaceSince = -1f;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        enabled = BotProcess.IsBot;
        if (!enabled) return;

        spawnPosition = transform.position;
        progressAnchorX = transform.position.x;
        lastProgressTime = Time.time;
        ScheduleNextJump();

        // 사람마다 반사신경/걸음 폭이 조금씩 다르듯, 봇마다 고정된 개성치를 하나씩
        // 뽑아둔다(합의가 필요 없는 순수 로컬 연출이라 서버 방송 없이 각자 뽑아도 무방).
        reflexSpeedScale = Random.Range(0.85f, 1f);
        arrivalTolerance = Random.Range(0.15f, 0.25f);
        reactionDelay = Random.Range(0.05f, 0.35f);
    }

    private void Update()
    {
        JumpRequested = false;

        switch (mode)
        {
            case BotMode.FollowNearest:
                UpdateFollowNearest();
                MaybeRandomJump();
                break;
            case BotMode.StageAuto:
                UpdateStageAuto();
                break;
            case BotMode.Patrol:
            default:
                UpdatePatrol();
                MaybeRandomJump();
                break;
        }
    }

    // ------------------------------------------------------------------ 수동 테스트 모드

    private void MaybeRandomJump()
    {
        if (Time.time >= nextJumpTime)
        {
            JumpRequested = true;
            ScheduleNextJump();
        }
    }

    private void UpdatePatrol()
    {
        float offset = transform.position.x - spawnPosition.x;
        if (offset > patrolHalfWidth) direction = -1;
        else if (offset < -patrolHalfWidth) direction = 1;
        HorizontalInput = direction;
    }

    private void UpdateFollowNearest()
    {
        GameObject nearest = null;
        float nearestDist = float.MaxValue;

        foreach (GameObject player in PlayerSetupNGO.ActivePlayers)
        {
            if (player == gameObject) continue;
            float dist = Mathf.Abs(player.transform.position.x - transform.position.x);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = player;
            }
        }

        if (nearest == null)
        {
            HorizontalInput = 0f;
            return;
        }

        float dx = nearest.transform.position.x - transform.position.x;
        HorizontalInput = Mathf.Abs(dx) < followDistance ? 0f : Mathf.Sign(dx);
    }

    // ------------------------------------------------------------------ StageAuto

    private void UpdateStageAuto()
    {
        RefreshStageRefs();
        players = PlayerSetupNGO.ActivePlayers;
        recoverySuppressed = false;

        // 판별 순서는 각 스테이지가 가진 "고유" 기믹 기준. 4개 스테이지는 서로
        // 겹치는 기믹이 없어 이 순서로 정확히 하나만 매칭된다.
        if (hazard != null) UpdateEscape();
        else if (block != null) UpdateBlockPush();
        else if (key != null) UpdateKeyRelay();
        else if (coopDoor != null && goal != null) UpdateGatekeeper();
        else if (goal != null) MoveToward(goal.transform.position.x); // 기믹 없는 골 전용 씬: 그냥 골로 모인다
        else UpdatePatrol();

        // 순수 이동(비-회복) 구간에만 개인차 속도를 곱한다 -- 판정/타겟은 그대로 두고
        // "얼마나 빨리 걷는가"만 사람마다 다르게 보이게 한다. 미는 대형(recoverySuppressed)
        // 과 회복 기동은 각각 요구 인원 게이팅/탈출 신뢰성이 걸려 있어 원래 크기 그대로 둔다.
        if (!recoverySuppressed)
        {
            HorizontalInput *= reflexSpeedScale;
        }

        ApplyLocomotionRecovery();
    }

    private void RefreshStageRefs()
    {
        // goal은 모든 스테이지에 존재하므로, 스테이지가 언로드되면 goal이 파괴(fake-null)돼
        // 즉시 재스캔을 유발한다 -- 새 스테이지 참조를 프레임 지연 없이 잡는다.
        if (Time.time < nextStageScan && goal != null) return;
        nextStageScan = Time.time + 0.5f;

        goal = Object.FindAnyObjectByType<GoalZoneNGO>();
        goalCollider = goal != null ? goal.GetComponent<BoxCollider2D>() : null;
        block = Object.FindAnyObjectByType<PushableBlockNGO>();
        key = Object.FindAnyObjectByType<CarryableKeyNGO>();
        keyDoor = Object.FindAnyObjectByType<KeyDoorNGO>();
        seesaw = Object.FindAnyObjectByType<SeesawPlatformNGO>();
        hazard = Object.FindAnyObjectByType<RisingHazardNGO>();
        coopDoor = Object.FindAnyObjectByType<CoopDoorNGO>();
        coopButton = Object.FindAnyObjectByType<CoopButtonNGO>();

        gatekeeperButtons = Object.FindObjectsByType<CoopButtonNGO>(FindObjectsSortMode.None);
        System.Array.Sort(gatekeeperButtons, (a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
    }

    // ------------------------------------------------------------------ Stage 1/5: 문지기 계열

    // 솔루션: rank0이 버튼 위에 서서 문을 열어두고, 나머지는 문을 통과해 골 쪽으로 간다.
    // 골존이 맵 전체를 덮도록 설계돼 있어(현 지오메트리) rank0가 버튼을 떠나지 않아도
    // 전원이 골 안에 들어와 클리어된다 -- "한 명이 열고 나머지가 통과"라는 기믹을
    // 그대로 보여주면서 100% 재현 가능하다.
    private void UpdateGatekeeper()
    {
        // Stage 5(쌍둥이 문지기)는 같은 컴포넌트 조합(coopDoor+goal)을 쓰지만 문이
        // 버튼 하나가 아니라 둘 다 필요하다 -- requiredButtons로 두 변형을 구분한다.
        if (coopDoor != null && coopDoor.requiredButtons > 1)
        {
            UpdateTwinGatekeeper();
            return;
        }

        int rank = GetMyRank();

        if (rank == 0 && coopButton != null)
        {
            MoveToward(coopButton.transform.position.x);
            return;
        }

        // 나머지: 문 너머로 이동(문이 닫혀 있으면 앞에서 대기하다 열리면 통과).
        float targetX = coopDoor.transform.position.x + 2f;
        MoveToward(targetX);
    }

    // 솔루션: rank0/rank1(x좌표 오름차순으로 정렬된 버튼 배열의 앞쪽 두 자리)이
    // 각자 다른 버튼을 맡아 동시에 밟고, 나머지는 문 너머로 이동한다 -- Stage1과
    // 동일한 "일부가 유지, 나머지가 통과" 구조를 인원만 둘로 늘린 것.
    private void UpdateTwinGatekeeper()
    {
        int rank = GetMyRank();

        if (rank < gatekeeperButtons.Length)
        {
            MoveToward(gatekeeperButtons[rank].transform.position.x);
            return;
        }

        float targetX = coopDoor.transform.position.x + 2f;
        MoveToward(targetX);
    }

    // ------------------------------------------------------------------ Stage 2: 돌덩이 운반

    // 솔루션: 전원이 블록을 목표 방향으로 민다(요구 인원 게이팅은 서버가 검증). 블록이
    // 구덩이를 잇는 다리가 되면(isInPlace) 전원이 그 위를 건너 골로 간다. 특정 봇만
    // 밀게 배정하면 한 명만 타이밍이 어긋나도 영영 정체되므로 전원 밀기로 단일 실패점을
    // 없앤다. 바닥과 블록 윗면은 평평(flush)해 평상시엔 점프가 필요 없지만, 이음매
    // 콜라이더 코너에 끼는 경우가 있어 그건 범용 회복 레이어가 자동으로 처리한다.
    private void UpdateBlockPush()
    {
        bool inPlace = block.isInPlace.Value;
        if (inPlace && blockInPlaceSince < 0f) blockInPlaceSince = Time.time;
        if (!inPlace) blockInPlaceSince = -1f;

        if (inPlace)
        {
            // 블록이 자리 잡은 걸 알아채는 데도 사람처럼 반응 지연을 조금 둔다(각자
            // reactionDelay만큼) -- 다 도착하자마자 전원이 프레임 단위로 똑같이 방향을
            // 트는 게 아니라, 몇 프레임씩 어긋나게 건너기 시작한다. 판정/타겟은 그대로라
            // 클리어 신뢰성에는 영향이 없다.
            if (Time.time - blockInPlaceSince < reactionDelay)
            {
                HorizontalInput = 0f;
                return;
            }

            // 다리(블록)가 놓인 뒤 골로 걸어가는 구간. 블록/바닥 이음매(콜라이더 코너)에
            // 봇이 끼는 경우가 있는데, 이 구간은 회복 레이어를 기본값(켜짐) 그대로 둬서
            // 끼면 자동으로 후퇴+도움닫기로 빠져나온다. 블록을 Ground 레이어로 둔 덕에
            // 블록 위에서도 점프가 서버 검증(groundCheck)을 통과한다 -- 예전엔 블록이
            // Ground가 아니라 그 위에서의 점프 요청이 서버에서 조용히 거부돼, 이 이음매
            // 끼임에 점프가 무력했던 것이 5봇 테스트에서 영구 정체를 만든 근본 원인이었다.
            MoveTowardGoal();
            return;
        }

        // 미는 단계: 전원이 목표 방향으로 블록을 민다. 일부러 블록에 눌러 붙어 정지하는
        // 대형이라 위치가 안 변하는 게 정상 -- 여기서 회복이 발동하면 후퇴/점프로 대형이
        // 흐트러져 요구 인원 게이팅이 풀리지 않으므로 이 구간만 회복을 끈다(유일한 opt-out).
        recoverySuppressed = true;

        if (block.targetPoint == null)
        {
            HorizontalInput = 0f;
            return;
        }

        HorizontalInput = Mathf.Sign(block.targetPoint.position.x - block.transform.position.x);
    }

    // ------------------------------------------------------------------ Stage 3: 열쇠 릴레이

    // 솔루션:
    //  - rank0(운반자): 열쇠를 주워 문 앞으로 가서 문을 열어둔 채 대기 → 나머지가 다
    //    건너면 자기도 시소를 단독으로(=자동 균형) 건너 골로.
    //  - 나머지: 문이 열릴 때까지 대기 → 시소를 "균형 규칙"으로 한 명씩 건넌다(한쪽이
    //    무겁지 않을 때만 진입). 이러면 |좌−우| ≤ 1 로 유지돼 시소가 tiltThreshold(1.5)를
    //    넘지 않고 계속 수평이라 안전하게 건널 수 있다.
    private void UpdateKeyRelay()
    {
        int rank = GetMyRank();
        float seesawX = seesaw != null ? seesaw.transform.position.x : 12f;
        float acrossX = seesawX + SeesawHalf + AcrossMargin;

        if (rank == 0)
        {
            UpdateKeyCarrier(seesawX, acrossX);
            return;
        }

        // --- 비운반자 ---
        bool doorOpen = keyDoor != null && keyDoor.isOpen.Value;
        if (doorOpen && doorOpenSince < 0f) doorOpenSince = Time.time;
        if (!doorOpen) doorOpenSince = -1f;

        // 문이 열린 걸 알아채기까지도 사람마다 조금씩 다른 반응 지연(reactionDelay)을
        // 준다 -- 어차피 스폰이 흩어져 있어 자연 시차가 나던 걸, 문이 열리는 순간에도
        // 전원이 프레임 단위로 똑같이 반응하지 않게 한다. 시소 균형 규칙은 위치 기준
        // 이라 이 지연 자체가 순서에 영향을 줄 뿐 안정성은 그대로다.
        if (!doorOpen || Time.time - doorOpenSince < reactionDelay)
        {
            // 문이 열리기 전엔 제자리 대기(운반자가 열쇠를 문으로 가져올 때까지).
            // 스폰이 −6~6로 흩어져 있어, 대기 중 흩어진 채로 있다가 문이 열리면
            // 자연히 시차를 두고 시소에 도착 -> 균형 규칙이 순차 횡단을 만든다.
            HorizontalInput = 0f;
            return;
        }

        if (transform.position.x >= acrossX)
        {
            MoveTowardGoal();
            return;
        }

        CrossSeesawBalanced(seesawX);
    }

    private void UpdateKeyCarrier(float seesawX, float acrossX)
    {
        // 아직 안 들었으면: 열쇠로 다가가 주울 때까지 요청을 반복한다. 클라이언트가 보는
        // 자기 위치는 NetworkTransform 보간 때문에 서버가 보는 위치보다 뒤처질 수 있어
        // (실측: 약 0.2유닛) 한 번의 판정으로는 놓칠 수 있으므로, 넉넉한 반경 안에서
        // 계속 다가가며 반복 요청해 서버가 승인할 때까지 시도한다.
        if (key.carrierClientId.Value != OwnerClientId)
        {
            MoveToward(key.transform.position.x);
            float dist = Vector2.Distance(transform.position, key.transform.position);
            if (dist < key.pickupRadius * 1.5f && Time.time >= nextPickupRequest)
            {
                nextPickupRequest = Time.time + 0.2f;
                key.RequestPickupServerRpc();
            }
            return;
        }

        // 들었음: 나머지가 다 건넜으면 나도 건너 골로(단독이라 시소는 자동으로 수평).
        if (AllOthersAcross(acrossX))
        {
            if (transform.position.x >= acrossX) MoveTowardGoal();
            else CrossSeesawSolo(seesawX);
            return;
        }

        // 아직 건너는 중인 동료가 있으면 문 앞을 지키며 문을 열어둔다. 문 중심에서 살짝
        // 왼쪽(−0.6)에 서면, 오른쪽으로 밀려나도 문 중심(열림 판정 중심)에 가까워져
        // 오히려 더 확실히 열린 채로 유지된다.
        float doorX = keyDoor != null ? keyDoor.transform.position.x : 7f;
        MoveToward(doorX - 0.6f);
    }

    // 시소 좌/우 절반의 인원을 세어, 내가 왼쪽에서 진입할 때 좌측이 우측보다 무거워지지
    // 않을 때만(L ≤ R) 한 발 올린다. 이미 시소 위면 오른쪽으로 계속 건너간다. 이 규칙은
    // 독립 프로세스인 봇들끼리 합의 없이도 |좌−우| ≤ 1을 유지시켜 시소를 수평으로 만든다.
    private void CrossSeesawBalanced(float seesawX)
    {
        // 회복 레이어가 기본으로 켜져 있어, 시소 윗면 이음매에 끼면 자동으로 빠져나온다
        // (시소도 Ground 레이어라 그 위에서 점프가 서버 검증을 통과한다).
        float myX = transform.position.x;
        float leftEdge = seesawX - SeesawHalf;

        if (myX >= leftEdge)
        {
            HorizontalInput = 1f; // 이미 시소 위 -> 계속 오른쪽으로 건넌다
            return;
        }

        int left = 0, right = 0;
        foreach (GameObject p in players)
        {
            float px = p.transform.position.x;
            if (px >= leftEdge && px < seesawX) left++;
            else if (px >= seesawX && px <= seesawX + SeesawHalf) right++;
        }

        if (left <= right)
        {
            HorizontalInput = 1f; // 좌측이 안 무거우니 진입
        }
        else
        {
            MoveToward(leftEdge - 0.3f); // 시소 바로 앞에서 대기(균형 맞을 때까지)
        }
    }

    private void CrossSeesawSolo(float seesawX)
    {
        HorizontalInput = 1f; // 단독 횡단은 항상 균형(한 명뿐) -> 그냥 오른쪽으로
    }

    // ------------------------------------------------------------------ Stage 4: 탈출 카운트다운

    // 솔루션: 전원이 오른쪽 골로 달린다. 단 (1) 골에 도달하면 멈춰서 낭떠러지로
    // 행진하지 않고 골 안에서 대기하며 나머지를 기다리고, (2) 팀의 최소 진행도보다
    // 너무 앞서면 낙오자를 기다린다("제일 느린 사람에게 맞춘다"). 벽(0.3u/s)보다
    // 봇(5u/s)이 훨씬 빨라 시간 압박은 문제되지 않고, 관건은 전원이 동시에 골 안에
    // 모이는 것이다 -- 그래서 골에서 멈춰 뭉치게 한다.
    private void UpdateEscape()
    {
        if (goal == null)
        {
            HorizontalInput = 0f;
            return;
        }

        float goalX = goal.transform.position.x;
        float myX = transform.position.x;

        // 골 안에 안전히 들어왔으면 멈춰서 대기(골존 폭 6 → 중심−2면 확실히 안쪽).
        if (myX >= goalX - 2f)
        {
            HorizontalInput = 0f;
            return;
        }

        float teamMinX = float.MaxValue;
        foreach (GameObject p in players) teamMinX = Mathf.Min(teamMinX, p.transform.position.x);

        // 팀을 너무 앞서면 멈춰서 기다린다(가장 뒤처진 봇은 myLead=0이라 항상 전진 -> 교착 없음).
        HorizontalInput = (myX - teamMinX > AllowedLead) ? 0f : 1f;
    }

    // ------------------------------------------------------------------ 공통 헬퍼

    // 골로 모일 때 전원이 "골 중심 한 점"으로 수렴하지 않고 rank별로 골존 안에 흩어진
    // 슬롯에 자리잡게 한다. 5명이 좁은 골(폭 6)의 한 점에 몰리면 서로 몸으로 밀쳐
    // (다이나믹 바디끼리 겹침 해소가 큰 임펄스를 만든다) 튕겨 오르내리며 (a) 골존의
    // 세로 트리거 범위(높이 1.6) 밖으로 점프해 나갔다 들어왔다 반복해 "전원 동시 존재"가
    // 성립하지 않고, (b) 그 속도 스파이크가 얇은 바닥을 관통시켜 낙사를 유발했다.
    // rank로 폭을 나눠 서면 서로 겹치지 않아 조용히 골 안에 머문다 -> 클리어가 성립한다.
    private void MoveTowardGoal()
    {
        if (goal == null)
        {
            HorizontalInput = 0f;
            return;
        }

        float goalX = goal.transform.position.x;
        float inner = Mathf.Max(0.4f, GoalHalfWidth() - 0.8f); // 가장자리에서 살짝 안쪽까지만 사용
        int n = Mathf.Max(1, players.Count);
        int rank = Mathf.Clamp(GetMyRank(), 0, n - 1);
        float frac = (n <= 1) ? 0.5f : (float)rank / (n - 1); // 0..1
        float slotX = goalX - inner + frac * (2f * inner);

        MoveToward(slotX);
    }

    // 골존 트리거의 월드 반폭. 스테이지마다 다르므로(넓은 S1, 좁은 S2~4) 콜라이더에서 읽는다.
    private float GoalHalfWidth()
    {
        if (goal == null || goalCollider == null) return 3f;
        return Mathf.Abs(goalCollider.size.x * goal.transform.lossyScale.x) * 0.5f;
    }

    // 이미 골존 안(가로 기준)에 들어와 있는가. 들어와 있으면 위치는 충분히 좋으므로
    // 회복 기동을 걸 필요가 없다.
    private bool IsInsideGoalZone()
    {
        if (goal == null) return false;
        return Mathf.Abs(transform.position.x - goal.transform.position.x) <= GoalHalfWidth();
    }

    // 진행 방향으로 몸 하나 거리 안에 다른 플레이어가 밀착해 있는가(= 지형이 아니라
    // 팀원 혼잡에 막힌 것). 진행 방향 앞쪽, 대략 같은 높이의 팀원만 본다.
    private bool TeammateDirectlyAhead(float dir)
    {
        float myX = transform.position.x;
        float myY = transform.position.y;
        foreach (GameObject p in players)
        {
            if (p == gameObject) continue;
            float dx = p.transform.position.x - myX;
            if (dx * dir <= 0f) continue;                              // 앞쪽만
            if (Mathf.Abs(dx) > 1.1f) continue;                        // 몸 하나 거리 안(밀착)만
            if (Mathf.Abs(p.transform.position.y - myY) > 1.2f) continue; // 대략 같은 높이만
            return true;
        }
        return false;
    }

    private void MoveToward(float targetX)
    {
        float dx = targetX - transform.position.x;
        HorizontalInput = Mathf.Abs(dx) < arrivalTolerance ? 0f : Mathf.Sign(dx);
    }

    // 접속한 Player 오브젝트 중 내 OwnerClientId보다 작은 것의 수 = 오름차순 정렬 시 내 순번.
    // 모든 클라이언트가 동일한 OwnerClientId 집합을 보므로 서버 방송 없이 같은 결론에 도달한다.
    private int GetMyRank()
    {
        int rank = 0;
        foreach (GameObject p in players)
        {
            NetworkObject no = p.GetComponent<NetworkObject>();
            if (no != null && no.OwnerClientId < OwnerClientId) rank++;
        }
        return rank;
    }

    private bool AllOthersAcross(float acrossX)
    {
        foreach (GameObject p in players)
        {
            if (p == gameObject) continue;
            if (p.transform.position.x < acrossX) return false;
        }
        return true;
    }

    // 스테이지 로직이 정한 목표 이동 입력(HorizontalInput)을 후처리해, 실제로 끼였을 때만
    // 범용 회복 기동으로 덮어쓴다. 모든 스테이지가 공유하는 단일 이동 회복 레이어 --
    // "어디로 가려 하는가"(스테이지 의도)와 "실제로 어떻게 빠져나가는가"(이 레이어)를 분리한다.
    private void ApplyLocomotionRecovery()
    {
        float desired = HorizontalInput;
        float myX = transform.position.x;

        // 이동 의사가 없거나(대기/도착으로 입력 0) 회복이 억제된 구간(밀기 대형)이면
        // 회복을 끄고 진행도 추적만 리셋한다.
        if (recoverySuppressed || Mathf.Abs(desired) < 0.5f)
        {
            progressAnchorX = myX;
            lastProgressTime = Time.time;
            recoveryActive = false;
            recoveryAttempts = 0;
            lastDesiredDir = 0f;
            return;
        }

        float dir = Mathf.Sign(desired);

        // 목표 방향이 바뀌면 기준점을 다시 잡는다(방향 전환은 정체가 아니다).
        if (dir != lastDesiredDir)
        {
            lastDesiredDir = dir;
            progressAnchorX = myX;
            lastProgressTime = Time.time;
            recoveryActive = false;
            recoveryAttempts = 0;
        }

        // 의도한 방향으로 ProgressEpsilon 이상 순전진했으면 정상 진행 -> 타이머/회복 리셋.
        // 기준점 대비 "순진행량"이라 보간이 좌우로 흔들려도(지터) 리셋되지 않는다.
        if ((myX - progressAnchorX) * dir >= ProgressEpsilon)
        {
            progressAnchorX = myX;
            lastProgressTime = Time.time;
            recoveryActive = false;
            recoveryAttempts = 0;
            return;
        }

        // 이미 골존 안이면 위치가 충분히 좋아 언스틱이 필요 없다(골 판정은 존 안에
        // "있는가"이지 정확한 중심 도달이 아니다) -- 이건 시간 제한 없이 계속 억제한다.
        if (IsInsideGoalZone())
        {
            progressAnchorX = myX;
            lastProgressTime = Time.time;
            recoveryActive = false;
            recoveryAttempts = 0;
            teammateBlockedSince = -1f;
            return;
        }

        // 진행 방향에 팀원이 몸 하나 거리로 밀착해 있으면 지형이 아니라 "동적 팀원 혼잡"일
        // 수 있다 -- 점프로는 애초에 안 풀리고, 밀집한 다이나믹 바디들 사이에 큰 충돌
        // 임펄스를 만들어 오히려 튕겨나가(→ 얇은 바닥 관통 낙사) 상황을 악화시킬 수 있다.
        // 다만 이 억제를 무기한 걸면 안 된다 -- Stage3 5봇 테스트로 실측: 열쇠 운반자가
        // "문이 열릴 때까지 제자리 대기"하는(=영원히 안 움직이는) 팀원 바로 옆에서 막혀,
        // 매 프레임 이 조건이 다시 참이 되어 정체 타이머가 한 번도 못 쌓이고 영구
        // 데드락에 빠졌다(운반자가 못 움직이니 문도 안 열리고, 대기자는 문이 안 열리니
        // 계속 그 자리 -- 서로가 서로를 막음). 골 근처의 일시적 붐빔(몇 초 안에 팀원이
        // 자리를 잡아 풀림)과 "팀원이 원래 거기 영원히 서 있음"을 구분할 수 없으므로,
        // 이 억제 자체에 유예 시간을 둬서 오래 지속되면 포기하고 일반 회복 로직으로
        // 넘긴다 -- 그러면 최소한 옆으로 비켜서라도 데드락을 깬다.
        if (TeammateDirectlyAhead(dir))
        {
            if (teammateBlockedSince < 0f) teammateBlockedSince = Time.time;
            if (Time.time - teammateBlockedSince < TeammateBlockGraceTime)
            {
                progressAnchorX = myX;
                lastProgressTime = Time.time;
                recoveryActive = false;
                recoveryAttempts = 0;
                return;
            }
        }
        else
        {
            teammateBlockedSince = -1f;
        }

        if (!recoveryActive)
        {
            if (Time.time - lastProgressTime < StuckDelay) return; // 아직 정체 전 -> 정상 이동 유지
            recoveryActive = true;
            recoveryStartTime = Time.time;
            recoveryBackupJumped = false;
            recoveryForwardJumped = false;
            recoveryAttempts++;
        }

        // 회복 기동: (1) 진행 방향의 반대로 잠깐 물러나며 점프해 끼임면에서 떨어진 뒤
        // (2) 도움닫기로 다시 전진+점프해 코너/턱을 넘는다. 시도가 반복될수록 후퇴 시간을
        // 키워(BackupPerAttempt) 어떤 이음매에서도 결국 빠져나오게 한다 -- 단발 점프로는
        // 못 넘는 "평평한 수직 코너 끼임"까지 포함해 영구 소프트락을 원천 차단한다.
        // 후퇴는 대개 Ground 위(좌측 바닥/블록)로 물러나는 것이라, 뒤이은 도움닫기 점프가
        // 발판(grounded)을 확보한 상태에서 발동해 서버 검증을 통과한다.
        float backupDur = Mathf.Min(BaseBackupDur + BackupPerAttempt * (recoveryAttempts - 1), MaxBackupDur);
        float t = Time.time - recoveryStartTime;

        if (t < backupDur)
        {
            HorizontalInput = -dir; // 끼임면에서 후퇴
            if (!recoveryBackupJumped) { JumpRequested = true; recoveryBackupJumped = true; }
        }
        else if (t < backupDur + ForwardDur)
        {
            HorizontalInput = dir; // 도움닫기 전진
            if (!recoveryForwardJumped) { JumpRequested = true; recoveryForwardJumped = true; }
        }
        else
        {
            // 기동 종료 -> 재평가. 빠져나왔으면 다음 프레임 진행도 판정이 통과되고,
            // 아직 막혀 있으면 StuckDelay 뒤 더 큰 후퇴로 다음 기동이 발동한다.
            recoveryActive = false;
            progressAnchorX = myX;
            lastProgressTime = Time.time;
        }
    }

    private void ScheduleNextJump()
    {
        nextJumpTime = Time.time + Random.Range(jumpIntervalMin, jumpIntervalMax);
    }
}
