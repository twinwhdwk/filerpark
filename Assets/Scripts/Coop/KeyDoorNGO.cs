using UnityEngine;
using Unity.Netcode;

// CoopDoorNGO는 "누른 버튼 개수"로 열림을 판정하지만, 이 문은 "공유 열쇠가 문 앞
// 범위 안에 들어와 있는가"로 판정한다 -- 누가 들고 있는지는 상관없다.
public class KeyDoorNGO : NetworkBehaviour
{
    public CarryableKeyNGO key;
    public Vector2 openRange = new Vector2(1f, 1.5f);

    public readonly NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        isOpen.OnValueChanged += OnDoorStateChanged;
        UpdateDoorVisuals(isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged -= OnDoorStateChanged;
    }

    private void FixedUpdate()
    {
        if (!IsServer || key == null) return;

        Vector2 delta = key.transform.position - transform.position;
        bool keyHere = Mathf.Abs(delta.x) < openRange.x && Mathf.Abs(delta.y) < openRange.y;
        isOpen.Value = keyHere;
    }

    private void OnDoorStateChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[KeyDoor] {gameObject.name} {(newValue ? "열림" : "닫힘")}");
        UpdateDoorVisuals(newValue);
    }

    private void UpdateDoorVisuals(bool state)
    {
        GetComponent<Collider2D>().enabled = !state;
        Color baseColor = UITheme.ColorIce;
        GetComponent<SpriteRenderer>().color = state
            ? new Color(baseColor.r, baseColor.g, baseColor.b, 0.2f)
            : baseColor;
    }
}
