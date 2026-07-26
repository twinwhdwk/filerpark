using UnityEngine;
using UnityEngine.UI;

// "게임 스테이지 화면" -- 맵에서 노드를 클릭하면 뜨는 상세 카드. StageDefinition의
// 내용을 보여주고, "시작" 버튼은 GameFlowManager에 이 스테이지를 다음으로 틀어달라고
// 요청만 한다 -- 실제 씬 전환은 여전히 로비 카운트다운이 끝나는 시점에
// GameFlowManager.StartNextStage()가 전담한다.
public class StageIntroUI : MonoBehaviour
{
    public Text titleText;
    public Text subtitleText;
    public Text descriptionText;
    public Text themeTagText;
    public Text playerCountText;
    public WorldMapUI worldMap;

    private StageDefinition currentStage;

    public void Show(StageDefinition stage)
    {
        if (stage == null)
        {
            Debug.LogWarning("[StageIntro] StageDefinition이 null입니다.");
            return;
        }

        gameObject.SetActive(true);
        currentStage = stage;

        if (titleText != null) titleText.text = stage.titleEn;
        if (subtitleText != null) subtitleText.text = stage.subtitleKr;
        if (descriptionText != null) descriptionText.text = stage.descriptionKr;
        if (themeTagText != null) themeTagText.text = ThemeLabel(stage.theme);
        if (playerCountText != null) playerCountText.text = $"{stage.minPlayers}~{stage.maxPlayers}인";
    }

    private static string ThemeLabel(StageTheme theme)
    {
        switch (theme)
        {
            case StageTheme.SimultaneousSwitches: return "동시 입력";
            case StageTheme.StackAndPush: return "스택 & 푸시";
            case StageTheme.CarryAndBalance: return "운반 & 균형";
            case StageTheme.SharedSurvival: return "공동 생존";
            default: return "";
        }
    }

    // 아무도 이 버튼을 안 눌러도 로비는 예전처럼 순서대로 자동 진행된다 -- 이 요청은
    // "다음 한 번만" 그 순서 위에 우선하는 제안일 뿐이라, 이미 검증된 자동 순환
    // 흐름을 전혀 대체하지 않는다.
    public void OnStartPressed()
    {
        if (currentStage != null && GameFlowManager.Instance != null && !string.IsNullOrEmpty(currentStage.sceneName))
        {
            GameFlowManager.Instance.RequestSelectStageServerRpc(currentStage.sceneName);
        }

        if (worldMap != null) worldMap.CloseMap();
    }
}
