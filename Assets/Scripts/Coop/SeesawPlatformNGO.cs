using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// 왼쪽/오른쪽에 선 인원 수 차이가 tiltThreshold를 넘으면 기울어지는 다리.
// 좌우 판정은 자식 오브젝트에 붙는 SeesawSideZone이 대신 세어서 보고한다 -- 부모
// 하나에 트리거 콜라이더 두 개를 붙이면 OnTrigger 콜백에서 어느 쪽이 반응했는지
// 구분할 수 없기 때문에 좌/우를 별도 오브젝트로 분리했다.
public class SeesawPlatformNGO : NetworkBehaviour
{
    [Header("설정")]
    public float tiltThreshold = 1.5f;
    public float tiltAngle = 35f;
    public float tiltDegreesPerSecond = 90f;

    [Header("봇이 대기할 좌/우 지점 (StageAuto가 참조)")]
    public Transform leftHoldPoint;
    public Transform rightHoldPoint;

    public readonly NetworkVariable<bool> isLevel = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly HashSet<Collider2D> leftPlayers = new HashSet<Collider2D>();
    private readonly HashSet<Collider2D> rightPlayers = new HashSet<Collider2D>();

    public void ReportEnter(bool isLeftSide, Collider2D collider)
    {
        if (!IsServer) return;
        (isLeftSide ? leftPlayers : rightPlayers).Add(collider);
    }

    public void ReportExit(bool isLeftSide, Collider2D collider)
    {
        if (!IsServer) return;
        (isLeftSide ? leftPlayers : rightPlayers).Remove(collider);
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        leftPlayers.RemoveWhere(c => c == null);
        rightPlayers.RemoveWhere(c => c == null);

        int diff = leftPlayers.Count - rightPlayers.Count;
        isLevel.Value = Mathf.Abs(diff) <= tiltThreshold;

        float targetZ = isLevel.Value ? 0f : Mathf.Sign(diff) * -tiltAngle;
        float currentZ = transform.eulerAngles.z;
        if (currentZ > 180f) currentZ -= 360f;
        float newZ = Mathf.MoveTowards(currentZ, targetZ, tiltDegreesPerSecond * Time.fixedDeltaTime);
        transform.rotation = Quaternion.Euler(0f, 0f, newZ);
    }
}

// SeesawPlatformNGO의 자식에 붙는 좌/우 판정용 트리거. 부모의 leftHoldPoint/
// rightHoldPoint와는 별개 -- 이건 "지금 그 자리에 서 있는가"를 세고, holdPoint는
// 봇이 "어디로 이동해야 하는가"를 가리키는 목표 좌표일 뿐이다.
public class SeesawSideZone : MonoBehaviour
{
    public SeesawPlatformNGO platform;
    public bool isLeftSide;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (platform != null && collision.CompareTag("Player")) platform.ReportEnter(isLeftSide, collision);
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (platform != null && collision.CompareTag("Player")) platform.ReportExit(isLeftSide, collision);
    }
}
