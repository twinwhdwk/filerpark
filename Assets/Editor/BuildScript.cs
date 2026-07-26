#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 서버/클라이언트 빌드를 GUI 없이(-executeMethod) 커맨드라인에서 실행하기 위한
// 스크립트. Dedicated Server 빌드는 subtarget을 Server로 지정하면 Unity가
// UNITY_SERVER 심볼을 자동으로 정의해준다 (NetworkBootstrapper가 이 심볼로 분기).
//
// 씬 목록은 하드코딩하지 않고 Build Settings(EditorBuildSettings.scenes)에서 그대로
// 읽는다 -- GameFlowSceneSetup.ConfigureBuildSettingsScenes()가 그 목록의 유일한
// 소유자이므로, 스테이지를 추가/제거해도 이 파일은 손댈 필요가 없다.
public static class BuildScript
{
    private static string[] EnabledScenePaths()
    {
        return EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
    }

    [MenuItem("Tools/Coop Setup/Build/Dedicated Server (Linux)")]
    public static void BuildDedicatedServerLinux()
    {
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = EnabledScenePaths(),
            locationPathName = "ServerBuild/filerpark.x86_64",
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None,
        };

        BuildAndRestoreSubtarget(options);
    }

    // 실제 배포 대상(GCP)은 Linux지만, 로컬에서 네트워킹을 반복 검증할 때마다 WSL/
    // 클라우드 배포까지 거치는 건 느리다 -- Windows 개발 머신에서 그대로 실행 가능한
    // Dedicated Server 빌드를 하나 더 둬서, Editor Play 모드(GUI 필요)에 기대지 않고도
    // 순수 커맨드라인만으로 서버+봇 클라이언트를 띄워 검증할 수 있게 한다.
    [MenuItem("Tools/Coop Setup/Build/Dedicated Server (Windows, local testing)")]
    public static void BuildDedicatedServerWindows()
    {
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = EnabledScenePaths(),
            locationPathName = "ServerBuildWindows/filerpark_server.exe",
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None,
        };

        BuildAndRestoreSubtarget(options);
    }

    // BuildPipeline.BuildPlayer(subtarget: Server)는 성공하든 실패하든(모듈 미설치로
    // 즉시 실패하는 경우 포함) EditorUserBuildSettings.standaloneBuildSubtarget을
    // Server로 남겨버린다 -- 그 뒤로 Editor에서 Play 모드에 들어가는 모든 세션이
    // (사람이 직접 하는 테스트든, 헤드리스 스모크 테스트든) UNITY_SERVER 심볼로
    // 컴파일되어 NetworkBootstrapper가 자동으로 StartServer()만 부르고 아무 것도
    // 스폰되지 않는다 -- "빌드는 실패했는데 왜 Play 모드가 이상하게 동작하지"로만
    // 보이는, 원인 추적이 매우 까다로운 상태 누수다(실제로 겪음). 서버 빌드를 시도한
    // 직후에는 항상 Player로 되돌려서, 평소 Editor Play 테스트는 항상 클라이언트처럼
    // 동작하도록 보장한다.
    private static void BuildAndRestoreSubtarget(BuildPlayerOptions options)
    {
        try
        {
            LogReport(BuildPipeline.BuildPlayer(options));
        }
        finally
        {
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
        }
    }

    [MenuItem("Tools/Coop Setup/Build/Windows Client")]
    public static void BuildWindowsClient()
    {
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = EnabledScenePaths(),
            locationPathName = "ClientBuild/filerpark.exe",
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            options = BuildOptions.None,
        };

        LogReport(BuildPipeline.BuildPlayer(options));
    }

    private static void LogReport(BuildReport report)
    {
        BuildSummary summary = report.summary;
        Debug.Log($"[Build] result={summary.result} errors={summary.totalErrors} warnings={summary.totalWarnings} " +
            $"size={summary.totalSize} time={summary.totalTime} output={summary.outputPath}");
    }
}
#endif
