using UnityEngine;

// 맵 화면의 스테이지 노드 하나. Button의 OnClick(파라미터 없음)에서 이 컴포넌트의
// NotifyClicked()를 부르면, 여기 저장된 stageIndex로 WorldMapUI.ShowStage를 호출한다.
public class StageNodeButton : MonoBehaviour
{
    public WorldMapUI worldMap;
    public int stageIndex;

    public void NotifyClicked()
    {
        if (worldMap != null)
        {
            worldMap.ShowStage(stageIndex);
        }
    }
}
