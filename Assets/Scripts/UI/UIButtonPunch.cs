using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 모든 버튼 생성 지점(CreateMenuButton 등)에 공통으로 붙는 눌림+호버 피드백. Button의
// 기본 색상 트랜지션만으로는 "눌렸다"는 확인이 약해 상용 게임 UI 느낌이 안 나서,
// 눌리는 순간 살짝 축소되고 떼면 되돌아오는 스케일 펀치를 얹는다. Awake 시점의
// localScale을 기준(1.0 배)으로 삼으므로, 이미 다른 이유로 스케일된 버튼에도
// 그대로 붙일 수 있다.
public class UIButtonPunch : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    private const float PressedScale = 0.92f;
    private const float RecoverSpeed = 18f;
    private const float HighlightRingDistance = 3f;

    private Vector3 baseScale;
    private float currentScale = 1f;
    private float targetScale = 1f;
    private Outline highlightRing;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void OnEnable()
    {
        currentScale = 1f;
        targetScale = 1f;
        transform.localScale = baseScale;
        if (highlightRing != null) highlightRing.enabled = false;
    }

    public void OnPointerDown(PointerEventData eventData) => targetScale = PressedScale;
    public void OnPointerUp(PointerEventData eventData) => targetScale = 1f;

    // color-highlight-ring(#bfffde) -- UI Style Guide가 hover/focus/selected 전용으로
    // 명시한 효과인데, 지금까지 UITheme에 토큰만 정의돼 있고 실제 UI 어디에도 적용된
    // 곳이 없었다. 모든 버튼이 이미 이 컴포넌트를 거치므로 여기 한 곳에 붙이면
    // 게임 전체 버튼에 한 번에 일관되게 적용된다.
    public void OnPointerEnter(PointerEventData eventData)
    {
        EnsureHighlightRing();
        highlightRing.enabled = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetScale = 1f;
        if (highlightRing != null) highlightRing.enabled = false;
    }

    private void EnsureHighlightRing()
    {
        if (highlightRing != null) return;
        highlightRing = gameObject.AddComponent<Outline>();
        highlightRing.effectColor = UITheme.ColorHighlightRing;
        highlightRing.effectDistance = new Vector2(HighlightRingDistance, -HighlightRingDistance);
        highlightRing.useGraphicAlpha = false;
    }

    private void Update()
    {
        if (Mathf.Approximately(currentScale, targetScale)) return;
        currentScale = Mathf.Lerp(currentScale, targetScale, Time.unscaledDeltaTime * RecoverSpeed);
        transform.localScale = baseScale * currentScale;
    }
}
