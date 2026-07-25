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

        // UnityTransport의 ConnectionData.ServerListenAddress를 안 정해주면 Address(퍼블릭 IP)와
        // 무관하게 127.0.0.1(루프백)에만 바인딩된다 -- 그러면 서버 프로세스 자체는 "성공적으로
        // 열렸다"고 보고하지만 외부에서는 아무도 접속할 수 없다. 모든 인터페이스에서 받도록
        // 명시적으로 0.0.0.0으로 바인딩한다.
        UnityTransport serverTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (serverTransport != null)
        {
            serverTransport.ConnectionData.ServerListenAddress = "0.0.0.0";
        }

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
