using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

// NetworkManager 오브젝트에 부착. 이 오브젝트는 NetworkObject로 스폰되지 않으므로
// NetworkBehaviour가 아닌 일반 MonoBehaviour로 작성한다 (NetworkBootstrapper와 같은
// 이유/패턴). GoalZoneNGO의 클리어 상태를 지켜보다가 서버가
// NetworkManager.SceneManager.LoadScene으로 모든 클라이언트를 함께 다음 씬으로
// 데려간다 (클라이언트 각자 SceneManager.LoadScene을 부르면 씬이 갈라지므로 반드시
// 이 경로를 통해야 한다). NetworkManager 컴포넌트의 "Enable Scene Management" 옵션이
// 켜져 있어야 동작한다.
public class StageManagerNGO : MonoBehaviour
{
    [Header("연결")]
    public GoalZoneNGO goalZone;

    [Header("다음 스테이지")]
    public string nextSceneName;
    public float transitionDelay = 2f;

    private void OnEnable()
    {
        if (goalZone != null)
        {
            goalZone.stageCleared.OnValueChanged += HandleStageCleared;
        }
    }

    private void OnDisable()
    {
        if (goalZone != null)
        {
            goalZone.stageCleared.OnValueChanged -= HandleStageCleared;
        }
    }

    private void HandleStageCleared(bool previousValue, bool newValue)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !newValue) return;
        Invoke(nameof(LoadNextStage), transitionDelay);
    }

    private void LoadNextStage()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (string.IsNullOrEmpty(nextSceneName)) return;
        NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
    }
}
