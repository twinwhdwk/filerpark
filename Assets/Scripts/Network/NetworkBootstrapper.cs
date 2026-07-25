using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

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

        // 봇 시뮬레이션: -bot 인자로 실행된 클라이언트는 사람의 클릭을 기다리지 않고
        // 곧바로 접속한다. 접속 주소는 -serverip/-serverport로 지정하며, 지정이
        // 없으면 같은 컴퓨터의 Editor Play 모드(호스트)를 향해 127.0.0.1로 접속한다.
        if (BotProcess.IsBot)
        {
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.ConnectionData.Address = BotProcess.ServerAddressOverride ?? "127.0.0.1";
                if (BotProcess.ServerPortOverride.HasValue)
                {
                    transport.ConnectionData.Port = BotProcess.ServerPortOverride.Value;
                }
            }

            ConnectToServer();
        }
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
