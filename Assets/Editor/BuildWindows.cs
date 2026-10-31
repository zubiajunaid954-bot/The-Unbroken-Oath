using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Builds the Windows game into D:\GAMECON_GAME\build (GAMECON > Build Windows Game).</summary>
public static class BuildWindows
{
    const string OutDir = "build";
    const string ExeName = "TheUnbrokenOath.exe";

    [MenuItem("GAMECON/Build Windows Game (build folder)")]
    public static void BuildFromMenu() { EditorUtility.DisplayDialog("Build", Build(), "OK"); }

    public static string Build()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();
        if (scenes.Length == 0) return "No scenes in the Build Settings.";
        Directory.CreateDirectory(OutDir);
        PlayerSettings.productName = "The Unbroken Oath";
        PlayerSettings.runInBackground = true;
        PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.resizableWindow = true;
        var opts = new BuildPlayerOptions { scenes = scenes, locationPathName = Path.Combine(OutDir, ExeName), target = BuildTarget.StandaloneWindows64, options = BuildOptions.None };
        BuildReport rep = BuildPipeline.BuildPlayer(opts);
        var s2 = rep.summary;
        string msg = "Build " + s2.result + ": " + (s2.result == BuildResult.Succeeded ? Path.GetFullPath(opts.locationPathName) + "  (" + (s2.totalSize / 1048576) + " MB, " + s2.totalTime.TotalSeconds.ToString("0") + " s)" : s2.totalErrors + " errors");
        foreach (var step in rep.steps) foreach (var m in step.messages) if (m.type == LogType.Error || m.type == LogType.Exception) msg += "\n" + m.content;
        return msg;
    }
}
