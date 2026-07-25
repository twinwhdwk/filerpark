using System;
using UnityEngine;

// 이 프로세스가 사람 대신 자동으로 움직이는 "봇 클라이언트"로 실행됐는지 여부를 담는다.
// 커맨드라인 인자 -bot 으로 켜지며, NetworkBootstrapper(자동 접속)와
// BotController(자동 조작)가 둘 다 이 값을 참조한다.
public static class BotProcess
{
    public static bool IsBot { get; private set; }
    public static string ServerAddressOverride { get; private set; }
    public static ushort? ServerPortOverride { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ParseArgs()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-bot")
            {
                IsBot = true;
            }
            else if (args[i] == "-serverip" && i + 1 < args.Length)
            {
                ServerAddressOverride = args[i + 1];
            }
            else if (args[i] == "-serverport" && i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort port))
            {
                ServerPortOverride = port;
            }
        }

        if (IsBot)
        {
            Debug.Log($"[Bot] 이 프로세스는 봇 클라이언트로 실행되었습니다. 접속 대상: {ServerAddressOverride ?? "127.0.0.1"}:{ServerPortOverride ?? 7777}");
        }
    }
}
