using UnityEngine;
using UnityEngine.UI;

// "게임 스테이지 화면" -- 맵에서 노드를 클릭하면 뜨는 상세 카드. StageDefinition의
// 내용을 그대로 보여주기만 하는 뷰이고, 실제 게임플레이 로딩은 아직 이 프로젝트에
// 없다 (Docs/Stages는 설계 문서 단계) -- OnStartPressed는 그 자리를 표시하는 TODO다.
public class StageIntroUI : MonoBehaviour
{
    public Text titleText;
    public Text subtitleText;
    public Text descriptionText;
    public Text themeTagText;
    public Text playerCountText;

    public void Show(StageDefinition stage)
    {
        if (stage == null)
        {
            Debug.LogWarning("[StageIntro] StageDefinition이 null입니다.");
            return;
        }

        gameObject.SetActive(true);

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

    // TODO: 실제 스테이지 게임플레이 로딩 -- Docs/Stages 설계 문서 구현 단계에서 연결.
    public void OnStartPressed()
    {
        string selected = titleText != null ? titleText.text : "(제목 미연결)";
        Debug.Log($"[StageIntro] 스테이지 로딩 미구현 (설계 단계) -- 선택됨: {selected}");
    }
}
