using UnityEngine;

// 스테이지 1개의 메타데이터. Docs/Stages/*.md 설계문서와 1:1 대응 --
// 여기 값을 바꾸면 문서도 같이 갱신해야 한다 (역방향도 마찬가지).
// 게임플레이 씬 자체(버튼/문 배치 등)는 별도이고, 이건 맵/인트로 UI가 보여줄
// "카탈로그 정보"만 담는다.
[CreateAssetMenu(fileName = "StageDefinition", menuName = "Coop/Stage Definition")]
public class StageDefinition : ScriptableObject
{
    [Header("식별 / 순서")]
    public string stageId = "stage-01";
    public int order;

    [Header("표시 텍스트")]
    [Tooltip("Dosis(라틴 전용) 로고체로 표시되는 영문 제목")]
    public string titleEn = "GATEKEEPER";
    [Tooltip("M PLUS 1p로 표시되는 한글 부제")]
    public string subtitleKr = "문지기";
    [TextArea(2, 5)]
    public string descriptionKr = "";

    [Header("설계 메타데이터")]
    public StageTheme theme;
    public int minPlayers = 4;
    public int maxPlayers = 6;

    [Header("연결")]
    [Tooltip("이 스테이지의 실제 게임플레이가 들어있는 씬 이름 (아직 단일 씬 구조라 비워둘 수 있음)")]
    public string sceneName = "";
}
