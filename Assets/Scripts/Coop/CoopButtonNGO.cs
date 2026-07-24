using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;

public class CoopButtonNGO : NetworkBehaviour
{
    [Header("버튼 이벤트 설정")]
    public UnityEvent OnButtonPress;
    public UnityEvent OnButtonRelease;

    private int playersOnButton = 0;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsServer) return;

        if (collision.CompareTag("Player"))
        {
            playersOnButton++;
            if (playersOnButton == 1)
            {
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
                OnButtonRelease.Invoke();
            }
        }
    }
}
