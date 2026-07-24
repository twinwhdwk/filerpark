using UnityEngine;

// Main Camera에 부착. 네트워크 동기화 대상이 아니다 -- 각 클라이언트가 자기 화면에서
// 로컬로 계산하는 순수 시각 효과이며, 플레이어 위치 자체는 이미 NetworkTransform으로
// 동기화되어 있으므로 이 스크립트는 그 결과만 읽는다.
public class CoopCameraFollow : MonoBehaviour
{
    [Header("추적 대상")]
    public string playerTag = "Player";

    [Header("줌 설정")]
    public float minOrthoSize = 3f;
    public float maxOrthoSize = 12f;
    public float padding = 2f;

    [Header("추적 부드러움")]
    public float positionSmoothTime = 0.2f;
    public float zoomSmoothTime = 0.3f;

    private Camera cam;
    private Vector3 positionVelocity;
    private float zoomVelocity;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag(playerTag);
        if (players.Length == 0) return;

        Bounds bounds = new Bounds(players[0].transform.position, Vector3.zero);
        for (int i = 1; i < players.Length; i++)
        {
            bounds.Encapsulate(players[i].transform.position);
        }

        Vector3 targetPosition = new Vector3(bounds.center.x, bounds.center.y, transform.position.z);
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref positionVelocity, positionSmoothTime);

        float verticalSize = bounds.size.y / 2f + padding;
        float horizontalSize = (bounds.size.x / 2f + padding) / cam.aspect;
        float targetSize = Mathf.Clamp(Mathf.Max(verticalSize, horizontalSize), minOrthoSize, maxOrthoSize);
        cam.orthographicSize = Mathf.SmoothDamp(cam.orthographicSize, targetSize, ref zoomVelocity, zoomSmoothTime);
    }
}
