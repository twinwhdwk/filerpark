using UnityEngine;
using Unity.Netcode;

public class NetworkBootstrapper : MonoBehaviour
{
    [Header("UI 연결")]
    public GameObject startMenuUI;

    void Start()
    {
#if UNITY_SERVER
        Debug.Log("[Server] 데디케이티드 서버 모드로 실행됩니다. 포트 개방 대기 중...");

        if (NetworkManager.Singleton.StartServer())
        {
            Debug.Log("[Server] 서버가 성공적으로 열렸습니다!");
        }
        else
        {
            Debug.LogError("[Server] 서버 열기 실패. 포트 충돌이나 설정을 확인하세요.");
        }
#else
        Debug.Log("[Client] 클라이언트 모드입니다. 서버 접속을 대기합니다.");
#endif
    }

    public void ConnectToServer()
    {
        Debug.Log("[Client] 서버에 접속을 시도합니다...");
        NetworkManager.Singleton.StartClient();

        if (startMenuUI != null)
        {
            startMenuUI.SetActive(false);
        }
    }
}
