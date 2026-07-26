using UnityEngine;
using Unity.Netcode;

// 공유 열쇠 하나. 누가 들고 있는지는 carrierClientId로 추적하고, 들고 있는 동안은
// 서버가 매 FixedUpdate마다 캐리어 위치로 옮긴다 (NetworkTransform이 결과를
// 클라이언트에 전파). Pickup/Drop은 서버가 거리(pickupRadius)를 검증하는
// ServerRpc라 클라이언트가 원격 캐릭터의 열쇠를 임의로 주울 수 없다.
public class CarryableKeyNGO : NetworkBehaviour
{
    private const ulong NoCarrier = ulong.MaxValue;

    // 봇(그리고 사람 클라이언트도 마찬가지)이 "가까워졌다"고 판단하는 로컬
    // transform.position은 서버의 NetworkTransform 보간을 거친 값이라 실시간
    // 권위 위치보다 살짝 지연된다. 5봇 테스트에서 실측한 그 오차가 대략 0.2 유닛
    // 수준이었는데(예: 로컬은 0.8 안쪽이라 픽업을 시도했지만 서버 판정 거리는
    // 1.19였음), 반경을 1이 아니라 넉넉하게 잡아 그 오차를 흡수한다.
    public float pickupRadius = 2f;
    public Vector3 carryOffset = new Vector3(0f, 0.9f, 0f);

    public readonly NetworkVariable<ulong> carrierClientId = new NetworkVariable<ulong>(
        NoCarrier, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        carrierClientId.OnValueChanged += OnCarrierChanged;
    }

    public override void OnNetworkDespawn()
    {
        carrierClientId.OnValueChanged -= OnCarrierChanged;
    }

    private void OnCarrierChanged(ulong previousValue, ulong newValue)
    {
        AudioManager.Instance?.PlaySfx(newValue == NoCarrier ? SfxId.KeyDrop : SfxId.KeyPickup);
    }

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
