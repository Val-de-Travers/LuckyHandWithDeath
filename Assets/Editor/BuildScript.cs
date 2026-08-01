// Assets/Editor/BuildScript.cs
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Export du jeu en version autonome (Windows / macOS).
/// Utilisable depuis le menu Tools/Build, ou en ligne de commande :
///   Unity.exe -quit -batchmode -projectPath "..." -executeMethod BuildScript.BuildWindows
/// </summary>
public static class BuildScript
{
    const string ProductName = "LHWD";
    const string CompanyName = "Val de Travers";
    const string BundleId    = "com.valdetravers.lhwd";

    static string BuildsRoot => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds");

    [MenuItem("Tools/Build/Windows (64 bits)")]
    public static void BuildWindows()
    {
        Build(BuildTarget.StandaloneWindows64, Path.Combine(BuildsRoot, "Windows", ProductName + ".exe"));
    }

    [MenuItem("Tools/Build/macOS")]
    public static void BuildMac()
    {
        Build(BuildTarget.StandaloneOSX, Path.Combine(BuildsRoot, "macOS", ProductName + ".app"));
    }

    [MenuItem("Tools/Build/Windows + macOS")]
    public static void BuildAll()
    {
        BuildWindows();
        BuildMac();
    }

    static void Build(BuildTarget target, string locationPathName)
    {
        ApplyPlayerSettings();

        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
            throw new Exception("Aucune scene activee dans les Build Settings.");

        var dir = Path.GetDirectoryName(locationPathName);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = locationPathName,
            target = target,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[Build] {target} OK -> {locationPathName} ({summary.totalSize / (1024 * 1024)} Mo, {summary.totalTime.TotalSeconds:F0} s)");
        }
        else
        {
            var message = $"[Build] {target} ECHEC ({summary.result}), {summary.totalErrors} erreur(s).";
            Debug.LogError(message);
            throw new Exception(message);
        }
    }

    /// <summary>Identite et fenetre du jeu exporte.</summary>
    static void ApplyPlayerSettings()
    {
        PlayerSettings.productName = ProductName;
        PlayerSettings.companyName = CompanyName;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);

        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;

        AssetDatabase.SaveAssets();
    }
}
