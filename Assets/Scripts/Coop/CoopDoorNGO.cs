using UnityEngine;
using Unity.Netcode;

public class CoopDoorNGO : NetworkBehaviour
{
    [Header("문 설정")]
    public int requiredButtons = 2;
    private int currentPressed = 0;

    public NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        isOpen.OnValueChanged += OnDoorStateChanged;
        UpdateDoorVisuals(isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged -= OnDoorStateChanged;
    }

    public void AddPress()
    {
        if (!IsServer) return;
        currentPressed++;
        CheckDoorState();
    }

    public void RemovePress()
    {
        if (!IsServer) return;
        currentPressed--;
        CheckDoorState();
    }

    private void CheckDoorState()
    {
        isOpen.Value = (currentPressed >= requiredButtons);
    }

    private void OnDoorStateChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[CoopDoor] {gameObject.name} {(newValue ? "열림" : "닫힘")}");
        UpdateDoorVisuals(newValue);
        AudioManager.Instance?.PlaySfx(newValue ? SfxId.DoorOpen : SfxId.DoorClose);
    }

    // color-ice (#4dd6fa) -- CLAUDE.md UI Style Guide 색상 토큰. 닫힌 문은 불투명하게,
    // 열린 문은 같은 색을 낮은 알파로 남겨 "여기 문이 있었다"는 흔적만 비친다.
    private static readonly Color ClosedColor = new Color(0.302f, 0.839f, 0.980f, 1f);
    private static readonly Color OpenColor = new Color(0.302f, 0.839f, 0.980f, 0.2f);

    private void UpdateDoorVisuals(bool state)
    {
        GetComponent<Collider2D>().enabled = !state;
        GetComponent<SpriteRenderer>().color = state ? OpenColor : ClosedColor;
    }
}
