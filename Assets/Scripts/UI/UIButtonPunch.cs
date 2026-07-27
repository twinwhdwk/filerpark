using UnityEngine;
using UnityEngine.EventSystems;

// 모든 버튼 생성 지점(CreateMenuButton 등)에 공통으로 붙는 눌림 피드백. Button의
// 기본 색상 트랜지션만으로는 "눌렸다"는 확인이 약해 상용 게임 UI 느낌이 안 나서,
// 눌리는 순간 살짝 축소되고 떼면 되돌아오는 스케일 펀치를 얹는다. Awake 시점의
// localScale을 기준(1.0 배)으로 삼으므로, 이미 다른 이유로 스케일된 버튼에도
// 그대로 붙일 수 있다.
public class UIButtonPunch : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private const float PressedScale = 0.92f;
    private const float RecoverSpeed = 18f;

    private Vector3 baseScale;
    private float currentScale = 1f;
    private float targetScale = 1f;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void OnEnable()
    {
        currentScale = 1f;
        targetScale = 1f;
        transform.localScale = baseScale;
    }

    public void OnPointerDown(PointerEventData eventData) => targetScale = PressedScale;
    public void OnPointerUp(PointerEventData eventData) => targetScale = 1f;
    public void OnPointerExit(PointerEventData eventData) => targetScale = 1f;

    private void Update()
    {
        if (Mathf.Approximately(currentScale, targetScale)) return;
        currentScale = Mathf.Lerp(currentScale, targetScale, Time.unscaledDeltaTime * RecoverSpeed);
        transform.localScale = baseScale * currentScale;
    }
}
