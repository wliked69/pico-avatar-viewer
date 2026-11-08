using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvatarViewer;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using InputSystem = UnityEngine.InputSystem.InputSystem;
using UnityEngine.InputSystem.Controls;
using InputDevice = UnityEngine.XR.InputDevice;
using UnityEngine.UI;
using UnityEngine.XR;

public sealed class ViewerController : MonoBehaviour
{
    public Font uiFont;
    public Shader portableShader;
    public TrackedVirtualHand leftVisual,rightVisual;
    Camera viewCamera;
    string statusText="正在准备模型…",modelTitle="尚未选择模型",trackingSummary="等待追踪数据";
    WheelSummonGesture summonGesture;
    WheelSummonIndicator summonIndicator;
    Transform stage;
    GameObject avatar;
    AssetBundle bundle;
    string loadedPath;
    ViewerWheelOptions.ModelFile[] libraryFiles=new ViewerWheelOptions.ModelFile[0];

    PointerEventData pointer;

    LineRenderer laser;
    GameObject cursor;
    bool loading, bones=true, turning, previousTrigger, shaking;
    bool pickerPending;
    readonly ModelHandManipulation manipulation=new ModelHandManipulation();
    HandBoneInteraction handBones;
    AvatarEmbodiment embodiment;
    AvatarExpressionTracking expressions;
    AvatarMirror mirror;
    AvatarRadialMenu modelMenu;
    bool previousMenuButton;
    bool embodied=true;
    string usbRequestId;
    float nextUsbCheck;
    bool previouslyCalibrated;

    string appliedConfig;
    bool rightPinch, leftPinch;
    int pinchEvents, clickEvents;
    HandJointLocations rightJoints, leftJoints;
    Button hoverButton;
    float nextInputReport;

    readonly List<InputDevice> eyeDevices=new List<InputDevice>();
    Vector3 placement = new Vector3(.4f,0,2f);
    float yaw=180, size=1;
    const long MaxFileSize = 512L*1024*1024;
    string ModelsDirectory => Path.Combine(Application.persistentDataPath,"Models");

    IEnumerator Start()
    {
        gameObject.name="AvatarViewer";
        summonGesture=new WheelSummonGesture();
        Directory.CreateDirectory(ModelsDirectory);
        Application.targetFrameRate=90;
        viewCamera=Camera.main;
        viewCamera.clearFlags=CameraClearFlags.SolidColor; viewCamera.backgroundColor=Color.clear;
        #if UNITY_ANDROID && !UNITY_EDITOR
        PXR_Manager.EnableVideoSeeThrough=true;
        #endif
        stage=new GameObject("Avatar stage").transform;
        handBones=new HandBoneInteraction();
        embodiment=gameObject.AddComponent<AvatarEmbodiment>();
        embodiment.portableShader=portableShader;
        expressions=gameObject.AddComponent<AvatarExpressionTracking>();
        mirror=gameObject.AddComponent<AvatarMirror>(); mirror.Initialize(embodiment,viewCamera,uiFont);
        modelMenu=gameObject.AddComponent<AvatarRadialMenu>(); modelMenu.Initialize(viewCamera,uiFont);
        summonIndicator=WheelSummonIndicator.Create(transform,viewCamera);
        modelMenu.ConfigureViewer(()=>mirror.Visible,ToggleMirror,()=>mirror.Reposition(),RecalibrateEmbodiment);
        modelMenu.ConfigureFloor(()=>embodiment.FloorOffset,value=>{embodiment.SetFloorOffset(value); RecalibrateEmbodiment();});
        modelMenu.ConfigureHands(embodiment);
        gameObject.AddComponent<ViewerPerformance>().Initialize(embodiment,mirror);
        gameObject.AddComponent<AvatarPoseCapture>().rig=embodiment;
        ApplyRuntimeConfig();
        ConfigureWheel(); CreatePointer(); RefreshLibrary(); modelMenu.ShowHome();
        appliedConfig=null; ApplyRuntimeConfig();
        RefreshLibrary();
        if(libraryFiles.Length>0) yield return Load(libraryFiles[0].path);
        else SetStatus("尚未导入模型，请使用 Unity 插件上传，或从模型库导入 .avatarbundle 文件。");
    }
    void RefreshLibrary()
    {
        var files=Directory.GetFiles(ModelsDirectory,"*.avatarbundle");
        Array.Sort(files,(a,b)=>File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
        libraryFiles=files.Select(path=>new ViewerWheelOptions.ModelFile {path=path,label=Path.GetFileNameWithoutExtension(path)}).ToArray();
        SetStatus("模型库已扫描 · "+files.Length+" 个模型");
    }
    void ConfigureWheel()
    {
        modelMenu.ConfigureOptions(new ViewerWheelOptions {
            Embodied=()=>embodiment.Active,Bones=()=>bones,Expressions=()=>expressions.TrackingEnabled,
            ToggleEmbodied=ToggleEmbodiment,ToggleBones=()=>{bones=!bones;ApplyBones();},
            ToggleExpressions=()=>{expressions.enabled=true;expressions.SetTracking(!expressions.TrackingEnabled);},
            Import=Import,Scan=RefreshLibrary,Quit=Application.Quit,
            ModelTitle=()=>modelTitle,Status=()=>statusText,Tracking=()=>trackingSummary,
            Models=()=>libraryFiles,LoadModel=path=>{if(!loading && !pickerPending)StartCoroutine(Load(path));},Gesture=summonGesture
        });
    }
    void ToggleMenu() {if(!modelMenu.Visible){manipulation.Cancel();handBones.Release();}modelMenu.Toggle();}
    void Import()
    {
        if(loading || pickerPending) return;
        #if UNITY_ANDROID && !UNITY_EDITOR
        pickerPending=true; SetStatus("请选择电脑导出的 .avatarbundle 模型包");
        try { using(var picker=new AndroidJavaClass("com.psp.avatarviewer.AvatarFilePicker")) picker.CallStatic("open","AvatarViewer","OnFileSelected"); }
        catch(Exception error) { pickerPending=false; SetStatus("无法打开文件选择器："+error.Message); }
        #else
        SetStatus("电脑预览：将 .avatarbundle 放入 "+ModelsDirectory+"，然后重新扫描");
        #endif
    }
    public void OnFileSelected(string result)
    {
        pickerPending=false;
        if(result.StartsWith("ERROR:")) { SetStatus(result.Substring(6)); return; }
        if(!string.IsNullOrEmpty(result)) { RefreshLibrary(); StartCoroutine(Load(result)); }
    }
    IEnumerator Load(string path)
    {
        if(loading) yield break;
        loading=true; SetStatus("正在加载 "+Path.GetFileName(path));
        if(!File.Exists(path) || new FileInfo(path).Length>MaxFileSize) { loading=false; SetStatus("文件不存在或超过 512 MB"); yield break; }
        if(avatar != null && loadedPath==path) { loading=false; SetStatus("模型已经加载"); yield break; }
        // Release the bundle registration before loading another copy with the same internal name.
        // Existing avatar assets stay alive until a replacement has successfully loaded.
        if(bundle != null) { bundle.Unload(false); bundle=null; }
        var operation=AssetBundle.LoadFromFileAsync(path); yield return operation;
        var candidate=operation.assetBundle;
        if(candidate==null) { loading=false; SetStatus("加载失败：需要 Unity 2022.3 导出的 Android 模型包"); yield break; }
        var request=candidate.LoadAllAssetsAsync<GameObject>(); yield return request;
        var prefab=request.allAssets.Length==1?request.allAssets[0] as GameObject:null;
        var info=prefab!=null?prefab.GetComponent<AvatarPackageInfo>():null;
        if(info==null || info.formatVersion!=1 || info.buildPlatform!="Android" || !info.unityVersion.StartsWith("2022.3.")) {
            candidate.Unload(true); loading=false; SetStatus("模型包格式不支持，请使用配套导出工具"); yield break;
        }
        handBones.Release();
        embodiment.Unbind();
        if(avatar!=null) Destroy(avatar);
        if(bundle!=null) bundle.Unload(false);
        bundle=candidate; loadedPath=path; avatar=Instantiate(prefab,stage); avatar.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
        handBones.Bind(avatar.GetComponentsInChildren<SpringChain>(true));
        modelTitle=info.displayName; ApplyBones(); ResetPlacement();
        embodiment.Bind(avatar,stage,viewCamera); expressions.Bind(embodiment); embodiment.SetActive(embodied);
        modelMenu.Bind(avatar);
        embodied=embodiment.Active; 
        modelTitle=info.displayName+(modelMenu.PageCount>0?" · 原菜单 "+modelMenu.PageCount+" 页":" · 无轮盘数据，请重新上传");
        mirror.SetVisible(embodied && mirror.Visible);  mirror.Reposition();
        SetStatus("已加载 · 动骨 "+info.convertedChains+" 组 · "+expressions.ExpressionMode+" · "+(expressions.VrcParameters>0?expressions.VrcParameters:expressions.MappedShapes)+" 项\n"+embodiment.RigStatus);
        loading=false;
        yield return null;
        yield return Resources.UnloadUnusedAssets();
        // The headset owns the XR swapchain. Do not read it back through ScreenCapture.
    }
    void ApplyBones() { if(avatar!=null) foreach(var chain in avatar.GetComponentsInChildren<SpringChain>(true)) { chain.simulate=bones; chain.ResetSimulation(); } }
    void ResetPlacement() { manipulation.Cancel(); placement=new Vector3(.4f,0,2f); yaw=180; size=1; turning=shaking=false; ApplyBones(); }
    void ToggleEmbodiment() {
        embodied=!embodied; manipulation.Cancel(); handBones.Release(); embodiment.SetActive(embodied); embodied=embodiment.Active;
        if(!embodied) ResetPlacement();
        mirror.SetVisible(embodied);  
         mirror.Reposition(); ApplyBones();
    }
    void RecalibrateEmbodiment() { if(embodied) embodiment.Recalibrate();  mirror.Reposition(); }
    void ToggleMirror() { mirror.SetVisible(!mirror.Visible); if(mirror.Visible) mirror.Reposition();  }
    void SetStatus(string value)
    {
        statusText=value; modelMenu?.NotifyStatus(value); Debug.Log("AVATAR_VIEWER: "+value);
        try {
            var chains=avatar!=null?avatar.GetComponentsInChildren<SpringChain>(true):new SpringChain[0];
            int active=0; foreach(var chain in chains) if(chain.isActiveAndEnabled) active++;
            File.WriteAllText(Path.Combine(Application.persistentDataPath,"runtime-status.json"),JsonUtility.ToJson(new RuntimeStatus {
                message=value, timeUtc=DateTime.UtcNow.ToString("O"),unity=Application.unityVersion,
                loadedModel=avatar!=null?avatar.name:"",chainCount=chains.Length,activeChains=active,
                colliderCount=avatar!=null?avatar.GetComponentsInChildren<SpringCollider>(true).Length:0
            },true));
        } catch(Exception error) { Debug.LogWarning("Runtime status could not be written: "+error.Message); }
    }
    [Serializable] sealed class RuntimeStatus { public string message,timeUtc,unity,loadedModel; public int chainCount,activeChains,colliderCount; }
    void CreatePointer()
    {
        laser=new GameObject("Controller laser").AddComponent<LineRenderer>(); laser.positionCount=2; laser.startWidth=laser.endWidth=.003f;
        laser.material=new Material(Shader.Find("Unlit/Color")) { color=new Color(.3f,.8f,1) };
        cursor=GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(cursor.GetComponent<Collider>()); cursor.transform.localScale=Vector3.one*.014f;
        cursor.GetComponent<Renderer>().material=laser.material;
    }
    void Update()
    {
        if(stage==null) return;
        if(embodiment.Calibrated && !previouslyCalibrated) {  mirror.Reposition(); }
        previouslyCalibrated=embodiment.Calibrated;
        var right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand); var left=InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        bool menuButton=left.TryGetFeatureValue(CommonUsages.menuButton,out bool menuDown) && menuDown;
        if(menuButton && !previousMenuButton && !loading) ToggleMenu(); previousMenuButton=menuButton;
        if(!embodied && !modelMenu.Visible && right.TryGetFeatureValue(CommonUsages.primary2DAxis,out var stick)) {
            if(Mathf.Abs(stick.x)>.2f) yaw+=stick.x*60*Time.deltaTime;
            if(Mathf.Abs(stick.y)>.2f) size=Mathf.Clamp(size+stick.y*.4f*Time.deltaTime,.2f,2f);
        }
        if(!embodied && left.TryGetFeatureValue(CommonUsages.primary2DAxis,out var move) && move.magnitude>.2f) placement+=new Vector3(move.x,0,move.y)*Time.deltaTime*.5f;

        if(turning) yaw+=20*Time.deltaTime;
        if(!embodied) {
            stage.position=placement + (shaking ? new Vector3(Mathf.Sin(Time.time*3)*.08f,0,0):Vector3.zero);
            stage.rotation=Quaternion.Euler(0,yaw,0); stage.localScale=Vector3.one*size;
        }
        Quaternion rotation=Quaternion.identity;
        Vector3 origin=default;
        bool tracked=right.TryGetFeatureValue(CommonUsages.isTracked,out bool currentTracked) && currentTracked && right.TryGetFeatureValue(CommonUsages.devicePosition,out origin) && right.TryGetFeatureValue(CommonUsages.deviceRotation,out rotation);
        bool trigger=right.TryGetFeatureValue(CommonUsages.triggerButton,out var down) && down;
        bool gazeMode=!tracked, handValid=false, eyeValid=false;
        ulong handFlags=0;
        float rightGap=-1, leftGap=-1;
        ModelHandManipulation.Hand leftHand=default,rightHand=default;
        var gazeRay=new Ray(viewCamera.transform.position,viewCamera.transform.forward);
        #if UNITY_ANDROID && !UNITY_EDITOR
        if(PXR_HandTracking.GetActiveInputDevice()==ActiveInputDevice.HandTrackingActive || !tracked)
        {
            var aim=new HandAimState();
            handValid=PXR_HandTracking.GetAimState(HandType.HandRight,ref aim);
            handFlags=(ulong)aim.aimStatus;
            rightPinch=ReadPinch(HandType.HandRight,ref rightJoints,rightPinch,out rightGap);
            leftPinch=ReadPinch(HandType.HandLeft,ref leftJoints,leftPinch,out leftGap);
            trigger=rightPinch || leftPinch;
            leftHand=ReadHandPose(leftJoints,leftPinch); rightHand=ReadHandPose(rightJoints,rightPinch);
            // Pinch selection is independent of the hand-ray validity flag.
            gazeMode=true;
        }
        if(gazeMode) {
            eyeDevices.Clear(); InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking,eyeDevices);
            if(eyeDevices.Count>0 && PXR_EyeTracking.GetCombineEyeGazeVector(out var eyeDirection) && eyeDirection.sqrMagnitude>.000001f) {
                eyeValid=true; gazeRay.direction=viewCamera.transform.TransformDirection(eyeDirection.normalized);
            }
        }
        #endif
        if(!gazeMode) { leftJoints=rightJoints=default; rightPinch=leftPinch=false; }
        if(summonGesture.Tick(leftJoints,rightJoints,Time.unscaledTime,Application.isFocused)) {
            manipulation.Cancel();handBones.Release();modelMenu.ShowHome(summonGesture.LastHand);
        }
        trigger=summonGesture.FilterSelection(trigger) && Application.isFocused;
        summonIndicator.Supply(summonGesture,leftJoints,rightJoints,viewCamera.transform.parent,Application.isFocused);
        embodiment.SupplyHands(leftJoints,rightJoints);
        modelMenu.SupplyAnchor(leftJoints,rightJoints,viewCamera.transform.parent,Time.unscaledDeltaTime);
        if(leftVisual!=null) { leftVisual.SetDisplayAllowed(!embodiment.Active && Application.isFocused); leftVisual.Apply(leftJoints); }
        if(rightVisual!=null) { rightVisual.SetDisplayAllowed(!embodiment.Active && Application.isFocused); rightVisual.Apply(rightJoints); }
        if(gazeMode) { origin=gazeRay.origin; rotation=Quaternion.LookRotation(gazeRay.direction); }
        var ray=gazeMode?gazeRay:new Ray(origin,rotation*Vector3.forward);
        Button hovered=modelMenu.HitTest(ray,out Vector3 point);
        laser.enabled=!gazeMode && modelMenu.Visible;laser.SetPosition(0,origin);laser.SetPosition(1,point);cursor.SetActive(hovered!=null && !gazeMode);cursor.transform.position=point;
        if(pointer==null) pointer=new PointerEventData(EventSystem.current);
        pointer.position=viewCamera.WorldToScreenPoint(point);
        modelMenu.ProcessHeld(ray,trigger);
        if(hovered!=hoverButton) {
            if(hoverButton!=null) hoverButton.OnPointerExit(pointer);
            hoverButton=hovered; 
            if(hoverButton!=null) hoverButton.OnPointerEnter(pointer);
        }
        bool wasGrabbing=manipulation.Mode!=0;
        var leftTouch=ReadHandPose(leftJoints,leftPinch,true); var rightTouch=ReadHandPose(rightJoints,rightPinch,true);
        handBones.Update(leftJoints,rightJoints,viewCamera.transform.parent,leftTouch,rightTouch,!embodied && !wasGrabbing && !loading && !pickerPending && !modelMenu.Visible && !summonGesture.ContactActive && hovered==null);
        bool touchingBone=handBones.LeftGrabbing || handBones.RightGrabbing;
        if(touchingBone) { manipulation.Cancel(); leftHand.pinched=rightHand.pinched=false; }
        bool canGrab=!embodied && !touchingBone && avatar!=null && !loading && !pickerPending && !modelMenu.Visible && !summonGesture.ContactActive && hovered==null && ModelTargeted(gazeRay,leftHand,rightHand);
        manipulation.ViewRotation=viewCamera.transform.rotation;
        manipulation.DepthGain=Mathf.Clamp(Vector3.Distance(viewCamera.transform.position,placement)*1.8f,1.5f,4f);
        manipulation.Step(leftHand,rightHand,canGrab,ref placement,ref yaw,ref size,Time.unscaledDeltaTime);
        if(manipulation.Mode!=0) {
            turning=shaking=false;
            stage.SetPositionAndRotation(placement,Quaternion.Euler(0,yaw,0)); stage.localScale=Vector3.one*size;
        }
        if(trigger && !previousTrigger) {
            pinchEvents++;
            if(!wasGrabbing && !touchingBone && manipulation.Mode==0 && hovered!=null && hovered.interactable && !pickerPending && !loading) { clickEvents++; hovered.onClick.Invoke(); }
        }
        previousTrigger=trigger;
        if(Time.unscaledTime>=nextUsbCheck) { nextUsbCheck=Time.unscaledTime+1; ApplyRuntimeConfig(); CheckUsbUpload(); }
        if(Time.unscaledTime>=nextInputReport) {
            nextInputReport=Time.unscaledTime+1f;
            trackingSummary=(embodiment.Calibrated?"已校准":"等待校准")+" · 眼动 "+(expressions.EyeValid?"有效":"等待")+" · 面捕 "+(expressions.UpperFaceValid||expressions.LowerFaceValid?"有效":"等待");
            try { File.WriteAllText(Path.Combine(Application.persistentDataPath,"input-status.json"),JsonUtility.ToJson(new InputStatus { standingEyeHeight=embodiment.StandingHeight.EyeHeight,heightCalibrationBusy=embodiment.StandingHeight.Busy,heightCalibrationStatus=embodiment.StandingHeight.Status,stance=embodiment.StandingHeight.Stance,floorOriginReady=HeadTracking.FloorOriginReady, wheelVisible=modelMenu.Visible,wheelSummons=summonGesture.SummonCount,summonLeftGap=summonGesture.LeftGap,summonRightGap=summonGesture.RightGap,summonProgress=summonGesture.Progress,summonHoldSeconds=summonGesture.HoldSeconds,summonEnabled=summonGesture.Enabled,bodySettings=embodiment.Proportions?.Value,automaticFitBusy=embodiment.AutomaticFit?.Busy??false,automaticFitStatus=embodiment.AutomaticFit?.Status,armCalibrationStatus=embodiment.Proportions?.Status,armCalibrationSamples=embodiment.Proportions?.SampleCount??0,embodied=embodiment.Active,calibrated=embodiment.Calibrated,leftHandScale=embodiment.LeftHandScale,rightHandScale=embodiment.RightHandScale,leftWristError=embodiment.LeftWristError,rightWristError=embodiment.RightWristError,menuPages=modelMenu.PageCount,menuDockMode=modelMenu.DockMode,menuAnchorTracked=modelMenu.AnchorTracked,floorOffset=embodiment.FloorOffset,leftFootHeight=embodiment.LeftFootHeight,rightFootHeight=embodiment.RightFootHeight,mirrorVisible=mirror.Visible,mirrorEarVisible=mirror.EarVisible,faceSupported=expressions.FaceSupported,upperFaceValid=expressions.UpperFaceValid,lowerFaceValid=expressions.LowerFaceValid,mappedShapes=expressions.MappedShapes,vrcParameters=expressions.VrcParameters,vrcGaze=expressions.VrcGaze,vrcEyelids=expressions.VrcEyelids,facialGaze=expressions.FacialGaze,expressionMode=expressions.ExpressionMode,eyeSource=expressions.EyeSource,faceService=expressions.ServiceStatus,leftArmTracked=embodiment.LeftTracked,rightArmTracked=embodiment.RightTracked,handValid=handValid,handFlags=handFlags,rightGap=rightGap,leftGap=leftGap,pinchEvents=pinchEvents,clickEvents=clickEvents,leftHandVisible=leftVisual!=null && leftVisual.Visible,rightHandVisible=rightVisual!=null && rightVisual.Visible,handColliders=handBones.ActiveColliders,handBoneContacts=handBones.ContactEvents,handBoneGrabs=handBones.GrabEvents,grabMode=manipulation.Mode,grabEvents=manipulation.GrabCount,modelPosition=stage.position,modelYaw=stage.eulerAngles.y,modelScale=stage.localScale.x,passthrough=PXR_Manager.EnableVideoSeeThrough,eyeValid=expressions.EyeValid,gazeMode=gazeMode,hovered=hovered!=null?hovered.name:"",cameraPosition=viewCamera.transform.position },true)); } catch(IOException) {}
        }
    }
    [Serializable] sealed class InputStatus { public float standingEyeHeight; public bool heightCalibrationBusy,floorOriginReady; public string heightCalibrationStatus,stance; public bool wheelVisible,summonEnabled; public int wheelSummons; public float summonLeftGap,summonRightGap,summonProgress,summonHoldSeconds; public BodyProportions.Settings bodySettings; public bool automaticFitBusy; public string automaticFitStatus; public string armCalibrationStatus; public int armCalibrationSamples; public int frameCount=Time.frameCount; public string timeUtc=DateTime.UtcNow.ToString("O"); public Vector3 cameraRotation=Camera.main.transform.eulerAngles; public bool xrActive=XRSettings.isDeviceActive,stereoEnabled=Camera.main.stereoEnabled; public bool mirrorVisible,mirrorEarVisible; public bool embodied,calibrated,faceSupported,upperFaceValid,lowerFaceValid,leftArmTracked,rightArmTracked,handValid,eyeValid,gazeMode,passthrough,leftHandVisible,rightHandVisible; public float rightGap,leftGap,leftHandScale,rightHandScale,leftWristError,rightWristError; public bool menuAnchorTracked; public float floorOffset,leftFootHeight,rightFootHeight; public int menuDockMode; public Vector2 vrcGaze,vrcEyelids; public Vector4 facialGaze; public string expressionMode; public int vrcParameters; public int menuPages,mappedShapes,pinchEvents,clickEvents,grabMode,grabEvents,handColliders,handBoneContacts,handBoneGrabs; public Vector3 modelPosition; public float modelYaw,modelScale; public ulong handFlags; public string hovered,faceService,eyeSource; public Vector3 cameraPosition; }
    [Serializable] sealed class UsbRequest { public string id,file; }
    [Serializable] sealed class RuntimeConfig { public bool embodied,mirror,expressions; }
    void ApplyRuntimeConfig() {
        string path=Path.Combine(Application.persistentDataPath,"viewer-config.json");
        if(!File.Exists(path)) return;
        try {
            string text=File.ReadAllText(path); if(text==appliedConfig) return;
            var config=JsonUtility.FromJson<RuntimeConfig>(text); if(config==null) return;
            appliedConfig=text; embodied=config.embodied; manipulation.Cancel(); handBones.Release();
            embodiment.SetActive(embodied); if(!embodied) ResetPlacement();
            expressions.SetTracking(config.expressions); expressions.enabled=config.expressions;
            mirror.SetVisible(config.mirror && embodied);
            if(modelMenu!=null) {modelMenu.Refresh();     mirror.Reposition(); }
        } catch(Exception error) { Debug.LogWarning("Viewer runtime config: "+error.Message); }
    }
    [Serializable] sealed class UsbResult { public string id,message,model; public bool loaded; }
    void CheckUsbUpload() {
        if(loading || pickerPending) return;
        string path=Path.Combine(Application.persistentDataPath,"usb-request.json");
        if(!File.Exists(path)) return;
        try {
            var request=JsonUtility.FromJson<UsbRequest>(File.ReadAllText(path));
            if(request==null || string.IsNullOrEmpty(request.id) || request.id==usbRequestId) return;
            usbRequestId=request.id;
            if(string.IsNullOrEmpty(request.file) || Path.GetFileName(request.file)!=request.file || !request.file.EndsWith(".avatarbundle",StringComparison.OrdinalIgnoreCase)) { WriteUsbResult(request.id,false,"无效的模型文件名"); return; }
            StartCoroutine(LoadUsb(request));
        } catch(Exception error) { Debug.LogWarning("USB model request: "+error.Message); }
    }
    IEnumerator LoadUsb(UsbRequest request) {
        string path=Path.Combine(ModelsDirectory,request.file);
        RefreshLibrary(); yield return Load(path);
        WriteUsbResult(request.id,avatar!=null && loadedPath==path,statusText);
    }
    void WriteUsbResult(string id,bool success,string message) {
        File.WriteAllText(Path.Combine(Application.persistentDataPath,"usb-result.json"),JsonUtility.ToJson(new UsbResult { id=id,loaded=success,message=message,model=avatar!=null?avatar.name:"" },true));
        string request=Path.Combine(Application.persistentDataPath,"usb-request.json");
        if(File.Exists(request) && JsonUtility.FromJson<UsbRequest>(File.ReadAllText(request))?.id==id) File.Delete(request);
    }
    ModelHandManipulation.Hand ReadHandPose(HandJointLocations joints,bool pinch,bool fingertips=false) {
        if(joints.isActive==0 || joints.jointLocations==null || joints.jointCount<=10) return default;
        var palm=joints.jointLocations[(int)HandJoint.JointPalm];
        var thumb=joints.jointLocations[(int)HandJoint.JointThumbTip];
        var index=joints.jointLocations[(int)HandJoint.JointIndexTip];
        var required=HandLocationStatus.PositionValid;
        if((thumb.locationStatus & required)==0 || (index.locationStatus & required)==0) return default;
        var tracking=viewCamera.transform.parent;
        Vector3 position=(thumb.pose.Position.ToVector3()+index.pose.Position.ToVector3())*.5f;
        if(!fingertips) {
            if((palm.locationStatus & required)!=0) position=palm.pose.Position.ToVector3();
            else {
                var wrist=joints.jointLocations[(int)HandJoint.JointWrist];
                if((wrist.locationStatus & required)==0) return default;
                position=wrist.pose.Position.ToVector3();
            }
        }
        Quaternion rotation=palm.pose.Orientation.ToQuat();
        return new ModelHandManipulation.Hand { valid=true,pinched=pinch,position=tracking!=null?tracking.TransformPoint(position):position,rotation=tracking!=null?tracking.rotation*rotation:rotation };
    }
    bool ModelTargeted(Ray ray,ModelHandManipulation.Hand left,ModelHandManipulation.Hand right) {
        bool found=false; Bounds bounds=default;
        foreach(var renderer in avatar.GetComponentsInChildren<Renderer>()) {
            if(!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if(!found) { bounds=renderer.bounds; found=true; } else bounds.Encapsulate(renderer.bounds);
        }
        if(!found) return false;
        bounds.Expand(.12f);
        bool nearLeft=left.valid && Vector3.Distance(bounds.ClosestPoint(left.position),left.position)<.15f;
        bool nearRight=right.valid && Vector3.Distance(bounds.ClosestPoint(right.position),right.position)<.15f;
        return nearLeft || nearRight || (bounds.IntersectRay(ray,out var distance) && distance<5);
    }
    void OnApplicationFocus(bool focus) { if(!focus) { summonGesture?.Cancel();summonIndicator?.Hide();modelMenu?.SetVisible(false);manipulation.Cancel(); handBones?.Release(); leftVisual?.Hide(); rightVisual?.Hide(); rightPinch=leftPinch=previousTrigger=false; } }
    void OnApplicationPause(bool paused) { if(paused) { summonGesture?.Cancel();summonIndicator?.Hide();modelMenu?.SetVisible(false);manipulation.Cancel(); handBones?.Release(); leftVisual?.Hide(); rightVisual?.Hide(); rightPinch=leftPinch=previousTrigger=false; } }
    public static bool PinchFromDistance(bool valid,float gap,bool wasPinched) {
        return valid && !float.IsNaN(gap) && gap>=0 && gap<(wasPinched?.04f:.025f);
    }
    bool ReadPinch(HandType hand,ref HandJointLocations joints,bool wasPinched,out float gap) {
        gap=-1;
        bool received=PXR_HandTracking.GetJointLocations(hand,ref joints);
        if(!received) joints=default;
        bool jointsValid=received && joints.isActive!=0 && joints.jointLocations!=null && joints.jointCount>10;
        if(jointsValid) {
            var thumb=joints.jointLocations[(int)HandJoint.JointThumbTip];
            var index=joints.jointLocations[(int)HandJoint.JointIndexTip];
            jointsValid=(thumb.locationStatus & HandLocationStatus.PositionValid)!=0 && (index.locationStatus & HandLocationStatus.PositionValid)!=0;
            if(jointsValid) gap=Vector3.Distance(thumb.pose.Position.ToVector3(),index.pose.Position.ToVector3());
        }
        // Read the SDK's public Input System controls when available (new hand interaction).
        string usage=hand==HandType.HandRight?"RightHand":"LeftHand";
        foreach(var device in InputSystem.devices) {
            bool matches=false; foreach(var deviceUsage in device.usages) if(deviceUsage.ToString()==usage) matches=true;
            if(!matches) continue;
            var value=device.TryGetChildControl<AxisControl>("pinchValue");
            var ready=device.TryGetChildControl<ButtonControl>("pinchReady");
            if(value!=null && ready!=null && ready.isPressed) return value.ReadValue()>(wasPinched?.6f:.8f);
        }
        if(jointsValid) return PinchFromDistance(true,gap,wasPinched);
        var aim=new HandAimState();
        return PXR_HandTracking.GetAimState(hand,ref aim) && (aim.aimStatus & HandAimStatus.AimIndexPinching)!=0;
    }
    public static bool ContainsWorldPoint(RectTransform rectangle,Vector3 point) {
        var local=rectangle.InverseTransformPoint(point);
        return Mathf.Abs(local.z)<.01f && rectangle.rect.Contains(new Vector2(local.x,local.y));
    }
    void OnDestroy() { handBones?.Destroy(); if(bundle!=null) bundle.Unload(false); }
}


