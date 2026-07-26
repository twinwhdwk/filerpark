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
            if (setup != null) setup.MoveToSpawnPoint();
            else transform.position = new Vector3(transform.position.x, SafeRespawnY, transform.position.z);
            return;
        }

        isGrounded = groundCheck != null
            && Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        rb.linearVelocity = new Vector2(horizontalInput.Value * moveSpeed, rb.linearVelocity.y);
    }

    [ServerRpc]
    private void RequestJumpServerRpc()
    {
        if (!isGrounded) return;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
    }
}
