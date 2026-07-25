using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// 원격 인간 파트너 없이도 협동 기믹(버튼/문/스택/푸시)을 검증할 수 있도록,
// 소유 클라이언트에서 키보드 대신 이 컴포넌트가 만든 입력을 PlayerMovementNGO에 흘려보낸다.
// 실제 클라이언트가 쓰는 것과 동일한 NetworkVariable/ServerRpc 경로를 그대로 타므로
// 네트워크 동작까지 포함해 진짜에 가까운 시뮬레이션이 된다.
// BotProcess.IsBot(= -bot 커맨드라인 인자로 실행된 프로세스)일 때만 스스로 활성화된다.
public class BotController : NetworkBehaviour
{
    public enum BotMode
    {
        Patrol,        // 스폰 지점 좌우로 왕복 -- 버튼/문처럼 특정 지형을 반복 통과시키는 테스트용
        FollowNearest, // 가장 가까운 다른 플레이어를 따라다님 -- 스택/푸시처럼 두 플레이어가 붙어있어야 하는 테스트용
        StageAuto,     // 현재 로드된 스테이지에 어떤 기믹 컴포넌트가 있는지 보고 알맞은 협동 행동을 스스로 고른다
    }

    [Header("봇 동작")]
    public BotMode mode = BotMode.StageAuto;
    public float patrolHalfWidth = 3f;
    public float followDistance = 1.5f;
    public float jumpIntervalMin = 1.5f;
    public float jumpIntervalMax = 4f;

    public float HorizontalInput { get; private set; }
    public bool JumpRequested { get; private set; }

    private Vector3 spawnPosition;
    private int direction = 1;
    private float nextJumpTime;

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
        ScheduleNextJump();
    }

    private void Update()
    {
        JumpRequested = false;

        switch (mode)
        {
            case BotMode.FollowNearest:
                UpdateFollowNearest();
                break;
            case BotMode.StageAuto:
                UpdateStageAuto();
                break;
            case BotMode.Patrol:
            default:
                UpdatePatrol();
                break;
        }

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
        // ConnectedClients는 서버에서만 신뢰할 수 있으므로, 모든 클라이언트에서
        // 동일하게 동작하도록 로컬에 스폰되어 있는 "Player" 태그 오브젝트를 직접 찾는다.
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        GameObject nearest = null;
        float nearestDist = float.MaxValue;

        foreach (GameObject player in players)
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

    // 스테이지 2~4는 각자 새 기믹 컴포넌트(PushableBlockNGO/CarryableKeyNGO/RisingHazardNGO)를
    // 씬에 배치하는 것으로 스스로를 드러낸다 -- 봇은 씬 이름이나 태그를 몰라도, 로드된
    // 씬에서 어떤 컴포넌트를 찾을 수 있는지만 보고 알맞은 행동을 고른다. 새 스테이지를
    // 추가해도 이 파일이나 태그 목록(TagManager.asset)을 손댈 필요가 없다는 게 핵심 --
    // Player 프리팹은 모든 씬에서 공유되므로 이 자기-판별 방식이 아니면 씬마다 봇 동작을
    // 다시 배선해야 한다.
    private void UpdateStageAuto()
    {
        RisingHazardNGO hazard = Object.FindFirstObjectByType<RisingHazardNGO>();
        if (hazard != null)
        {
            UpdateGroupAdvance();
            return;
        }

        PushableBlockNGO block = Object.FindFirstObjectByType<PushableBlockNGO>();
        if (block != null)
        {
            UpdatePushTarget(block);
            return;
        }

        CarryableKeyNGO key = Object.FindFirstObjectByType<CarryableKeyNGO>();
        if (key != null)
        {
            UpdateKeyRelay(key);
            return;
        }

        UpdatePatrol();
    }

    // 스테이지 2 (돌덩이 운반): 접속 인원의 60% 이상만 "미는 팀"으로 배정한다
    // (순위는 OwnerClientId 오름차순 -- 모든 클라이언트가 동일한 목록을 보므로 서버가
    // 따로 역할을 방송할 필요 없이 각자 결정론적으로 같은 결론에 도달한다). 나머지는
    // 블록이 도착할 때까지 대기했다가 골로 향한다.
    private void UpdatePushTarget(PushableBlockNGO block)
    {
        if (block.isInPlace.Value)
        {
            UpdateGoToGoal();
            return;
        }

        if (block.targetPoint == null)
        {
            HorizontalInput = 0f;
            return;
        }

        int totalPlayers = GameObject.FindGameObjectsWithTag("Player").Length;
        int requiredPushers = Mathf.Max(1, Mathf.CeilToInt(totalPlayers * 0.6f));
        int myRank = GetMyRank();

        if (myRank >= requiredPushers)
        {
            HorizontalInput = 0f;
            return;
        }

        // 계산된 "대기 지점"으로 걸어가게 했더니 여러 겹의 버그가 났다 -- 전원 같은
        // 좌표를 노리면 서로의 물리 콜라이더에 막혀 정체되고(overlap이 3을 못 넘음),
        // rank별로 좌표를 흩어놔도(0.6 간격) 그 간격이 플레이어 폭(~0.9)보다 좁아
        // 여전히 서로 부딪혀 정체되었다. 스폰 지점이 전부 블록의 미는 반대편(왼쪽)에
        // 있다는 레벨 전제 위에서, 그냥 미는 방향으로 계속 걸어가게 하는 게 훨씬
        // 단순하고 견고하다 -- 블록의 솔리드 콜라이더가 알아서 정지시키고, 그 위치는
        // 이미 넓은 트리거 범위(pushStandoffDistance보다 더 넓게 잡음) 안에 든다.
        float pushDir = Mathf.Sign(block.targetPoint.position.x - block.transform.position.x);
        HorizontalInput = pushDir;
    }

    // 스테이지 3 (열쇠 릴레이): 순위 0번 봇만 열쇠를 줍고 문까지 운반한다. 나머지는
    // 문이 열리기 전까지 시소 좌/우로 절반씩 나뉘어 균형을 맞추다가, 문이 열리면 골로
    // 향한다. 설계 문서의 "틈 건너 릴레이"는 정밀 타이밍이 필요해 봇 자동화 신뢰도가
    // 가장 낮다고 문서 스스로 명시한 부분이라, 이 구현은 단일 운반자로 단순화했다 --
    // 사람 플레이테스트로 다단계 릴레이를 검증하는 몫은 남겨둔다.
    private void UpdateKeyRelay(CarryableKeyNGO key)
    {
        KeyDoorNGO door = Object.FindFirstObjectByType<KeyDoorNGO>();
        int myRank = GetMyRank();

        if (myRank == 0)
        {
            if (key.carrierClientId.Value != OwnerClientId)
            {
                MoveToward(key.transform.position);
                float distToKey = Vector2.Distance(transform.position, key.transform.position);
                if (distToKey < key.pickupRadius * 0.8f)
                {
                    key.RequestPickupServerRpc();
                }
                return;
            }

            if (door != null) MoveToward(door.transform.position);
            else UpdateGoToGoal();
            return;
        }

        SeesawPlatformNGO seesaw = Object.FindFirstObjectByType<SeesawPlatformNGO>();
        if (seesaw != null && (door == null || !door.isOpen.Value))
        {
            Transform holdPoint = (myRank % 2 == 1) ? seesaw.leftHoldPoint : seesaw.rightHoldPoint;
            if (holdPoint != null)
            {
                MoveToward(holdPoint.position);
                return;
            }
        }

        UpdateGoToGoal();
    }

    // 스테이지 4 (탈출 카운트다운): 역할 분담이 없다 -- 전원이 같은 규칙(팀의 최소
    // 진행도보다 너무 앞서지 않기)을 따르는 것만으로 "제일 느린 사람에게 맞춘다"는
    // 스테이지 취지가 그대로 구현된다.
    private void UpdateGroupAdvance()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        float teamMinX = float.MaxValue;
        foreach (GameObject player in players)
        {
            teamMinX = Mathf.Min(teamMinX, player.transform.position.x);
        }

        const float allowedLead = 2.5f;
        float myLead = transform.position.x - teamMinX;
        HorizontalInput = myLead > allowedLead ? 0f : 1f;
    }

    private void UpdateGoToGoal()
    {
        GoalZoneNGO goal = Object.FindFirstObjectByType<GoalZoneNGO>();
        if (goal == null)
        {
            HorizontalInput = 0f;
            return;
        }
        MoveToward(goal.transform.position);
    }

    private void MoveToward(Vector3 target)
    {
        float dx = target.x - transform.position.x;
        HorizontalInput = Mathf.Abs(dx) < 0.2f ? 0f : Mathf.Sign(dx);
    }

    // 접속한 Player 오브젝트를 OwnerClientId 오름차순으로 정렬했을 때 내 순번.
    // NetworkObject.OwnerClientId는 모든 클라이언트에 이미 동기화되어 있으므로,
    // 서버가 역할을 따로 배정/방송하지 않아도 모든 봇이 동일한 결론에 도달한다.
    private int GetMyRank()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        List<ulong> ids = new List<ulong>();
        foreach (GameObject player in players)
        {
            NetworkObject networkObject = player.GetComponent<NetworkObject>();
            if (networkObject != null) ids.Add(networkObject.OwnerClientId);
        }
        ids.Sort();
        return ids.IndexOf(OwnerClientId);
    }

    private void ScheduleNextJump()
    {
        nextJumpTime = Time.time + Random.Range(jumpIntervalMin, jumpIntervalMax);
    }
}
