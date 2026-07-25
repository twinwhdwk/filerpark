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
    private PushableBlockNGO block;
    private CarryableKeyNGO key;
    private KeyDoorNGO keyDoor;
    private SeesawPlatformNGO seesaw;
    private RisingHazardNGO hazard;
    private CoopDoorNGO coopDoor;
    private CoopButtonNGO coopButton;

    // 매 StageAuto 프레임마다 한 번만 갱신해서 하위 로직이 공유하는 스크래치.
    private GameObject[] players;
    private float nextPickupRequest;

    // "막힘 감지" 점프: 가로로 가려는데 위치가 안 바뀌면(작은 턱에 걸림) 점프한다.
    // 랜덤 점프와 달리 실제로 막혔을 때만 발동하므로 다리/시소에서 헛점프로
    // 떨어지는 일이 없다. 밀기처럼 "일부러 눌러 붙어 정지"하는 상황엔 끄고, 시소
    // 횡단처럼 턱을 넘어야 하는 상황에서만 켠다(unstickEnabled).
    private bool unstickEnabled;
    private float unstickLastX;
    private float unstickStuckTime;

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
        unstickLastX = transform.position.x;
        ScheduleNextJump();
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
        GameObject[] all = GameObject.FindGameObjectsWithTag("Player");
        GameObject nearest = null;
        float nearestDist = float.MaxValue;

        foreach (GameObject player in all)
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
        players = GameObject.FindGameObjectsWithTag("Player");
        unstickEnabled = false;

        // 판별 순서는 각 스테이지가 가진 "고유" 기믹 기준. 4개 스테이지는 서로
        // 겹치는 기믹이 없어 이 순서로 정확히 하나만 매칭된다.
        if (hazard != null) UpdateEscape();
        else if (block != null) UpdateBlockPush();
        else if (key != null) UpdateKeyRelay();
        else if (coopDoor != null && goal != null) UpdateGatekeeper();
        else if (goal != null) MoveToward(goal.transform.position.x); // 기믹 없는 골 전용 씬: 그냥 골로 모인다
        else UpdatePatrol();

        UpdateUnstickJump();
    }

    private void RefreshStageRefs()
    {
        // goal은 모든 스테이지에 존재하므로, 스테이지가 언로드되면 goal이 파괴(fake-null)돼
        // 즉시 재스캔을 유발한다 -- 새 스테이지 참조를 프레임 지연 없이 잡는다.
        if (Time.time < nextStageScan && goal != null) return;
        nextStageScan = Time.time + 0.5f;

        goal = Object.FindAnyObjectByType<GoalZoneNGO>();
        block = Object.FindAnyObjectByType<PushableBlockNGO>();
        key = Object.FindAnyObjectByType<CarryableKeyNGO>();
        keyDoor = Object.FindAnyObjectByType<KeyDoorNGO>();
        seesaw = Object.FindAnyObjectByType<SeesawPlatformNGO>();
        hazard = Object.FindAnyObjectByType<RisingHazardNGO>();
        coopDoor = Object.FindAnyObjectByType<CoopDoorNGO>();
        coopButton = Object.FindAnyObjectByType<CoopButtonNGO>();
    }

    // ------------------------------------------------------------------ Stage 1: 문지기

    // 솔루션: rank0이 버튼 위에 서서 문을 열어두고, 나머지는 문을 통과해 골 쪽으로 간다.
    // 골존이 맵 전체를 덮도록 설계돼 있어(현 지오메트리) rank0가 버튼을 떠나지 않아도
    // 전원이 골 안에 들어와 클리어된다 -- "한 명이 열고 나머지가 통과"라는 기믹을
    // 그대로 보여주면서 100% 재현 가능하다.
    private void UpdateGatekeeper()
    {
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

    // ------------------------------------------------------------------ Stage 2: 돌덩이 운반

    // 솔루션: 전원이 블록을 목표 방향으로 민다(요구 인원 게이팅은 서버가 검증). 블록이
    // 구덩이를 잇는 다리가 되면(isInPlace) 전원이 그 위를 건너 골로 간다. 특정 봇만
    // 밀게 배정하면 한 명만 타이밍이 어긋나도 영영 정체되므로 전원 밀기로 단일 실패점을
    // 없앤다. 이 스테이지는 바닥과 블록 윗면이 평평(flush)해 점프가 필요 없다.
    private void UpdateBlockPush()
    {
        if (block.isInPlace.Value)
        {
            MoveTowardGoal();
            return;
        }

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
        if (!doorOpen)
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
        unstickEnabled = true; // 시소 윗면 턱을 만나면 막힘 점프로 넘는다

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
        unstickEnabled = true;
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

    private void MoveTowardGoal()
    {
        if (goal == null)
        {
            HorizontalInput = 0f;
            return;
        }
        MoveToward(goal.transform.position.x);
    }

    private void MoveToward(float targetX)
    {
        float dx = targetX - transform.position.x;
        HorizontalInput = Mathf.Abs(dx) < 0.2f ? 0f : Mathf.Sign(dx);
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

    private void UpdateUnstickJump()
    {
        // 가로로 가려는데(입력 있음) 위치가 거의 안 변하면 작은 턱에 걸린 것 -> 점프.
        // 켜진 상황(시소 횡단)에서만 동작하고, 밀기/대기 등에는 꺼져 있어 헛점프가 없다.
        if (!unstickEnabled || Mathf.Abs(HorizontalInput) < 0.5f)
        {
            unstickStuckTime = 0f;
            unstickLastX = transform.position.x;
            return;
        }

        if (Mathf.Abs(transform.position.x - unstickLastX) > 0.05f)
        {
            unstickStuckTime = 0f;
            unstickLastX = transform.position.x;
            return;
        }

        unstickStuckTime += Time.deltaTime;
        if (unstickStuckTime >= 0.35f)
        {
            JumpRequested = true;
            unstickStuckTime = 0f;
            unstickLastX = transform.position.x;
        }
    }

    private void ScheduleNextJump()
    {
        nextJumpTime = Time.time + Random.Range(jumpIntervalMin, jumpIntervalMax);
    }
}
