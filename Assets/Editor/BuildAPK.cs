using UnityEditor;
using UnityEngine;

public class BuildAPK
{
    [MenuItem("Build/Build Android APK")]
    public static void BuildAndroid()
    {
        string[] scenes = { "Assets/Scenes/SampleScene.unity" };
        string buildPath = "Builds/VolumetricCameraBlend.apk";
        
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = scenes;
        buildPlayerOptions.locationPathName = buildPath;
        buildPlayerOptions.target = BuildTarget.Android;
        buildPlayerOptions.options = BuildOptions.None;
        
        BuildPipeline.BuildPlayer(buildPlayerOptions);
        Debug.Log($"Build completed: {buildPath}");
    }
}
