using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// Lobby<->Stage 전환은 Unload -> (모든 클라이언트 완료 대기) -> Load 순으로 진행된다
// (GameFlowManager.cs 헤더 주석 참고) -- 그 사이 화면에는 아무 안내도 없어서, 느린
// 회선의 클라이언트 입장에서는 게임이 멈춘 것처럼 보인다. 이 오버레이는 NGO의
// SceneEvent를 직접 구독해서(GameFlowManager를 건드리지 않고) Unload/Load 시작부터
// 새 씬의 Load가 실제로 끝날 때까지 계속 떠 있는다.
//
// 일부러 Unload의 완료 이벤트에서는 숨기지 않는다 -- Unload가 끝난 뒤 서버가 전원의
// Unload 완료를 기다렸다가 다음 Load를 시작하기까지 실제로 공백이 있어서, 거기서
// 한 번 숨겼다 다시 띄우면 오히려 깜빡임만 생긴다. Load가 실제로 끝나는 시점 하나만
// "완료" 신호로 취급한다.
public class LoadingScreenUI : MonoBehaviour
{
    public GameObject overlayPanel;
    public Text messageText;

    // 무슨 이유로든(늦게 접속한 클라이언트가 씬 이벤트 일부를 못 받는 경우 등)
    // 완료 이벤트를 놓치면, 오버레이가 영원히 화면을 가리는 것보다는 일정 시간 후
    // 스스로 사라지는 편이 훨씬 안전하다 -- 화면을 영구히 가리는 소프트락은 절대
    // 없어야 한다.
    private const float SafetyTimeoutSeconds = 10f;
    private const float DotIntervalSeconds = 0.35f;

    private float shownAtTime = -1f;
    private float nextDotUpdateTime;
    private int dotCount;

    private void OnEnable()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.SceneManager.OnSceneEvent += HandleSceneEvent;
        SetOverlayActive(false);
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SceneManager == null) return;
        NetworkManager.Singleton.SceneManager.OnSceneEvent -= HandleSceneEvent;
    }

    private void Update()
    {
        if (overlayPanel == null || !overlayPanel.activeSelf) return;

        if (shownAtTime > 0f && Time.unscaledTime - shownAtTime > SafetyTimeoutSeconds)
        {
            SetOverlayActive(false);
            return;
        }

        if (Time.unscaledTime >= nextDotUpdateTime)
        {
            nextDotUpdateTime = Time.unscaledTime + DotIntervalSeconds;
            dotCount = (dotCount + 1) % 4;
            if (messageText != null) messageText.text = "로딩 중" + new string('.', dotCount);
        }
    }

    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.Load:
            case SceneEventType.Unload:
                SetOverlayActive(true);
                break;
            case SceneEventType.LoadComplete:
            case SceneEventType.LoadEventCompleted:
                SetOverlayActive(false);
                break;
        }
    }

    private void SetOverlayActive(bool active)
    {
        if (overlayPanel == null) return;
        overlayPanel.SetActive(active);
        shownAtTime = active ? Time.unscaledTime : -1f;
        if (active)
        {
            dotCount = 0;
            nextDotUpdateTime = Time.unscaledTime + DotIntervalSeconds;
            if (messageText != null) messageText.text = "로딩 중";
        }
    }
}
