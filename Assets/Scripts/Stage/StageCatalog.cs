using UnityEngine;

// 맵 화면이 순서대로 그려줄 스테이지 목록. NetworkSetupMenu가 4개 미션 데이터로
// 채워 넣은 애셋 하나(Assets/StageData/StageCatalog.asset)를 WorldMapUI가 참조한다.
[CreateAssetMenu(fileName = "StageCatalog", menuName = "Coop/Stage Catalog")]
public class StageCatalog : ScriptableObject
{
    public StageDefinition[] stages;
}
