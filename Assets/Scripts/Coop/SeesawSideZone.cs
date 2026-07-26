using UnityEngine;

// SeesawPlatformNGO의 자식에 붙는 좌/우 판정용 트리거. 부모의 leftHoldPoint/
// rightHoldPoint와는 별개 -- 이건 "지금 그 자리에 서 있는가"를 세고, holdPoint는
// 봇이 "어디로 이동해야 하는가"를 가리키는 목표 좌표일 뿐이다.
//
// 원래 SeesawPlatformNGO.cs 안에 두 번째 클래스로 같이 있었는데(파일명과 클래스명
// 불일치), Stage03 씬 빌드가 헤드리스로 계속 손상되는 문제의 실제 원인이었다 --
// GameFlowSceneSetup.ValidateStage03Scene()으로 씬을 열어 직접 스캔해보니
// SeesawLeftZone 오브젝트의 컴포넌트가 "Missing Script"(null)로 나왔다: Library를
// 통째로 지우고 재임포트해도 매번 재현됐던 걸 보면, 도메인 리로드/재컴파일 직후
// 헤드리스로 곧바로 AddComponent<SeesawSideZone>()을 호출하는 시점에 파일명과
// 다른 보조 클래스가 아직 타입 등록이 덜 된 상태였던 것으로 보인다. Unity는 파일당
// 클래스 하나(파일명 일치)를 강하게 권장하는데 이 프로젝트에서 그 관례를 어긴
// 유일한 클래스였다. 별도 파일로 분리해 이 타이밍 문제를 근본적으로 없앤다.
public class SeesawSideZone : MonoBehaviour
{
    public SeesawPlatformNGO platform;
    public bool isLeftSide;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (platform != null && collision.CompareTag("Player")) platform.ReportEnter(isLeftSide, collision);
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (platform != null && collision.CompareTag("Player")) platform.ReportExit(isLeftSide, collision);
    }
}
