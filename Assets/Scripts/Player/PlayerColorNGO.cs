using UnityEngine;
using Unity.Netcode;

// Player Prefab에 부착 (SpriteRenderer 필요). 접속 순서(OwnerClientId)에 따라
// 서버가 고정 팔레트에서 색을 배정하고 NetworkVariable로 뿌린다 -- 늦게 들어온
// 클라이언트도 스폰되는 즉시 이미 배정된 색을 그대로 받는다.
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerColorNGO : NetworkBehaviour
{
    private static readonly Color[] PlayerColors =
    {
        new Color(0.95f, 0.35f, 0.35f), // red
        new Color(0.35f, 0.55f, 0.95f), // blue
        new Color(0.40f, 0.85f, 0.40f), // green
        new Color(0.95f, 0.85f, 0.30f), // yellow
        new Color(0.75f, 0.40f, 0.90f), // purple
        new Color(0.95f, 0.60f, 0.20f), // orange
    };

    private readonly NetworkVariable<Color> playerColor = new NetworkVariable<Color>(
        Color.white,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private SpriteRenderer sprite;

    public override void OnNetworkSpawn()
    {
        sprite = GetComponent<SpriteRenderer>();

        if (IsServer)
        {
            playerColor.Value = PlayerColors[(int)(OwnerClientId % (ulong)PlayerColors.Length)];
        }

        playerColor.OnValueChanged += HandleColorChanged;
        ApplyColor(playerColor.Value);
    }

    public override void OnNetworkDespawn()
    {
        playerColor.OnValueChanged -= HandleColorChanged;
    }

    private void HandleColorChanged(Color previousValue, Color newValue)
    {
        ApplyColor(newValue);
    }

    private void ApplyColor(Color color)
    {
        if (sprite != null)
        {
            sprite.color = color;
        }
    }
}
