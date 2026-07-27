using UnityEngine;

// 카드/배너 오브젝트가 SetActive(true)되는 순간 스케일+페이드로 팝인시킨다. 지금까지는
// 클리어 배너/일시정지 카드/월드맵 카드 등이 전부 즉시 나타나(SetActive만 토글) 상용
// 게임 UI 특유의 "등장감"이 없었다 -- 이 컴포넌트를 씬 생성 코드에서 카드 오브젝트에
// 한 번 붙여두면, 기존 SetActive(true) 호출부(StageClearUI/PauseMenuUI/WorldMapUI/
// ConnectionStatusUI)는 전혀 손댈 필요 없이 OnEnable이 트리거가 되어 동작한다.
[RequireComponent(typeof(CanvasGroup))]
public class UIPunchIn : MonoBehaviour
{
    private const float Duration = 0.16f;
    private const float StartScale = 0.85f;

    private CanvasGroup canvasGroup;
    private float startTime;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        startTime = Time.unscaledTime;
        transform.localScale = Vector3.one * StartScale;
        if (canvasGroup != null) canvasGroup.alpha = 0f;
    }

    private void Update()
    {
        float t = Mathf.Clamp01((Time.unscaledTime - startTime) / Duration);
        float eased = 1f - Mathf.Pow(1f - t, 3f); // ease-out cubic
        transform.localScale = Vector3.one * Mathf.Lerp(StartScale, 1f, eased);
        if (canvasGroup != null) canvasGroup.alpha = eased;
        if (t >= 1f) enabled = false;
    }
}
