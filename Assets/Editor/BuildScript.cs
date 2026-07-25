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

        LogReport(BuildPipeline.BuildPlayer(options));
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
