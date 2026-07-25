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

        if (mapPanel != null) mapPanel.SetActive(false);
        stageIntro.Show(catalog.stages[index]);
    }

    public void ReturnToMap()
    {
        if (stageIntro != null) stageIntro.gameObject.SetActive(false);
        if (mapPanel != null) mapPanel.SetActive(true);
    }
}
