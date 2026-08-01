using System.Collections.Generic;
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

    // 들고 있는 동안 FixedUpdate가 매 틱 NetworkManager.ConnectedClients 딕셔너리를
    // 뒤지지 않도록, 캐리어가 바뀔 때(OnCarrierChanged, 서버에서만) 한 번만 찾아 캐시한다.
    // 오브젝트가 파괴되면(디스폰) Unity가 오버라이드한 == 비교가 알아서 null로 보이므로
    // 딕셔너리 재조회 없이도 연결 끊김을 그대로 감지한다.
    private Transform carrierTransform;

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

        if (!IsServer) return;

        carrierTransform = null;
        if (newValue != NoCarrier
            && NetworkManager.Singleton.ConnectedClients.TryGetValue(newValue, out NetworkClient client)
            && client.PlayerObject != null)
        {
            carrierTransform = client.PlayerObject.transform;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPickupServerRpc(ServerRpcParams rpcParams = default)
    {
        if (carrierClientId.Value != NoCarrier) return;

        ulong requester = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(requester, out NetworkClient client)) return;
        if (client.PlayerObject == null) return;

        float dist = Vector2.Distance(client.PlayerObject.transform.position, transform.position);
        if (dist > pickupRadius)
        {
            // PushableBlockNGO의 "대기 중" 상시 로그와 같은 취지 -- 왜 안 풀리는지
            // 라이브 서버에서도 바로 보이게 남겨둔다(성공 시엔 조용함).
            Debug.Log($"[CarryableKey] pickup 거부: client={requester} dist={dist:F2} (반경 {pickupRadius})");
            return;
        }

        carrierClientId.Value = requester;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestDropServerRpc(ServerRpcParams rpcParams = default)
    {
        if (carrierClientId.Value != rpcParams.Receive.SenderClientId) return;
        carrierClientId.Value = NoCarrier;
    }

    private float nextNoCarrierLogTime;

    private void FixedUpdate()
    {
        if (!IsServer) return;

        if (carrierClientId.Value == NoCarrier)
        {
            // 아무도 안 들고 있는 채로 오래 지속되면(누가 다가가다 막혔는지) 서버 로그에서
            // 바로 보이게 한다 -- PushableBlockNGO의 상시 정체 로그와 같은 취지. 라이브
            // 서버에서 운반자가 pickupRadius 밖에 멈춰 다시는 안 좁혀지는 정체가 실측된
            // 적이 있어서(다른 팀원의 위치까지 같이 남겨야 "누가 막고 있는지" 구분된다).
            if (Time.time >= nextNoCarrierLogTime)
            {
                nextNoCarrierLogTime = Time.time + 3f;
                var positions = new List<string>();
                foreach (var p in PlayerSetupNGO.ActivePlayers)
                {
                    var no = p.GetComponent<NetworkObject>();
                    if (no == null) continue;
                    float dist = Vector2.Distance(p.transform.position, transform.position);
                    positions.Add($"owner={no.OwnerClientId}/pos=({p.transform.position.x:F2},{p.transform.position.y:F2})/distToKey={dist:F2}");
                }
                Debug.Log($"[CarryableKey] {gameObject.name} 아무도 안 들고 있음, key=({transform.position.x:F2},{transform.position.y:F2}) players=[{string.Join(", ", positions)}]");
            }
            return;
        }

        if (carrierTransform == null)
        {
            carrierClientId.Value = NoCarrier;
            return;
        }

        transform.position = carrierTransform.position + carryOffset;
    }
}
