using UnityEngine;

// "게임내 맵 화면" -- Pico Park 2의 WORLD 모드처럼 스테이지 노드를 나열해 보여주는
// 선택 화면. 노드 클릭은 StageNodeButton이 받아서 이 컴포넌트의 ShowStage(index)를
// 부른다 (Button.onClick은 인자를 못 받아서, 노드마다 자기 인덱스를 들고 있는
// 작은 컴포넌트를 하나 더 두는 쪽이 UnityEvent<int> 억지로 쓰는 것보다 단순하다).
public class WorldMapUI : MonoBehaviour
{
    public StageCatalog catalog;
    public StageIntroUI stageIntro;
    public GameObject mapPanel;

    public void ShowStage(int index)
    {
        if (catalog == null || catalog.stages == null || index < 0 || index >= catalog.stages.Length)
        {
            Debug.LogWarning($"[WorldMap] 잘못된 스테이지 인덱스: {index}");
            return;
        }
        if (stageIntro == null)
        {
            Debug.LogWarning("[WorldMap] stageIntro가 연결되어 있지 않습니다.");
            return;
        }

        PlayClick();
        if (mapPanel != null) mapPanel.SetActive(false);
        stageIntro.Show(catalog.stages[index]);
    }

    public void ReturnToMap()
    {
        PlayClick();
        if (stageIntro != null) stageIntro.gameObject.SetActive(false);
        if (mapPanel != null) mapPanel.SetActive(true);
    }

    // 이 컴포넌트가 붙은 오브젝트가 맵/인트로 전체를 감싸는 스크림이다 -- Open/Close는
    // 그 스크림 자체를 켜고 끄고, ReturnToMap/ShowStage는 스크림이 이미 열려 있다고
    // 가정하고 그 안의 맵 카드/인트로 카드끼리만 전환한다.
    public void OpenMap()
    {
        PlayClick();
        gameObject.SetActive(true);
        if (mapPanel != null) mapPanel.SetActive(true);
        if (stageIntro != null) stageIntro.gameObject.SetActive(false);
    }

    public void CloseMap()
    {
        PlayClick();
        gameObject.SetActive(false);
    }

    // 일시정지 메뉴(PauseMenuUI.PlayClick)와 동일한 패턴 -- 이 화면의 모든 버튼이
    // 눌릴 때 공통으로 거친다. 지금까지 월드맵/스테이지 인트로만 다른 메뉴들과 달리
    // 클릭 사운드가 전혀 없었다(구현 당시 누락).
    private static void PlayClick() => AudioManager.Instance?.PlaySfx(SfxId.UIClick);
}
