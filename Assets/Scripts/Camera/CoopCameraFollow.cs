using System.Collections.Generic;
using UnityEngine;

// Main Camera에 부착. 네트워크 동기화 대상이 아니다 -- 각 클라이언트가 자기 화면에서
// 로컬로 계산하는 순수 시각 효과이며, 플레이어 위치 자체는 이미 NetworkTransform으로
// 동기화되어 있으므로 이 스크립트는 그 결과만 읽는다.
public class CoopCameraFollow : MonoBehaviour
{
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

    // 흔들림을 매 프레임 transform.position에 직접 더해버리면, 다음 프레임의 SmoothDamp가
    // "흔들려서 어긋난 위치"를 새 시작점으로 삼아 그 오차를 쫓아가려 하면서 흔들림이
    // 끝난 뒤에도 미묘하게 흔들린 채로 남거나 드리프트한다. 그래서 흔들림 없는 "진짜"
    // 추적 위치를 따로 들고, 매 프레임 그 위에 흔들림 오프셋을 얹어서만 실제
    // transform.position에 쓴다.
    private Vector3 currentFollowPosition;

    // 화면 흔들림 -- 지금까지 이 게임의 유일한 피드백은 UI 배너/SFX뿐이었다. 스테이지
    // 클리어처럼 "한 방"이 있어야 할 순간에 화면 자체가 반응하지 않으면 밋밋하게
    // 느껴진다. 순수 로컬 시각 효과라(네트워크 상태에 영향 없음) 각 클라이언트가
    // 원하는 순간 자유롭게 걸 수 있다.
    private float shakeDuration;
    private float shakeTimeRemaining;
    private float shakeMagnitude;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        currentFollowPosition = transform.position;
    }

    public void Shake(float magnitude, float duration)
    {
        shakeMagnitude = magnitude;
        shakeDuration = duration;
        shakeTimeRemaining = duration;
    }

    private void LateUpdate()
    {
        // 매 프레임 GameObject.FindGameObjectsWithTag로 씬을 스캔하며 새 배열을
        // 할당하는 대신, PlayerSetupNGO가 스폰/디스폰 시점에만 갱신하는 공유 목록을
        // 그대로 읽는다(할당 없음) -- 카메라는 이미 SmoothDamp로 위치를 보간하므로
        // 목록이 프레임마다 새로 스캔될 필요가 애초에 없다.
        List<GameObject> players = PlayerSetupNGO.ActivePlayers;
        if (players.Count == 0) return;

        Bounds bounds = new Bounds(players[0].transform.position, Vector3.zero);
        for (int i = 1; i < players.Count; i++)
        {
            bounds.Encapsulate(players[i].transform.position);
        }

        Vector3 targetPosition = new Vector3(bounds.center.x, bounds.center.y, currentFollowPosition.z);
        currentFollowPosition = Vector3.SmoothDamp(currentFollowPosition, targetPosition, ref positionVelocity, positionSmoothTime);

        Vector3 shakeOffset = Vector3.zero;
        if (shakeTimeRemaining > 0f)
        {
            shakeTimeRemaining -= Time.deltaTime;
            // 선형으로 잦아들게 해서 뚝 끊기지 않고 자연스럽게 가라앉는다.
            float falloff = shakeDuration > 0f ? Mathf.Clamp01(shakeTimeRemaining / shakeDuration) : 0f;
            shakeOffset = (Vector3)Random.insideUnitCircle * shakeMagnitude * falloff;
        }

        transform.position = currentFollowPosition + shakeOffset;

        float verticalSize = bounds.size.y / 2f + padding;
        float horizontalSize = (bounds.size.x / 2f + padding) / cam.aspect;
        float targetSize = Mathf.Clamp(Mathf.Max(verticalSize, horizontalSize), minOrthoSize, maxOrthoSize);
        cam.orthographicSize = Mathf.SmoothDamp(cam.orthographicSize, targetSize, ref zoomVelocity, zoomSmoothTime);
    }
}
