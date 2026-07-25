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
        Patrol,      // 스폰 지점 좌우로 왕복 -- 버튼/문처럼 특정 지형을 반복 통과시키는 테스트용
        FollowNearest, // 가장 가까운 다른 플레이어를 따라다님 -- 스택/푸시처럼 두 플레이어가 붙어있어야 하는 테스트용
    }

    [Header("봇 동작")]
    public BotMode mode = BotMode.Patrol;
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

    private void ScheduleNextJump()
    {
        nextJumpTime = Time.time + Random.Range(jumpIntervalMin, jumpIntervalMax);
    }
}
