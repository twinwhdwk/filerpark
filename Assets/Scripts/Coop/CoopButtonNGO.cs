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

    private int playersOnButton = 0;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsServer) return;

        if (collision.CompareTag("Player"))
        {
            playersOnButton++;
            if (playersOnButton == 1)
            {
                Debug.Log($"[CoopButton] {gameObject.name} 눌림");
                OnButtonPress.Invoke();
            }
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (!IsServer) return;

        if (collision.CompareTag("Player"))
        {
            playersOnButton--;
            if (playersOnButton <= 0)
            {
                playersOnButton = 0;
                Debug.Log($"[CoopButton] {gameObject.name} 떨어짐");
                OnButtonRelease.Invoke();
            }
        }
    }
}
