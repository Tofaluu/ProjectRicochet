using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ricochet.Editor
{
    /// <summary>
    /// Command-line builds (see Tools/build-web.sh):
    ///   Unity.exe -batchmode -quit -projectPath UnityClient -executeMethod Ricochet.Editor.BuildScripts.WebGL
    /// Output goes to the repo's gitignored Builds/ folder.
    /// </summary>
    public static class BuildScripts
    {
        public static void WebGL()
        {
            PlayerSettings.WebGL.template = "PROJECT:Ricochet";
            // Gzip: seconds to compress (Brotli took minutes for ~7% smaller files). Pre-compressed files
            // need Content-Encoding headers from the web server (deploy/Caddyfile sets them).
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            // IL2CPP "faster (smaller) builds": quicker C++ compile, slightly slower code; plenty for this game.
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            // High stripping: drops unused engine and package code from the download (link.xml keeps what reflection needs).
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
            Build(BuildTarget.WebGL, "../Builds/WebGL");
        }

        private static void Build(BuildTarget target, string path)
        {
            // No "Made with Unity" splash: optional since Unity 6, Personal licence included. The game starts on our
            // own loading page instead.
            PlayerSettings.SplashScreen.show = false;
            // Also leave the Unity logo out: with it on, its 2.7 MB texture ships even though the splash never shows.
            PlayerSettings.SplashScreen.showUnityLogo = false;
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = path,
                target = target,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Build] {target}: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, " +
                      $"{report.summary.totalErrors} errors");
            if (report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
