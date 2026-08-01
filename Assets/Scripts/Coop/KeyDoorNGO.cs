using UnityEngine;
using Unity.Netcode;

// CoopDoorNGO는 "누른 버튼 개수"로 열림을 판정하지만, 이 문은 "공유 열쇠가 문 앞
// 범위 안에 들어와 있는가"로 판정한다 -- 누가 들고 있는지는 상관없다.
public class KeyDoorNGO : NetworkBehaviour
{
    public CarryableKeyNGO key;
    public Vector2 openRange = new Vector2(1f, 1.5f);

    // 히스테리시스 여유분: 이미 열린 상태에선 openRange보다 이만큼 더 넓게까지
    // "아직 열쇠가 근처"로 쳐준다. 운반자가 문 앞(doorX-0.6)에 서서 열어두고 대기하는
    // 동안, 뒤따라오는 비운반자들이 Player 레이어 자체 충돌로 옆에서 부딪혀 운반자를
    // openRange 경계 너머로 살짝 밀어내면 isOpen이 그 순간 false로 뒤집힌다. 그러면
    // (닫힘 판정을 보는) 대기 중인 비운반자들이 "문이 닫혔다"고 즉시 되돌아와 문 앞
    // 대기 지점으로 다시 몰려들어 운반자를 더 떠밀고, isOpen이 다시 열렸다 닫혔다를
    // 반복하는 자기강화 루프가 된다(실측: 라이브 서버에서 8분 넘게 매 틱 열림/닫힘이
    // 정확히 짝수로 반복되며 정체 -- 예외도 없고 진행도 없었다). 열릴 때는 기존처럼
    // 좁은 openRange만 인정하고, 이미 열린 뒤에는 이 여유분만큼 넓은 범위를 벗어나야만
    // 다시 닫히게 해서 경계 부근의 미세한 밀림으로는 안 흔들리게 한다.
    public Vector2 closeHysteresis = new Vector2(0.5f, 0.5f);

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

    private float nextTraceLogTime;

    private void FixedUpdate()
    {
        if (!IsServer || key == null) return;

        Vector2 delta = key.transform.position - transform.position;
        Vector2 threshold = isOpen.Value ? openRange + closeHysteresis : openRange;
        bool keyHere = Mathf.Abs(delta.x) < threshold.x && Mathf.Abs(delta.y) < threshold.y;
        isOpen.Value = keyHere;

        // 열림/닫힘 전환 순간의 스냅샷만으로는 "천천히 진동하는지 급격히 왕복하는지"를
        // 구분하기 어려워서, 열쇠가 실제로 운반 중인 동안엔 매 1초 궤적도 남긴다.
        if (key.carrierClientId.Value != CarryableKeyNGO.NoCarrier && Time.time >= nextTraceLogTime)
        {
            nextTraceLogTime = Time.time + 1f;
            Debug.Log($"[KeyDoor][trace] delta=({delta.x:F2},{delta.y:F2}) isOpen={isOpen.Value} carrier={key.carrierClientId.Value}");
        }
    }

    private void OnDoorStateChanged(bool previousValue, bool newValue)
    {
        // 서버 쪽에서만 열쇠 위치/캐리어 정보를 같이 남긴다 -- 히스테리시스를 넓혔는데도
        // 여전히 반복 깜빡이는 원인이 "경계 부근 미세한 밀림"인지 "운반자가 실제로 문에서
        // 꽤 멀어졌다가 돌아오는 큰 폭 왕복"인지 열림/닫힘 로그만으로는 구분이 안 됐다.
        if (IsServer && key != null)
        {
            Vector2 delta = key.transform.position - transform.position;
            Debug.Log($"[KeyDoor] {gameObject.name} {(newValue ? "열림" : "닫힘")} delta=({delta.x:F2},{delta.y:F2}) carrier={key.carrierClientId.Value} keyPos=({key.transform.position.x:F2},{key.transform.position.y:F2})");
        }
        else
        {
            Debug.Log($"[KeyDoor] {gameObject.name} {(newValue ? "열림" : "닫힘")}");
        }
        UpdateDoorVisuals(newValue);
        AudioManager.Instance?.PlaySfx(newValue ? SfxId.DoorOpen : SfxId.DoorClose);
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
