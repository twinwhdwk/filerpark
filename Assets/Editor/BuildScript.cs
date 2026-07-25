#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 서버/클라이언트 빌드를 GUI 없이(-executeMethod) 커맨드라인에서 실행하기 위한
// 스크립트. Dedicated Server 빌드는 subtarget을 Server로 지정하면 Unity가
// UNITY_SERVER 심볼을 자동으로 정의해준다 (NetworkBootstrapper가 이 심볼로 분기).
public static class BuildScript
{
    private const string ScenePath = "Assets/scense/main.unity";

    [MenuItem("Tools/Coop Setup/Build/Dedicated Server (Linux)")]
    public static void BuildDedicatedServerLinux()
    {
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "ServerBuild/filerpark.x86_64",
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None,
        };

        LogReport(BuildPipeline.BuildPlayer(options));
    }

    [MenuItem("Tools/Coop Setup/Build/Windows Client")]
    public static void BuildWindowsClient()
    {
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
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
