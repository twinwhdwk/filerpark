using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;

public class CoopButtonNGO : NetworkBehaviour
{
    [Header("버튼 이벤트 설정")]
    // 인라인 초기화 필수 -- 인스펙터를 거치지 않고 스크립트로 AddComponent할 때
    // (에디터 자동화, 헤드리스 -executeMethod 등) 필드가 null로 남아
    // UnityEventTools.AddPersistentListener가 NullReferenceException을 던진다.
    public UnityEvent OnButtonPress = new UnityEvent();
    public UnityEvent OnButtonRelease = new UnityEvent();

    // 예전엔 int 카운터였다 -- 봇 프로세스가 강제 종료되는 등 콜라이더가 파괴되면서
    // OnTriggerExit2D가 안 불리는 경우(GoalZoneNGO에 있던 것과 동일한 부류의 문제),
    // 카운터가 영영 어긋나 버튼이 계속 "눌린" 상태로 고정될 수 있었다. HashSet으로
    // 바꾸고 매 Enter/Exit마다 파괴된 참조를 정리하면, 같은 콜라이더가 실수로 두 번
    // 겹침 이벤트를 받아도(드문 물리 이벤트 중복) 이중 카운트되지 않는 것도 덤이다.
    private readonly HashSet<Collider2D> playersOnButton = new HashSet<Collider2D>();

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsServer) return;
        if (!collision.CompareTag("Player")) return;

        playersOnButton.RemoveWhere(c => c == null);
        bool wasEmpty = playersOnButton.Count == 0;
        playersOnButton.Add(collision);

        if (wasEmpty)
        {
            Debug.Log($"[CoopButton] {gameObject.name} 눌림");
            OnButtonPress.Invoke();
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (!IsServer) return;
        if (!collision.CompareTag("Player")) return;

        playersOnButton.Remove(collision);
        playersOnButton.RemoveWhere(c => c == null);

        if (playersOnButton.Count == 0)
        {
            Debug.Log($"[CoopButton] {gameObject.name} 떨어짐");
            OnButtonRelease.Invoke();
        }
    }
}
