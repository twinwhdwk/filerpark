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
        UpdateDoorVisuals(newValue);
    }

    private void UpdateDoorVisuals(bool state)
    {
        GetComponent<Collider2D>().enabled = !state;
        GetComponent<SpriteRenderer>().color = state ? new Color(1, 1, 1, 0.2f) : new Color(1, 1, 1, 1f);
    }
}
