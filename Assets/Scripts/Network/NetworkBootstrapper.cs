using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class NetworkBootstrapper : MonoBehaviour
{
    [Header("UI 연결")]
    public GameObject startMenuUI;

    void Start()
    {
        // startMenuUI를 감추는 걸 ConnectToServer() 호출 한 곳에만 맡기면, 그 메서드를
        // 거치지 않는 경로(Tools/Coop Setup/Debug: Start Host처럼 StartHost()를 직접
        // 부르는 로컬 테스트용 단축 경로)에서는 접속 화면이 로비 위에 계속 겹쳐
        // 남는다 -- 실제로 겪은 문제(로컬 Start Host 테스트에서 "게임 화면에 전혀
        // 진입이 안 된다"는 리포트, 실제로는 로비가 이미 그 뒤에서 잘 동작 중이었지만
        // 접속 화면이 위에 계속 덮고 있었을 뿐). 실제 배포 클라이언트는 항상
        // ConnectToServer()를 거치므로 원래도 문제 없었지만, 접속 콜백 기준으로
        // 한 곳에서 처리하면 앞으로 생길 다른 진입 경로에도 안전하다.
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        }

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
        // StartClient()의 반환값을 지금까지 아무도 확인하지 않았다 -- 실패해도(트랜스포트
        // 초기화 실패 등) 조용히 무시되고 접속 화면만 닫혀서, "눌렀는데 아무 일도 안
        // 일어남"으로만 보이는 원인 불명 버그처럼 느껴졌다. 실패는 실패라고 로그로 남긴다.
        // 접속 성공 시 접속 화면을 감추는 건 HandleClientConnected가 맡는다(아래).
        bool started = NetworkManager.Singleton.StartClient();
        if (!started)
        {
            Debug.LogError("[Client] StartClient() 실패 -- 트랜스포트 초기화에 문제가 있을 수 있습니다.");
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || clientId != NetworkManager.Singleton.LocalClientId) return;
        if (startMenuUI != null) startMenuUI.SetActive(false);
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        }
    }
}
