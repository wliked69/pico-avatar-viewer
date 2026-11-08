using System;
using System.Collections;
using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.XR;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public sealed class AvatarExpressionTracking : MonoBehaviour
{
    public bool TrackingEnabled { get; private set; }=true;
    public bool EyeValid { get; private set; }
    public string EyeSource { get; private set; }="waiting";
    public bool UpperFaceValid { get; private set; }
    public bool LowerFaceValid { get; private set; }
    public bool FaceSupported { get; private set; }
    public string ServiceStatus { get; private set; }="等待面捕服务";
    public int MappedShapes => bindings.Count;
    public int VrcParameters => vrc.Count;
    public string ExpressionMode => vrc.Count>0?"VRCFT / Unified Expressions":"基础表情";
    public Vector2 VrcGaze { get { frame.TryRead("EyeX",out float x,out bool xv); frame.TryRead("EyeY",out float y,out bool yv); return new Vector2(xv?x:0,yv?y:0); } }
    public Vector2 VrcEyelids { get { frame.TryRead("EyeLidLeft",out float l,out bool lv); frame.TryRead("EyeLidRight",out float r,out bool rv); return new Vector2(lv?l:0,rv?r:0); } }
    public Vector4 FacialGaze => new Vector4(weights[7]-weights[8],weights[9]-weights[6],weights[15]-weights[14],weights[16]-weights[13]);
    readonly VrcFaceTrackingBridge vrc=new VrcFaceTrackingBridge();
    readonly VrcFaceTrackingBridge.Frame frame=new VrcFaceTrackingBridge.Frame();
    readonly List<Binding> bindings=new List<Binding>();
    readonly List<InputDevice> eyeDevices=new List<InputDevice>();
    readonly float[] weights=new float[52];
    AvatarEmbodiment rig;
    float nextStart,lastFaceTime;
    long lastTimestamp;
    bool started;
    Quaternion leftRest,rightRest;
    bool leftOpenValid,rightOpenValid;
    float leftOpen=1,rightOpen=1,leftYaw,rightYaw,leftPitch,rightPitch;
    struct Binding { public SkinnedMeshRenderer mesh; public int shape,source,other; public float original; }
    // Semantic names follow the SDK's XrFaceExpressionBD order, rather than the legacy BlendShapeIndex order.
    static readonly string[] Names={"browDownLeft","browDownRight","browInnerUp","browOuterUpLeft","browOuterUpRight","eyeBlinkLeft","eyeLookDownLeft","eyeLookInLeft","eyeLookOutLeft","eyeLookUpLeft","eyeSquintLeft","eyeWideLeft","eyeBlinkRight","eyeLookDownRight","eyeLookInRight","eyeLookOutRight","eyeLookUpRight","eyeSquintRight","eyeWideRight","noseSneerLeft","noseSneerRight","cheekPuff","cheekSquintLeft","cheekSquintRight","mouthClose","mouthFunnel","mouthPucker","mouthLeft","mouthRight","mouthSmileLeft","mouthSmileRight","mouthFrownLeft","mouthFrownRight","mouthDimpleLeft","mouthDimpleRight","mouthStretchLeft","mouthStretchRight","mouthRollLower","mouthRollUpper","mouthShrugLower","mouthShrugUpper","mouthPressLeft","mouthPressRight","mouthLowerDownLeft","mouthLowerDownRight","mouthUpperUpLeft","mouthUpperUpRight","jawForward","jawLeft","jawRight","jawOpen","tongueOut"};
    public static string ShapeKey(string name) {
        string key=AvatarEmbodiment.Canonical(name).Replace("blendshape2","").Replace("blendshape","");
        if(key.EndsWith("left")) key=key.Substring(0,key.Length-4)+"l";
        if(key.EndsWith("right")) key=key.Substring(0,key.Length-5)+"r";
        return key;
    }
    public void Bind(AvatarEmbodiment modelRig) {
        Restore(); bindings.Clear(); rig=modelRig;
        if(rig.LeftEye!=null) leftRest=rig.LeftEye.localRotation;
        if(rig.RightEye!=null) rightRest=rig.RightEye.localRotation;
        vrc.Bind(rig.Avatar.GetComponentInChildren<AvatarViewer.AvatarMenuData>(true)?.effects);
        rig.PreserveAnimatedEyes=vrc.OwnsGaze;
        foreach(var mesh in rig.Avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
            if(mesh.sharedMesh==null || !mesh.enabled || !mesh.gameObject.activeInHierarchy) continue;
            var map=new Dictionary<string,int>();
            for(int i=0;i<mesh.sharedMesh.blendShapeCount;i++) { string key=ShapeKey(mesh.sharedMesh.GetBlendShapeName(i)); if(!map.ContainsKey(key)) map.Add(key,i); }
            var used=new HashSet<int>();
            for(int i=0;i<Names.Length;i++) Add(mesh,map,used,i,-1,Names[i]);
            if(!Has(mesh,5)) Add(mesh,map,used,5,-1,"eye_close_L");
            if(!Has(mesh,12)) Add(mesh,map,used,12,-1,"eye_close_R");
            if(!Has(mesh,5) && !Has(mesh,12)) Add(mesh,map,used,5,12,"vrc.Blink","eye_close","Blink");
            if(!Has(mesh,50)) Add(mesh,map,used,50,-1,"vrc.v_aa","mouth_a1","A");
            if(!Has(mesh,26)) Add(mesh,map,used,26,-1,"vrc.v_ou","mouth_u1");
            if(!Has(mesh,25)) Add(mesh,map,used,25,-1,"vrc.v_oh","mouth_o1");
            if(!Has(mesh,29)) Add(mesh,map,used,29,-1,"mouth_smile_L");
            if(!Has(mesh,30)) Add(mesh,map,used,30,-1,"mouth_smile_R");
            if(!Has(mesh,29) && !Has(mesh,30)) Add(mesh,map,used,29,30,"mouth_smile");
            if(!Has(mesh,31)) Add(mesh,map,used,31,-1,"mouth_sad_L");
            if(!Has(mesh,32)) Add(mesh,map,used,32,-1,"mouth_sad_R");
        }
    }
    bool Has(SkinnedMeshRenderer mesh,int source) => bindings.Exists(b=>b.mesh==mesh && b.source==source);
    void Add(SkinnedMeshRenderer mesh,Dictionary<string,int> map,HashSet<int> used,int source,int other,params string[] aliases) {
        foreach(var alias in aliases) if(map.TryGetValue(ShapeKey(alias),out int index) && used.Add(index)) {
            bindings.Add(new Binding { mesh=mesh,shape=index,source=source,other=other,original=mesh.GetBlendShapeWeight(index) }); return;
        }
    }
    IEnumerator Start() {
        #if UNITY_ANDROID && !UNITY_EDITOR
        var missing=new List<string>();
        foreach(string p in new[]{"com.picovr.permission.EYE_TRACKING","com.picovr.permission.FACE_TRACKING"}) if(!Permission.HasUserAuthorizedPermission(p)) missing.Add(p);
        if(missing.Count>0) { Permission.RequestUserPermissions(missing.ToArray()); yield return new WaitForSeconds(1); }
        #endif
        yield return null;
    }
    void Update() {
        if(!TrackingEnabled || !Application.isFocused) { vrc.Restore(); return; }
        #if UNITY_ANDROID && !UNITY_EDITOR
        if(!started && Time.unscaledTime>=nextStart && Permission.HasUserAuthorizedPermission("com.picovr.permission.FACE_TRACKING")) {
            nextStart=Time.unscaledTime+5;
            bool supported=false; int count=0; XrFacialSimulationModeBD[] modes=null;
            var result=PXR_FacialSimulation.GetFacialSimulationSupported(ref supported,ref count,ref modes);
            FaceSupported=result==PxrResult.SUCCESS && supported;
            if(FaceSupported) {
                result=PXR_FacialSimulation.StartFacialSimulation(XrFacialSimulationModeBD.XR_FACIAL_SIMULATION_MODE_DEFAULT_BD);
                started=result==PxrResult.SUCCESS;
            }
            ServiceStatus=started?"面捕服务已启动":"面捕未就绪："+result;
        }
        ReadFace();
        #endif
        ReadEyes();
        ApplyVrcInputs(Application.isFocused);
    }
    void ApplyVrcInputs(bool focused) {
        frame.weights=weights; frame.enabled=TrackingEnabled && focused;
        frame.upper=UpperFaceValid; frame.lower=LowerFaceValid;
        // A valid dedicated gaze stream must not be replaced by face coefficients:
        // this headset can return valid upper-face data with zero look coefficients.
        frame.gaze=EyeValid && EyeSource=="gaze";
        frame.gazeX=leftYaw/30; frame.gazeY=-leftPitch/22;
        frame.leftOpenValid=leftOpenValid; frame.rightOpenValid=rightOpenValid;
        frame.leftOpen=leftOpen; frame.rightOpen=rightOpen;
        vrc.Apply(frame); // Animator evaluates the original FX graph after Update.
    }
    unsafe void ReadFace() {
        var data=new PxrFacialSimulationData();
        bool received=started && PXR_FacialSimulation.GetFacialSimulationData(0,ref data)==PxrResult.SUCCESS;
        if(received && data.timestamp!=lastTimestamp) { lastTimestamp=data.timestamp; lastFaceTime=Time.unscaledTime; }
        bool fresh=received && data.timestamp!=0 && Time.unscaledTime-lastFaceTime<.5f;
        UpperFaceValid=fresh && data.isUpperFaceDataValid; LowerFaceValid=fresh && data.isLowerFaceDataValid;
        for(int i=0;i<52;i++) weights[i]=fresh?SafeWeight(data.faceExpressionWeights[i]):0;
    }
    public static float SafeWeight(float value) => float.IsNaN(value)||float.IsInfinity(value)?0:Mathf.Clamp01(value);
    void ReadEyes() {
        EyeValid=false; leftOpenValid=rightOpenValid=false; leftOpen=rightOpen=1;
        Vector3 gaze=Vector3.forward;
        #if UNITY_ANDROID && !UNITY_EDITOR
        if(TrackingEnabled && Application.isFocused) {
            eyeDevices.Clear(); InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking|InputDeviceCharacteristics.HeadMounted,eyeDevices);
            if(eyeDevices.Count>0) {
                EyeValid=PXR_EyeTracking.GetCombineEyeGazeVector(out gaze) && AvatarEmbodiment.Finite(gaze) && gaze.sqrMagnitude>1e-8f;
                leftOpenValid=PXR_EyeTracking.GetLeftEyeGazeOpenness(out leftOpen);
                rightOpenValid=PXR_EyeTracking.GetRightEyeGazeOpenness(out rightOpen);
            }
        }
        #endif
        var direction=EyeValid?gaze.normalized:Vector3.forward;
        float yaw=Mathf.Clamp(Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,-30,30);
        float pitch=Mathf.Clamp(-Mathf.Atan2(direction.y,Mathf.Sqrt(direction.x*direction.x+direction.z*direction.z))*Mathf.Rad2Deg,-22,22);
        leftOpenValid&=EyeValid && !float.IsNaN(leftOpen) && !float.IsInfinity(leftOpen);
        rightOpenValid&=EyeValid && !float.IsNaN(rightOpen) && !float.IsInfinity(rightOpen);
        leftYaw=rightYaw=yaw; leftPitch=rightPitch=pitch;
        EyeSource=EyeValid?"gaze":"waiting";
        if(!EyeValid && TrackingEnabled && UpperFaceValid) {
            leftYaw=(weights[8]-weights[7])*30; rightYaw=(weights[14]-weights[15])*30;
            leftPitch=(weights[6]-weights[9])*22; rightPitch=(weights[13]-weights[16])*22;
            EyeValid=true; EyeSource="facial-eye-coefficients";
        }
    }
    void LateUpdate() {
        if(rig==null || rig.Avatar==null) return;
        if(!vrc.OwnsGaze) {
            if(rig.LeftEye!=null) rig.LeftEye.localRotation=leftRest*Quaternion.Euler(leftPitch,leftYaw,0);
            if(rig.RightEye!=null) rig.RightEye.localRotation=rightRest*Quaternion.Euler(rightPitch,rightYaw,0);
        }
        ApplyWeights(Time.unscaledDeltaTime,leftOpenValid,leftOpen,rightOpenValid,rightOpen);
    }
    public void ApplyWeights(float delta,bool leftOpenValid=false,float leftOpen=1,bool rightOpenValid=false,float rightOpen=1) {
        float rate=1-Mathf.Exp(-delta*25);
        foreach(var b in bindings) {
            if(b.mesh==null) continue;
            // Do not overwrite shapes already driven by the avatar's FTF graph.
            if(IsUpperSource(b.source)?vrc.OwnsUpper:vrc.OwnsLower) continue;
            bool valid=TrackingEnabled && (IsUpperSource(b.source)?UpperFaceValid:LowerFaceValid);
            float value=weights[b.source]; if(b.other>=0) value=(value+weights[b.other])*.5f;
            if(!UpperFaceValid && b.source==5 && b.other<0 && leftOpenValid) { valid=true; value=1-SafeWeight(leftOpen); }
            if(!UpperFaceValid && b.source==12 && rightOpenValid) { valid=true; value=1-SafeWeight(rightOpen); }
            if(!UpperFaceValid && b.source==5 && b.other==12 && leftOpenValid && rightOpenValid) { valid=true; value=1-(SafeWeight(leftOpen)+SafeWeight(rightOpen))*.5f; }
            // Restore imported defaults when a tracking domain is invalid, never reuse its last frame.
            float target=valid?SafeWeight(value)*100:b.original;
            b.mesh.SetBlendShapeWeight(b.shape,Mathf.Lerp(b.mesh.GetBlendShapeWeight(b.shape),target,rate));
        }
    }
    static bool IsUpperSource(int source) => source<19;
    public void SetTracking(bool value) { TrackingEnabled=value; enabled=value; if(!value) { StopService(); Restore(); } }
    void StopService() {
        #if UNITY_ANDROID && !UNITY_EDITOR
        if(started) PXR_FacialSimulation.StopFacialSimulation();
        #endif
        started=false; UpperFaceValid=LowerFaceValid=EyeValid=false; leftYaw=rightYaw=leftPitch=rightPitch=0;
        frame.enabled=frame.upper=frame.lower=frame.gaze=false;
        lastTimestamp=0; lastFaceTime=0; Array.Clear(weights,0,weights.Length); nextStart=0;
        vrc.Restore();
    }
    void Restore() {
        vrc.Restore();
        foreach(var b in bindings) if(b.mesh!=null && !(IsUpperSource(b.source)?vrc.OwnsUpper:vrc.OwnsLower)) b.mesh.SetBlendShapeWeight(b.shape,b.original);
        if(rig!=null && !vrc.OwnsGaze) {
            if(rig.LeftEye!=null) rig.LeftEye.localRotation=leftRest;
            if(rig.RightEye!=null) rig.RightEye.localRotation=rightRest;
        }
    }
    void OnDisable() { StopService(); Restore(); }
    void OnApplicationPause(bool paused) { if(paused) { StopService(); Restore(); } }
    void OnApplicationFocus(bool focused) { if(!focused) { StopService(); Restore(); } }
    void OnDestroy() { StopService(); Restore(); }
}
