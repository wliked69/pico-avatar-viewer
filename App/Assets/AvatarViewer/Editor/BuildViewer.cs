using System;
using System.IO;
using System.Linq;
using AvatarViewer;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

public static class BuildViewer
{
    public static void Prepare()
    {
        string android=Path.Combine(EditorApplication.applicationContentsPath,"PlaybackEngines/AndroidPlayer");
        EditorPrefs.SetBool("JdkUseEmbedded",false); EditorPrefs.SetString("JdkPath",android+"/OpenJDK");
        EditorPrefs.SetBool("AndroidSdkUseEmbedded",false); EditorPrefs.SetString("AndroidSdkRoot",android+"/SDK");
        EditorPrefs.SetBool("AndroidNdkUseEmbedded",false); EditorPrefs.SetString("AndroidNdkRootR23b",android+"/NDK");
        AndroidExternalToolsSettings.jdkRootPath=android+"/OpenJDK";
        AndroidExternalToolsSettings.sdkRootPath=android+"/SDK";
        AndroidExternalToolsSettings.ndkRootPath=android+"/NDK";
        PlayerSettings.companyName="PSP"; PlayerSettings.productName="Avatar Viewer";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,"com.psp.vrcavatarviewer");
        PlayerSettings.bundleVersion="0.3.23"; PlayerSettings.Android.bundleVersionCode=37;
        PlayerSettings.enableFrameTimingStats=true;
        PlayerSettings.allowUnsafeCode=true;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion=AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.OpenGLES3});
        PlayerSettings.colorSpace=ColorSpace.Linear;
        // Avoid the Unity 2022 GLES compute-buffer path that crashes on this headset.
        PlayerSettings.gpuSkinning=false;
        GraphicsSettings.defaultRenderPipeline=null;
        for(int i=0;i<QualitySettings.names.Length;i++) { QualitySettings.SetQualityLevel(i); QualitySettings.renderPipeline=null; }
        QualitySettings.vSyncCount=0; QualitySettings.antiAliasing=2; QualitySettings.shadows=ShadowQuality.Disable;
        var general=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        if(general==null || general.Manager==null) throw new Exception("Official template XR settings missing");
        if(!general.Manager.activeLoaders.Any(x=>x is PXR_Loader) && !XRPackageMetadataStore.AssignLoader(general.Manager,"ByteDance.PICO.XR.PXR_Loader",BuildTargetGroup.Android))
            throw new Exception("Unable to configure PICO XR loader");
        general.InitManagerOnStart=true; EditorUtility.SetDirty(general);
        var picoConfig=PXR_ProjectSetting.GetProjectConfig(); picoConfig.handTracking=true; picoConfig.eyeTracking=true; picoConfig.faceTracking=true; picoConfig.videoSeeThrough=true;
        picoConfig.openMRC=false;
        var xrSettings=AssetDatabase.LoadAssetAtPath<PXR_Settings>("Assets/XR/Settings/PXR_Settings.asset");
        xrSettings.stereoRenderingModeAndroid=PXR_Settings.StereoRenderingModeAndroid.MultiPass; EditorUtility.SetDirty(xrSettings);
        EditorUtility.SetDirty(picoConfig);
        Directory.CreateDirectory("Assets/Scenes");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var origin=new GameObject("XR Origin"); origin.AddComponent<HeadTracking>();
        var cameraObject=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener)); cameraObject.tag="MainCamera"; cameraObject.transform.SetParent(origin.transform);
        var camera=cameraObject.GetComponent<Camera>(); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.clear;
        camera.nearClipPlane=.02f; camera.farClipPlane=20f; camera.stereoTargetEye=StereoTargetEyeMask.Both;
        var driver=cameraObject.AddComponent<TrackedPoseDriver>();
        driver.positionInput=new InputActionProperty(new InputAction("HeadPosition",InputActionType.Value,"<XRHMD>/centerEyePosition",expectedControlType:"Vector3"));
        driver.rotationInput=new InputActionProperty(new InputAction("HeadRotation",InputActionType.Value,"<XRHMD>/centerEyeRotation",expectedControlType:"Quaternion"));
        driver.ignoreTrackingState=true;
        var manager=cameraObject.AddComponent<PXR_Manager>(); manager.eyeTracking=true; manager.openMRC=false;
        manager.trackingMode=XrFacialSimulationModeBD.XR_FACIAL_SIMULATION_MODE_DEFAULT_BD;
        var events=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
        var controller=new GameObject("AvatarViewer").AddComponent<ViewerController>();
        controller.portableShader=Shader.Find("AvatarViewer/PortableToon");
        controller.leftVisual=CreateVirtualHand(origin.transform,"HandLeft");
        controller.rightVisual=CreateVirtualHand(origin.transform,"HandRight");
        controller.uiFont=RequireFont();
        if(controller.uiFont==null) throw new Exception("Chinese UI font missing");
        var light=new GameObject("Key light").AddComponent<Light>(); light.type=LightType.Directional; light.transform.rotation=Quaternion.Euler(35,-30,0); light.intensity=1;
        RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.6f,.65f,.75f);
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/AvatarViewer.unity");
        var productionRoots=scene.GetRootGameObjects();
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/AvatarViewer.unity",true)};
        ValidateViewer.Run();
        foreach(var root in productionRoots) if(root==null || root.scene!=scene) throw new Exception("Validation replaced a production root");
        foreach(var root in scene.GetRootGameObjects()) if(!productionRoots.Contains(root)) UnityEngine.Object.DestroyImmediate(root);
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/AvatarViewer.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("PUBLIC_VIEWER_PREPARED_NO_BUNDLED_AVATAR");
    }
    static Font RequireFont() {
        var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/AvatarViewer/Fonts/ViewerFont.ttf");
        if(font==null)throw new Exception("Place a licensed Chinese TTF at Assets/AvatarViewer/Fonts/ViewerFont.ttf. See README.md.");
        return font;
    }
    static TrackedVirtualHand CreateVirtualHand(Transform parent,string name) {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.bytedance.pico.xr/Resources/Prefabs/"+name+".prefab");
        if(prefab==null) throw new Exception("Official hand mesh missing: "+name);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab); go.name="Virtual "+name; go.transform.SetParent(parent,false);
        var rig=go.GetComponent<PXR_Hand>(); rig.enabled=false;
        var ray=go.transform.Find("RayPose"); if(ray!=null) UnityEngine.Object.DestroyImmediate(ray.gameObject);
        var material=new Material(Shader.Find("Standard")); material.color=new Color(.55f,.8f,.95f);
        string path="Assets/AvatarViewer/Generated/"+name+".mat";
        Directory.CreateDirectory("Assets/AvatarViewer/Generated"); AssetDatabase.CreateAsset(material,path);
        foreach(var renderer in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
            if(renderer.sharedMesh==null) throw new Exception("Virtual hand has no mesh");
            renderer.sharedMaterial=material; renderer.updateWhenOffscreen=true;
        }
        var visual=go.AddComponent<TrackedVirtualHand>(); visual.rig=rig; return visual;
    }
    public static void Build() {
        Prepare();
        Directory.CreateDirectory("../Builds");
        string output=Path.GetFullPath("../Builds/AvatarViewer.apk");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=new[]{"Assets/Scenes/AvatarViewer.unity"},locationPathName=output,
            target=BuildTarget.Android,options=BuildOptions.None
        });
        File.WriteAllText(Path.GetFullPath("../Builds/build-result.json"),JsonUtility.ToJson(new Result {
            result=report.summary.result.ToString(),errors=report.summary.totalErrors,
            warnings=report.summary.totalWarnings,size=report.summary.totalSize
        },true));
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("APK build failed: "+report.summary.result);
        Debug.Log("PUBLIC_VIEWER_BUILD_COMPLETE "+output);
    }
    [Serializable]sealed class Result {public string result;public int errors,warnings;public ulong size;}
}
