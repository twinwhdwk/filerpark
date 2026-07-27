using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovementNGO : NetworkBehaviour
{
    [Header("이동 설정")]
    public float moveSpeed = 5f;
    public float jumpForce = 10f;

    [Header("바닥 체크")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.15f;
    public LayerMask groundLayer;

    // 어떤 원인으로든 바닥 밑으로 떨어졌을 때 무한 낙사(관측: Y가 -50만까지 발산)로 인한
    // 영구 소프트락 대신 스폰 지점으로 되돌리는 안전망 한계선/복귀 높이.
    private const float FallLimitY = -20f;
    private const float SafeRespawnY = 2f;
    // 씬 전환 중(Lobby 언로드 완료 ~ 다음 스테이지 로드 완료 사이) 아주 짧은 순간
    // 어떤 씬에도 SpawnPoint 태그 오브젝트가 하나도 없는 창이 생긴다. 그 타이밍에
    // 마침 중력으로 FallLimitY 밑까지 떨어진 플레이어가 있으면, 아래 안전망이 매
    // FixedUpdate(초당 50회)마다 MoveToSpawnPoint()를 다시 불러 "SpawnPoint 태그가
    // 없다" 경고를 무한 반복 스팸했다(실측: 봇 시뮬레이션 한 판에서 수십만 줄) --
    // 재시도 자체는 스폰 지점이 다시 생기면 결국 성공하지만, 이 쿨다운 없이는 그
    // 짧은 창 안에서만도 초당 50번씩 헛수고를 반복한다. 0.5초에 한 번으로만
    // 제한해도 실제 복귀 지연은 체감되지 않는다.
    private const float FallRecoveryRetryInterval = 0.5f;
    private float nextFallRecoveryAttempt;

    // 코요테 타임(플랫폼 가장자리에서 막 벗어난 직후에도 잠깐 점프를 허용) + 점프
    // 버퍼링(착지 직전에 미리 누른 점프를 착지 즉시 실행) -- "isGrounded인 그 프레임에
    // 정확히 눌러야만 점프된다"는 원래 판정은 퍼즐 플랫포머에서 특히 잘 느껴지는
    // 종류의 "입력이 씹혔다"는 답답함을 만든다. 서버 FixedUpdate가 유일한 물리/판정
    // 권위이므로 두 타이머 모두 서버에서만 관리한다 -- 클라이언트 입력(RequestJumpServerRpc)은
    // 그저 "지금 점프 버튼을 눌렀다"는 순간을 버퍼에 기록할 뿐, 실제 점프 여부/타이밍은
    // 여전히 서버가 100% 판정한다.
    private const float CoyoteTime = 0.12f;
    private const float JumpBufferTime = 0.12f;
    private float lastGroundedTime = float.NegativeInfinity;
    private float jumpBufferedUntil = float.NegativeInfinity;

    private Rigidbody2D rb;
    private bool isGrounded;
    private BotController bot;
    private PlayerSetupNGO setup;

    // 소유 클라이언트만 값을 쓰고, 서버가 FixedUpdate에서 읽어 실제 이동을 계산한다.
    private readonly NetworkVariable<float> horizontalInput = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody2D>();
        bot = GetComponent<BotController>();
        setup = GetComponent<PlayerSetupNGO>();

        // 물리 연산은 서버가 전담한다 -- 서버가 아닌 클라이언트에서는 Rigidbody2D를
        // Kinematic으로 바꿔 로컬 중력/충돌 계산을 끄고, NetworkTransform이 서버가
        // 계산한 위치를 그대로 받아 렌더링만 하도록 한다.
        rb.bodyType = IsServer ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;

        // 서버(권위 물리)의 다이나믹 바디는 Continuous 충돌로 둔다. 바닥 콜라이더가
        // 두께 1유닛으로 얇은데(gravityScale=3이라 낙하가 빠르다), 혼잡 상황의 겹침
        // 해소 임펄스로 플레이어가 한 FixedUpdate에 1유닛 넘게 튕기면 기본 Discrete는
        // 이를 놓쳐 바닥을 관통, 무한 낙사한다(실측: Y≈-50만). Continuous는 이 빠른
        // 바디-대-얇은콜라이더 관통을 막는다 -- 사람이 절벽 근처에서 점프 연타하는
        // 경우에도 동일하게 보호된다.
        if (IsServer)
        {
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        // 봇 클라이언트(-bot 인자로 실행됨)는 키보드 대신 BotController가 만든
        // 입력을 그대로 사용한다 -- 이후 경로(NetworkVariable/ServerRpc)는 사람과 동일하다.
        if (bot != null && bot.enabled)
        {
            horizontalInput.Value = bot.HorizontalInput;
            if (bot.JumpRequested)
            {
                RequestJumpServerRpc();
            }
            return;
        }

        horizontalInput.Value = Input.GetAxisRaw("Horizontal");

        if (Input.GetButtonDown("Jump"))
        {
            RequestJumpServerRpc();
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // 안전망: 바닥 밑으로 떨어지면(관통/밀림 등 어떤 원인이든) 무한 낙사로 인한 영구
        // 소프트락 대신 현재 씬의 스폰 지점으로 복귀시킨다. 근본 원인(혼잡→임펄스
        // 스파이크→관통)은 Continuous 설정과 BotController의 오검출 회복 억제로 줄였지만,
        // 이 그물은 남는 어떤 경로든 "영구 낙사"만은 확실히 차단한다 -- 지형마다 물리
        // 바닥을 까는 방식과 달리 정상 바닥 위 어디서든 통한다.
        if (transform.position.y < FallLimitY)
        {
            rb.linearVelocity = Vector2.zero;
            if (Time.time >= nextFallRecoveryAttempt)
            {
                nextFallRecoveryAttempt = Time.time + FallRecoveryRetryInterval;
                if (setup != null) setup.MoveToSpawnPoint();
                else transform.position = new Vector3(transform.position.x, SafeRespawnY, transform.position.z);
            }
            return;
        }

        isGrounded = groundCheck != null
            && Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        if (isGrounded)
        {
            lastGroundedTime = Time.time;
        }

        // 버퍼링된 점프 요청이 아직 유효하고(너무 오래 전에 누른 게 아님), 코요테
        // 타임 안에 있으면(방금까지 바닥이었음) 지금 이 물리 틱에 실행한다. 두 조건
        // 다 시간 비교라, 정상적인 "바닥에서 즉시 점프"도 자연히 이 경로 하나로 처리된다.
        if (jumpBufferedUntil >= Time.time && Time.time - lastGroundedTime <= CoyoteTime)
        {
            jumpBufferedUntil = float.NegativeInfinity;
            ExecuteJump();
        }

        rb.linearVelocity = new Vector2(horizontalInput.Value * moveSpeed, rb.linearVelocity.y);
    }

    [ServerRpc]
    private void RequestJumpServerRpc()
    {
        jumpBufferedUntil = Time.time + JumpBufferTime;
    }

    private void ExecuteJump()
    {
        // 점프 직후에도 lastGroundedTime은 "방금"이라 코요테 타임 조건이 그대로
        // 참으로 남는다 -- 무효화하지 않으면 착지 직후 0.12초 안에 점프를 두 번
        // 누르는 것만으로 이단 점프가 되어버린다.
        lastGroundedTime = float.NegativeInfinity;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        PlayJumpSfxClientRpc();
    }

    // 점프 성공 여부는 서버(isGrounded)만 판정할 수 있어 NetworkVariable로 자연히
    // 드러나지 않는다 -- 그래서 다른 코옵 기믹(문/블록/열쇠)의 SFX처럼 값 변경
    // 콜백에 얹지 못하고, 서버가 성공을 확인한 시점에 명시적으로 브로드캐스트한다.
    [ClientRpc]
    private void PlayJumpSfxClientRpc()
    {
        AudioManager.Instance?.PlaySfx(SfxId.Jump);
    }
}
