using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// InputField에 직접 붙는 포커스 링 -- UIButtonPunch의 호버 링과 같은 토큰
// (color-highlight-ring)을 선택 상태에 적용한다. Unity의 기본 이벤트 시스템은
// ISelectHandler/IDeselectHandler를 이벤트를 실제로 받은 GameObject에서만
// 호출하므로(부모로 자동 버블링되지 않음), InputField 오브젝트 자신에 붙여야 한다.
public class UIInputFieldFocusRing : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    private const float RingDistance = 3f;

    private Outline ring;

    public void OnSelect(BaseEventData eventData)
    {
        if (ring == null)
        {
            ring = gameObject.AddComponent<Outline>();
            ring.effectColor = UITheme.ColorHighlightRing;
            ring.effectDistance = new Vector2(RingDistance, -RingDistance);
            ring.useGraphicAlpha = false;
        }
        ring.enabled = true;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        if (ring != null) ring.enabled = false;
    }
}
