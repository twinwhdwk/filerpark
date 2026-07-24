using UnityEngine;
using Unity.Netcode;

// 미는 상자처럼, 플레이어와 부딪혀 밀리기만 하면 되는 소품용 범용 컴포넌트.
// PlayerMovementNGO와 동일한 원칙: 서버만 Dynamic으로 실제 물리를 시뮬레이션하고,
// 나머지 클라이언트는 Kinematic으로 두어 NetworkTransform이 전달하는 위치만 따라간다.
// Rigidbody2D + Collider2D + NetworkObject + NetworkTransform과 함께 붙이면
// 별도 스크립트 없이 플레이어가 밀 수 있는 상자가 완성된다.
[RequireComponent(typeof(Rigidbody2D))]
public class NetworkPhysicsObject : NetworkBehaviour
{
    private Rigidbody2D rb;

    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = IsServer ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
    }
}
