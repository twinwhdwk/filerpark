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

    private Rigidbody2D rb;
    private bool isGrounded;
    private BotController bot;

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

        // 물리 연산은 서버가 전담한다 -- 서버가 아닌 클라이언트에서는 Rigidbody2D를
        // Kinematic으로 바꿔 로컬 중력/충돌 계산을 끄고, NetworkTransform이 서버가
        // 계산한 위치를 그대로 받아 렌더링만 하도록 한다.
        rb.bodyType = IsServer ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
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
