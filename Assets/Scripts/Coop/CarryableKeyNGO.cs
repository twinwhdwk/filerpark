using UnityEngine;
using Unity.Netcode;

// 공유 열쇠 하나. 누가 들고 있는지는 carrierClientId로 추적하고, 들고 있는 동안은
// 서버가 매 FixedUpdate마다 캐리어 위치로 옮긴다 (NetworkTransform이 결과를
// 클라이언트에 전파). Pickup/Drop은 서버가 거리(pickupRadius)를 검증하는
// ServerRpc라 클라이언트가 원격 캐릭터의 열쇠를 임의로 주울 수 없다.
public class CarryableKeyNGO : NetworkBehaviour
{
    private const ulong NoCarrier = ulong.MaxValue;

    public float pickupRadius = 1f;
    public Vector3 carryOffset = new Vector3(0f, 0.9f, 0f);

    public readonly NetworkVariable<ulong> carrierClientId = new NetworkVariable<ulong>(
        NoCarrier, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [ServerRpc(RequireOwnership = false)]
    public void RequestPickupServerRpc(ServerRpcParams rpcParams = default)
    {
        if (carrierClientId.Value != NoCarrier) return;

        ulong requester = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(requester, out NetworkClient client)) return;
        if (client.PlayerObject == null) return;

        float dist = Vector2.Distance(client.PlayerObject.transform.position, transform.position);
        if (dist > pickupRadius) return;

        carrierClientId.Value = requester;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestDropServerRpc(ServerRpcParams rpcParams = default)
    {
        if (carrierClientId.Value != rpcParams.Receive.SenderClientId) return;
        carrierClientId.Value = NoCarrier;
    }

    private void FixedUpdate()
    {
        if (!IsServer || carrierClientId.Value == NoCarrier) return;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(carrierClientId.Value, out NetworkClient client)
            || client.PlayerObject == null)
        {
            carrierClientId.Value = NoCarrier;
            return;
        }

        transform.position = client.PlayerObject.transform.position + carryOffset;
    }
}
