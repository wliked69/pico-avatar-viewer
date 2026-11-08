using System;
using System.Collections.Generic;
using System.Linq;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.XR;

// Runs before secondary motion; only the tracked skeleton is posed here.
[DefaultExecutionOrder(-100)]
public sealed class AvatarEmbodiment : MonoBehaviour
{
    public Shader portableShader;
    public bool Active { get; private set; }
    public bool Calibrated { get; private set; }
    public bool PreserveAnimatedEyes { get; set; }
    public bool LeftTracked { get; private set; }
    public bool RightTracked { get; private set; }
    public float Scale { get; private set; }=1;
    public float LeftHandScale => left!=null?left.fit:1;
    public float RightHandScale => right!=null?right.fit:1;
    HandSizeCalibration handCalibration;
    public HandSizeCalibration HandCalibration=>handCalibration ?? (handCalibration=new HandSizeCalibration());
    public void BeginHandCalibration() { standingHeight?.Cancel();standingHeight?.ClearStatus();AutomaticFit?.Cancel(); AutomaticFit?.ClearStatus(); HandCalibration.Begin(Time.unscaledTime); }
    public BodyProportions Proportions { get; private set; }
    public AutomaticProportionCalibration AutomaticFit { get; private set; }
    StandingHeightCalibration standingHeight;
    public StandingHeightCalibration StandingHeight=>standingHeight??(standingHeight=new StandingHeightCalibration());
    public void BeginStandingHeightCalibration() {
        if(StandingHeight.Busy){StandingHeight.Cancel();return;}
        AutomaticFit?.Cancel();AutomaticFit?.ClearStatus();
        if(Proportions?.Busy==true || HandCalibration.Busy){StandingHeight.Reject("请等待当前手部或臂长校准结束");return;}
        StandingHeight.Begin(Time.unscaledTime);
    }
    float automaticHeightAt;
    float automaticOriginalScale,automaticOriginalFloor;
    bool automaticOriginalCalibrated;
    bool automaticHeightDone;
    public void BeginAutomaticCalibration() {
        if(AutomaticFit==null) return;
        if(!StandingHeight.Valid || StandingHeight.Busy) {AutomaticFit.Reject("请先完成「站立身高校准」");return;}
        StandingHeight.ClearStatus();
        if(!Active || Head==null || !left.Valid || !right.Valid) { AutomaticFit.Reject("请先开启附身，再站直开始自动匹配"); return; }
        if(AutomaticFit.Begin(Time.unscaledTime)) {
            automaticOriginalScale=Scale; automaticOriginalFloor=floorHeight; automaticOriginalCalibrated=Calibrated;
            automaticHeightAt=Time.unscaledTime+2.8f; automaticHeightDone=false;
        }
    }
    void AutomaticFinished(bool success) {
        if(!success && automaticHeightDone) {
            Scale=automaticOriginalScale; floorHeight=automaticOriginalFloor; Calibrated=automaticOriginalCalibrated;
            if(stage!=null) stage.localScale=Vector3.one*Scale;
            if(Active && Calibrated && Head!=null && eyes!=null) {RestorePose();PoseRoot();}
        }
        automaticHeightDone=false;
    }
    public void BeginArmCalibration() {
        AutomaticFit?.Cancel(); AutomaticFit?.ClearStatus();
        if(!StandingHeight.Valid || StandingHeight.Busy) {if(Proportions!=null)Proportions.Status="请先完成「站立身高校准」";return;}
        StandingHeight.ClearStatus();
        if(Avatar==null || Head==null || !left.Valid || !right.Valid) return;
        if(!Active || !Calibrated) { Proportions.Status="请先开启附身并站直重新校准"; return; }
        Proportions.Begin(Time.unscaledTime);
    }
    public float LeftWristError { get; private set; }=-1;
    public float RightWristError { get; private set; }=-1;
    public Vector3 LeftWristTarget { get; private set; }
    public Vector3 RightWristTarget { get; private set; }
    public double PoseMilliseconds { get; private set; }
    public float FloorOffset { get; private set; }=-.15f;
    public float LeftFootHeight => leftLeg!=null && leftLeg.foot!=null?leftLeg.foot.position.y:0;
    public float RightFootHeight => rightLeg!=null && rightLeg.foot!=null?rightLeg.foot.position.y:0;
    public string RigStatus { get; private set; }="等待模型";
    public Transform Head { get; private set; }
    public Transform LeftEye { get; private set; }
    public Transform RightEye { get; private set; }
    public GameObject Avatar { get; private set; }
    Camera eyes;
    Transform stage;
    Transform[] skeleton;
    readonly Dictionary<Transform,Quaternion> rest=new Dictionary<Transform,Quaternion>();
    readonly Dictionary<Transform,Vector3> restPositions=new Dictionary<Transform,Vector3>();
    readonly Dictionary<Transform,Vector3> restScales=new Dictionary<Transform,Vector3>();
    readonly Dictionary<string,Transform> humanoid=new Dictionary<string,Transform>();
    readonly List<Mesh> ownedMeshes=new List<Mesh>();
    readonly List<Tuple<SkinnedMeshRenderer,Mesh>> originals=new List<Tuple<SkinnedMeshRenderer,Mesh>>();
    readonly List<Tuple<MeshFilter,Mesh>> originalFilters=new List<Tuple<MeshFilter,Mesh>>();
    readonly List<Tuple<Renderer,Material[]>> originalMaterials=new List<Tuple<Renderer,Material[]>>();
    readonly Dictionary<Material,Material> ownedMaterials=new Dictionary<Material,Material>();
    Arm left,right;
    Leg leftLeg,rightLeg;
    Transform hips;
    float floorHeight;
    Quaternion headBasis;
    Vector3 eyeAnchor;
    float bodyYaw;
    HandJointLocations leftJoints,rightJoints;
    bool renderPrepared;
    AvatarViewer.SpringChain[] footAttachments=new AvatarViewer.SpringChain[0];
    AvatarViewer.SpringChain[] propChains=new AvatarViewer.SpringChain[0];
    void Awake() { FloorOffset=Mathf.Clamp(PlayerPrefs.GetFloat("AvatarFloorOffset",-.15f),-1,.5f); }
    public void SetFloorOffset(float value,bool save=true) {
        AutomaticFit?.Cancel();
        standingHeight?.Cancel();
        FloorOffset=Mathf.Clamp(value,-1,.5f); Calibrated=false;
        if(save) { PlayerPrefs.SetFloat("AvatarFloorOffset",FloorOffset); PlayerPrefs.Save(); }
    }
    struct Finger { public Transform bone; public int joint,segment; public Vector3 axis,tail; }
    sealed class Arm {
        public Transform upper,lower,hand,index,middle,little;
        public Quaternion basis;
        public float palmLength,palmWidth,fit=1,widthFit=1,baseReach;
        public Finger[] fingers;
        public bool Valid => upper!=null && lower!=null && hand!=null;
    }
    sealed class Leg {
        public Transform upper,lower,foot,toes;
        public Vector3 anchor;
        public Quaternion footBasis;
        public bool Valid => upper!=null && lower!=null && foot!=null;
    }
    public static string Canonical(string name) => new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()).Replace("mixamorig","");
    public Transform Bone(params string[] names) {
        if(skeleton==null) return null;
        foreach(var name in names) { string key=Canonical(name); if(humanoid.TryGetValue(key,out var mapped) && mapped!=null) return mapped; }
        foreach(var name in names) { string key=Canonical(name); foreach(var t in skeleton) if(t!=null && Canonical(t.name)==key) return t; }
        return null;
    }
    public void Bind(GameObject model,Transform modelStage,Camera camera) {
        Unbind(); Avatar=model; stage=modelStage; eyes=camera;
        var info=model.GetComponent<AvatarViewer.AvatarPackageInfo>();
        Proportions=new BodyProportions(info!=null?info.displayName:model.name.Replace("(Clone)",""));
        AutomaticFit=new AutomaticProportionCalibration(HandCalibration,Proportions);
        AutomaticFit.Finished+=AutomaticFinished;
        AutomaticFit.PrepareWholeBody=FitWholeBodyFromHands;
        if(info!=null && info.humanoidBoneNames!=null && info.humanoidBones!=null)
            for(int i=0;i<Math.Min(info.humanoidBoneNames.Length,info.humanoidBones.Length);i++) if(info.humanoidBones[i]!=null) humanoid[Canonical(info.humanoidBoneNames[i])]=info.humanoidBones[i];
        var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var body=skins.Where(s=>s.sharedMesh!=null && s.enabled && s.gameObject.activeInHierarchy)
            .OrderByDescending(s=>s.sharedMesh.blendShapeCount).FirstOrDefault();
        skeleton=(body!=null?body.bones.Where(t=>t!=null):Enumerable.Empty<Transform>())
            .Concat(model.GetComponentsInChildren<Transform>(true)).Distinct().ToArray();
        Head=Bone("Head"); LeftEye=Bone("LeftEye","Eye.L","Eye_L"); RightEye=Bone("RightEye","Eye.R","Eye_R");
        if(Head==null) { RigStatus="缺少 Head 骨骼，请使用人形模型"; return; }
        headBasis=Quaternion.Inverse(model.transform.rotation)*Head.rotation;
        left=CreateArm(true); right=CreateArm(false);
        propChains=model.GetComponentsInChildren<AvatarViewer.SpringChain>(true).Where(c=>IsPropChain(c,left.hand,right.hand)).ToArray();
        hips=Bone("Hips"); Remember(hips);
        leftLeg=CreateLeg(true); rightLeg=CreateLeg(false);
        footAttachments=model.GetComponentsInChildren<AvatarViewer.SpringChain>(true).Where(c=>c.root!=null && (c.root.parent==leftLeg.toes || c.root.parent==rightLeg.toes)).ToArray();
        Remember(Head); Remember(LeftEye); Remember(RightEye);
        eyeAnchor=stage.InverseTransformPoint(EyePosition());
        if(eyeAnchor.y<.2f) { RigStatus="模型眼睛高度无效"; return; }
        RigStatus=left.Valid && right.Valid?"头部、双臂、手指就绪":"部分手臂骨骼未匹配";
    }
    void Remember(Transform t) { if(t!=null && !rest.ContainsKey(t)) { rest.Add(t,t.localRotation); restPositions.Add(t,t.localPosition); restScales.Add(t,t.localScale); } }
    public static bool IsPropChain(AvatarViewer.SpringChain chain,Transform leftHand,Transform rightHand) {
        var root=chain.root; if(root==null) return false;
        return leftHand!=null && root.IsChildOf(leftHand) || rightHand!=null && root.IsChildOf(rightHand)
            || root.name.IndexOf("Chem",StringComparison.OrdinalIgnoreCase)>=0 || root.name.IndexOf("Charm",StringComparison.OrdinalIgnoreCase)>=0;
    }
    Leg CreateLeg(bool isLeft) {
        string side=isLeft?"Left":"Right",suffix=isLeft?".L":".R";
        var leg=new Leg { upper=Bone(side+"UpperLeg","Upper_leg"+suffix),lower=Bone(side+"LowerLeg","Lower_leg"+suffix),foot=Bone(side+"Foot","Foot"+suffix),toes=Bone(side+"Toes","Toe"+suffix) };
        Remember(leg.upper); Remember(leg.lower); Remember(leg.foot); Remember(leg.toes);
        if(leg.Valid) { leg.anchor=stage.InverseTransformPoint(leg.foot.position); leg.footBasis=Quaternion.Inverse(stage.rotation)*leg.foot.rotation; }
        return leg;
    }
    Arm CreateArm(bool isLeft) {
        string suffix=isLeft?".L":".R",side=isLeft?"Left":"Right";
        var arm=new Arm { upper=Bone("Upper_arm"+suffix,side+"UpperArm",side+"Arm"),lower=Bone("Lower_arm"+suffix,side+"LowerArm",side+"ForeArm"),hand=Bone("Hand"+suffix,side+"Hand") };
        arm.index=Bone("Index Proximal"+suffix,side+"IndexProximal",side+"HandIndex1");
        arm.middle=Bone("Middle Proximal"+suffix,side+"MiddleProximal",side+"HandMiddle1");
        arm.little=Bone("Little Proximal"+suffix,side+"LittleProximal",side+"HandPinky1");
        Remember(arm.upper); Remember(arm.lower); Remember(arm.hand);
        if(arm.Valid) arm.baseReach=(Vector3.Distance(arm.upper.position,arm.lower.position)+Vector3.Distance(arm.lower.position,arm.hand.position))/stage.lossyScale.x;
        if(arm.hand!=null && arm.index!=null && arm.middle!=null && arm.little!=null)
            arm.basis=Quaternion.Inverse(arm.hand.rotation)*PalmBasis(arm.hand.position,arm.index.position,arm.middle.position,arm.little.position);
        if(arm.hand!=null && arm.middle!=null) arm.palmLength=stage.InverseTransformVector(arm.middle.position-arm.hand.position).magnitude;
        if(arm.index!=null && arm.little!=null) arm.palmWidth=stage.InverseTransformVector(arm.index.position-arm.little.position).magnitude;
        var fingers=new List<Finger>(); string[] names={"Thumb","Index","Middle","Ring","Little"};
        string[] joints={"Proximal","Intermediate","Distal"}; int[] starts={2,7,12,17,22};
        for(int f=0;f<5;f++) for(int j=0;j<3;j++) {
            var bone=Bone(names[f]+" "+joints[j]+suffix,side+names[f]+joints[j],side+"Hand"+(f==4?"Pinky":names[f])+(j+1));
            if(bone==null) continue;
            Remember(bone); Vector3 tail=Vector3.zero;
            if(j<2) {
                var next=Bone(names[f]+" "+joints[j+1]+suffix,side+names[f]+joints[j+1],side+"Hand"+(f==4?"Pinky":names[f])+(j+2));
                if(next!=null) tail=bone.InverseTransformPoint(next.position);
            }
            if(tail.sqrMagnitude<1e-8f) tail=bone.childCount>0?bone.GetChild(0).localPosition:Vector3.zero;
            if(tail.sqrMagnitude<1e-8f && bone.parent!=null) tail=bone.InverseTransformVector(bone.position-bone.parent.position)*.65f;
            fingers.Add(new Finger { bone=bone,joint=starts[f]+j,segment=f*3+j,axis=tail.normalized,tail=tail });
        }
        arm.fingers=fingers.ToArray(); return arm;
    }
    public void SetActive(bool value) {
        if(!value) AutomaticFit?.Cancel();
        if(value && Active) return;
        if(value && Avatar!=null && Avatar.GetComponent<AvatarViewer.AvatarPackageInfo>()?.headMaskBaked!=true) { RigStatus="模型需要用新版上传插件重新转换后才能附身"; return; }
        Active=value && Head!=null; Calibrated=false;
        foreach(var chain in footAttachments) { chain.rigidAttachment=Active; chain.ResetSimulation(); }
        RestorePose(); Shader.SetGlobalFloat("_AvatarFirstPerson",0);
        if(Active && !renderPrepared) PrepareFirstPersonMeshes(Avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        if(!Active) RestoreMeshes();
    }
    public void Recalibrate() { Calibrated=false; }
    public bool Calibrate() {
        if(!Active || eyes==null || !Finite(eyes.transform.position)) return false;
        var tracking=eyes.transform.parent;
        float height=tracking!=null?tracking.InverseTransformPoint(eyes.transform.position).y:eyes.transform.position.y;
        if(height<.45f || height>2.5f) return false;
        RestorePose();
        Head.localScale=restScales[Head]*Proportions.Value.head;
        float standingEyeHeight=stage.InverseTransformPoint(EyePosition()).y;
        if(standingEyeHeight<.2f || float.IsNaN(standingEyeHeight) || float.IsInfinity(standingEyeHeight)) return false;
        // Once measured standing, seated/crouched HMD height changes posture,
        // never the person's stature or limb lengths.
        float stature=StandingHeight.Valid?StandingHeight.EyeHeight:height;
        Scale=Mathf.Clamp((stature-FloorOffset)/standingEyeHeight*Proportions.Value.overall,.5f,2.5f);
        floorHeight=(tracking!=null?tracking.position.y:0)+FloorOffset;
        bodyYaw=eyes.transform.eulerAngles.y; Calibrated=true;
        stage.localScale=Vector3.one*Scale; PoseRoot();
        foreach(var chain in Avatar.GetComponentsInChildren<AvatarViewer.SpringChain>(true)) chain.ResetSimulation();
        return true;
    }
    public void SupplyHands(HandJointLocations l,HandJointLocations r) { leftJoints=l; rightJoints=r; }
    void LateUpdate() {
        if(eyes!=null) {
            var tracking=eyes.transform.parent;
            float height=tracking!=null?tracking.InverseTransformPoint(eyes.transform.position).y:eyes.transform.position.y;
            bool tracked=InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.isTracked,out bool currentTracked) && currentTracked && Application.isFocused;
            bool wasBusy=StandingHeight.Busy;
            StandingHeight.Tick(Time.unscaledTime,height,tracked,HeadTracking.FloorOriginReady);
            if(wasBusy && !StandingHeight.Busy && StandingHeight.Succeeded) {
                // Previous reach may have compensated for a seated body scale.
                // Start from authored arm proportions at the new standing size.
                if(Proportions!=null){Proportions.Value.overall=1;Proportions.Value.arms=1;Proportions.Store();PlayerPrefs.Save();}
                Calibrated=false;
            }
        }
        if(AutomaticFit!=null && AutomaticFit.Busy) {
            if(!Active) AutomaticFit.Cancel("已取消 · 请保持附身状态");
            else if(!automaticHeightDone && Time.unscaledTime>=automaticHeightAt) {
                if(Calibrate()) automaticHeightDone=true;
                else AutomaticFit.Cancel("未保存 · 站直并确认地面高度后重试");
            }
        }
        HandCalibration.Tick(leftJoints,rightJoints,eyes!=null?eyes.transform.parent:null,Time.unscaledTime);
        if(Proportions!=null) foreach(var chain in propChains) chain.motionStrength=Proportions.Value.propSway;
        Shader.SetGlobalFloat("_AvatarFirstPerson",Active && Calibrated && Avatar.GetComponent<AvatarViewer.AvatarPackageInfo>()?.originalMaterials!=true?1:0);
        if(!Active || Avatar==null) { AutomaticFit?.Tick(); return; }
        if(!Calibrated && !Calibrate()) return;
        StepPose(Time.deltaTime);
        AutomaticFit?.Tick();
    }
    void OnApplicationFocus(bool focused) { if(!focused) {AutomaticFit?.Cancel("已取消 · 应用已失去焦点");standingHeight?.Cancel();} }
    void OnApplicationPause(bool paused) { if(paused) {AutomaticFit?.Cancel("已取消 · 应用已暂停");standingHeight?.Cancel();} }
    public void StepPose(float delta) {
        if(!Active || !Calibrated) return;
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        var animatedLeft=LeftEye!=null?LeftEye.localRotation:Quaternion.identity;
        var animatedRight=RightEye!=null?RightEye.localRotation:Quaternion.identity;
        RestorePose();
        if(PreserveAnimatedEyes) {
            if(LeftEye!=null) LeftEye.localRotation=animatedLeft;
            if(RightEye!=null) RightEye.localRotation=animatedRight;
        }
        float target=eyes.transform.eulerAngles.y;
        if(Mathf.Abs(Mathf.DeltaAngle(bodyYaw,target))>30) bodyYaw=Mathf.LerpAngle(bodyYaw,target,1-Mathf.Exp(-delta*4));
        PoseRoot();
        PoseLeg(leftLeg); PoseLeg(rightLeg);
        FitShoulders(left); FitShoulders(right); SampleArmLength(Time.unscaledTime);
        LeftTracked=PoseArm(left,leftJoints,true,delta); RightTracked=PoseArm(right,rightJoints,false,delta);
        foreach(var chain in propChains) chain.motionStrength=Proportions.Value.propSway;
        PoseMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
    }
    void PoseRoot() {
        stage.localScale=Vector3.one*Scale;
        stage.SetPositionAndRotation(eyes.transform.position-Quaternion.Euler(0,bodyYaw,0)*(eyeAnchor*Scale),Quaternion.Euler(0,bodyYaw,0));
        Head.rotation=eyes.transform.rotation*headBasis;
        Head.localScale=restScales[Head]*Proportions.Value.head;
        stage.position+=eyes.transform.position-EyePosition();
        // Keep the root on the tracking floor. Crouching lowers the pelvis;
        // leg IK then bends the knees instead of dragging the feet underground.
        if(hips!=null && leftLeg.Valid && rightLeg.Valid) {
            float adjustment=floorHeight-stage.position.y;
            stage.position+=Vector3.up*adjustment; hips.position-=Vector3.up*adjustment;
        }
    }
    void PoseLeg(Leg leg) {
        if(leg==null || !leg.Valid) return;
        var target=stage.position+stage.rotation*(leg.anchor*Scale);
        var hint=leg.upper.position+stage.rotation*new Vector3(0,-.3f,.45f)*Scale;
        SolveArm(leg.upper,leg.lower,leg.foot,target,hint);
        // Imported ankle axes are not Unity's forward axis. Preserve the
        // original anatomical orientation, including the model's heel pitch.
        leg.foot.rotation=stage.rotation*leg.footBasis;
    }
    Vector3 EyePosition() => LeftEye!=null && RightEye!=null?(LeftEye.position+RightEye.position)*.5f:Head.position+Avatar.transform.up*.065f;
    void FitShoulders(Arm arm) {
        if(arm==null || !arm.Valid || hips==null) return;
        arm.upper.position+=stage.right*Vector3.Dot(arm.upper.position-hips.position,stage.right)*(Proportions.Value.shoulders-1);
    }
    bool FitWholeBodyFromHands() {
        if(!left.Valid || !right.Valid || left.palmLength<=0 || right.palmLength<=0) return false;
        var measured=HandCalibration.Value;
        if(!measured.left.Valid || !measured.right.Valid) return false;
        // A larger hand is not evidence of greater standing height. Oversizing
        // the whole skeleton while pinning the eyes to the HMD forced the legs
        // into a crouch. Fit stature to the floor/eyes and hands independently.
        Proportions.Value.overall=1;
        if(!Calibrate()) return false;
        RestorePose();stage.localScale=Vector3.one*Scale;PoseRoot();FitShoulders(left);FitShoulders(right);
        var tracking=eyes.transform.parent;Vector3 r;
        if(!Joint(leftJoints,1,tracking,out var l)||!Joint(rightJoints,1,tracking,out r)
            || (leftJoints.jointLocations[1].locationStatus&HandLocationStatus.PositionTracked)==0
            || (rightJoints.jointLocations[1].locationStatus&HandLocationStatus.PositionTracked)==0)return false;
        float a=Vector3.Distance(l,left.upper.position)/(left.baseReach*Scale),b=Vector3.Distance(r,right.upper.position)/(right.baseReach*Scale);
        if(a<.7f || a>1.5f || b<.7f || b>1.5f || Mathf.Abs(a-b)>.18f)return false;
        Proportions.Value.arms=(a+b)*.5f;
        return true;
    }
    public void SampleArmLength(float now) {
        if(Proportions==null || !Proportions.Busy || !left.Valid || !right.Valid) return;
        var tracking=eyes.transform.parent;
        Vector3 r=Vector3.zero;
        bool valid=Joint(leftJoints,1,tracking,out var l) && Joint(rightJoints,1,tracking,out r)
            && (leftJoints.jointLocations[1].locationStatus & HandLocationStatus.PositionTracked)!=0
            && (rightJoints.jointLocations[1].locationStatus & HandLocationStatus.PositionTracked)!=0;
        string reason=valid?null:"双手未充分追踪";
        float height=(l.y+r.y)*.5f;
        if(valid && (Mathf.Abs(l.y-r.y)>.12f || height>eyes.transform.position.y-.08f || height<eyes.transform.position.y-.5f)) { valid=false; reason="双手与真实肩部同高，保持水平"; }
        if(valid && (Vector3.Dot(l-left.upper.position,stage.forward)<.25f || Vector3.Dot(r-right.upper.position,stage.forward)<.25f)) { valid=false; reason="双臂向正前方伸直"; }
        // Wrist height cannot identify anatomical shoulder height. Preserve
        // the imported shoulders and fit the actual 3D reach to each wrist.
        Proportions.Tick(now,valid,Vector3.Distance(l,left.upper.position)/(left.baseReach*Scale),Vector3.Distance(r,right.upper.position)/(right.baseReach*Scale),reason);
    }
    bool PoseArm(Arm arm,HandJointLocations joints,bool isLeft,float delta) {
        if(!arm.Valid) return false;
        var tracking=eyes.transform.parent;
        bool valid=Joint(joints,1,tracking,out var target);
        if(!valid) {
            // Controller fallback requires a current tracked pose, never a stale zero position.
            var device=InputDevices.GetDeviceAtXRNode(isLeft?XRNode.LeftHand:XRNode.RightHand);
            if(device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked) && tracked && device.TryGetFeatureValue(CommonUsages.devicePosition,out var p)) {
                target=tracking!=null?tracking.TransformPoint(p):p; valid=true;
            }
        }
        var body=stage.rotation;
        if(!valid) target=arm.upper.position+body*new Vector3(isLeft?-.09f:.09f,-.38f*Scale,.07f*Scale);
        Vector3 hint=arm.upper.position+body*new Vector3(isLeft?-.35f:.35f,-.35f,-.16f)*Scale;
        // Use a calibrated, fixed arm length. Only a small tracking tolerance is
        // allowed; reaching out must not stretch the arm by fifty percent.
        arm.lower.localPosition*=Proportions.Value.arms; arm.hand.localPosition*=Proportions.Value.arms;
        if(valid) {
            float reach=Vector3.Distance(arm.upper.position,arm.lower.position)+Vector3.Distance(arm.lower.position,arm.hand.position);
            float stretch=reach>.001f?Mathf.Clamp((Vector3.Distance(arm.upper.position,target)+.001f)/reach,1,1.03f):1;
            arm.lower.localPosition*=stretch; arm.hand.localPosition*=stretch;
        }
        SolveArm(arm.upper,arm.lower,arm.hand,target,hint);
        if(valid && Joint(joints,7,tracking,out var index) && Joint(joints,12,tracking,out var middle) && Joint(joints,22,tracking,out var little))
            arm.hand.rotation=PalmBasis(target,index,middle,little)*Quaternion.Inverse(arm.basis);
        var settings=HandCalibration.Value; var calibrated=isLeft?settings.left:settings.right;
        float length=calibrated.Valid?calibrated.palmLength:0,width=calibrated.Valid?calibrated.palmWidth:0;
        if(valid && !calibrated.Valid) {
            if(Joint(joints,12,tracking,out var knuckle)) length=Vector3.Distance(target,knuckle);
            if(Joint(joints,7,tracking,out var a) && Joint(joints,22,tracking,out var b)) width=Vector3.Distance(a,b);
        }
        float blend=1-Mathf.Exp(-Mathf.Max(0,delta)*8);
        if(length>.035f && length<.15f && arm.palmLength>.001f) arm.fit=Mathf.Lerp(arm.fit,Mathf.Clamp(length*settings.size/(arm.palmLength*Scale),.4f,3),blend);
        if(width>.025f && width<.14f && arm.palmWidth>.001f) arm.widthFit=Mathf.Lerp(arm.widthFit,Mathf.Clamp(width*settings.size*settings.width/(arm.palmWidth*Scale),.4f,3),blend);
        var forward=arm.basis*Vector3.forward; var across=arm.basis*Vector3.right; var normal=arm.basis*Vector3.up;
        var handScale=new Vector3(forward.x*forward.x,forward.y*forward.y,forward.z*forward.z)*arm.fit
            +new Vector3(across.x*across.x,across.y*across.y,across.z*across.z)*arm.widthFit
            +new Vector3(normal.x*normal.x,normal.y*normal.y,normal.z*normal.z)*Mathf.Sqrt(arm.fit*arm.widthFit);
        arm.hand.localScale=Vector3.Scale(restScales[arm.hand],handScale);
        Vector3 Adjust(Vector3 point) { var offset=(point-target)*settings.size; var rightAxis=arm.hand.rotation*across; return arm.hand.position+offset+rightAxis*(Vector3.Dot(offset,rightAxis)*(settings.width-1)); }
        if(valid) foreach(var finger in arm.fingers) {
            if(Joint(joints,finger.joint,tracking,out var from) && Joint(joints,finger.joint+1,tracking,out var to) && (to-from).sqrMagnitude>1e-8f)
            {
                // Use joint positions as well as directions: rotation alone keeps the
                // imported cartoon finger lengths and cannot match the wearer's hand.
                from=Adjust(from); to=Adjust(to); finger.bone.position=from;
                finger.bone.rotation=Quaternion.FromToRotation(finger.bone.TransformDirection(finger.axis),to-from)*finger.bone.rotation;
                float segmentLength=calibrated.Valid && calibrated.segments!=null && calibrated.segments.Length>finger.segment?calibrated.segments[finger.segment]*settings.size:Vector3.Distance(from,to);
                float existing=finger.bone.TransformVector(finger.tail).magnitude;
                if(existing>.001f && segmentLength>.003f) {
                    float ratio=Mathf.Clamp(segmentLength/existing,.4f,3); var scale=finger.bone.localScale;
                    var axis=finger.axis; int component=Mathf.Abs(axis.x)>Mathf.Abs(axis.y)?(Mathf.Abs(axis.x)>Mathf.Abs(axis.z)?0:2):(Mathf.Abs(axis.y)>Mathf.Abs(axis.z)?1:2);
                    scale[component]*=ratio; finger.bone.localScale=scale;
                }
            }
        }
        float error=valid?Vector3.Distance(arm.hand.position,target):-1;
        if(isLeft) {LeftWristError=error;LeftWristTarget=target;} else {RightWristError=error;RightWristTarget=target;}
        return valid;
    }
    public static void SolveArm(Transform upper,Transform lower,Transform hand,Vector3 target,Vector3 hint) {
        Vector3 start=upper.position,axis=target-start;
        float a=Vector3.Distance(start,lower.position),b=Vector3.Distance(lower.position,hand.position);
        if(a<.0001f || b<.0001f || !Finite(target)) return;
        if(axis.sqrMagnitude<1e-8f) axis=Vector3.forward;
        float d=Mathf.Clamp(axis.magnitude,Mathf.Abs(a-b)+.0001f,a+b-.0001f); axis.Normalize();
        Vector3 bend=Vector3.ProjectOnPlane(hint-start,axis);
        if(bend.sqrMagnitude<1e-8f) bend=Vector3.Cross(axis,Mathf.Abs(axis.y)<.9f?Vector3.up:Vector3.right);
        float along=(a*a+d*d-b*b)/(2*d),height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
        Vector3 elbow=start+axis*along+bend.normalized*height;
        upper.rotation=Quaternion.FromToRotation(lower.position-start,elbow-start)*upper.rotation;
        lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,start+axis*d-lower.position)*lower.rotation;
    }
    static Quaternion PalmBasis(Vector3 wrist,Vector3 index,Vector3 middle,Vector3 little) {
        var forward=middle-wrist; var normal=Vector3.Cross(index-wrist,little-wrist);
        return forward.sqrMagnitude>1e-8f && normal.sqrMagnitude>1e-8f?Quaternion.LookRotation(forward,normal):Quaternion.identity;
    }
    public static bool Joint(HandJointLocations joints,int index,Transform tracking,out Vector3 point) {
        point=Vector3.zero;
        if(joints.isActive==0 || joints.jointLocations==null || index>=joints.jointCount || index>=joints.jointLocations.Length) return false;
        var j=joints.jointLocations[index]; if((j.locationStatus & HandLocationStatus.PositionValid)==0) return false;
        var p=j.pose.Position.ToVector3(); point=tracking!=null?tracking.TransformPoint(p):p; return Finite(point);
    }
    public static bool Finite(Vector3 p) => !(float.IsNaN(p.x)||float.IsInfinity(p.x)||float.IsNaN(p.y)||float.IsInfinity(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.z));
    public void RestorePose() { foreach(var item in rest) if(item.Key!=null) { item.Key.localRotation=item.Value; item.Key.localPosition=restPositions[item.Key]; item.Key.localScale=restScales[item.Key]; } }
    void PrepareFirstPersonMeshes(SkinnedMeshRenderer[] skins) {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        bool baked=Avatar.GetComponent<AvatarViewer.AvatarPackageInfo>()?.headMaskBaked==true;
        var info=Avatar.GetComponent<AvatarViewer.AvatarPackageInfo>();
        if(info!=null && info.originalMaterials) foreach(var variant in info.meshVariants) {
            if(variant.renderer is SkinnedMeshRenderer s) { originals.Add(Tuple.Create(s,s.sharedMesh)); s.sharedMesh=variant.firstPerson; }
            else if(variant.renderer!=null) { var f=variant.renderer.GetComponent<MeshFilter>(); if(f!=null) {originalFilters.Add(Tuple.Create(f,f.sharedMesh)); f.sharedMesh=variant.firstPerson;} }
        }
        foreach(var skin in skins) {
            skin.gameObject.layer=30;
            // IK can move arms beyond the bounds imported for the rest pose.
            // This also applies to pre-baked meshes, which skip the branch below.
            skin.updateWhenOffscreen=true;
            if(baked || !skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh==null || !skin.sharedMesh.isReadable) continue;
            var flags=AvatarViewer.AvatarHeadMask.HeadBones(skin.bones);
            if(Array.IndexOf(flags,true)<0) continue;
            var source=skin.sharedMesh; var mesh=Instantiate(source); var weights=mesh.boneWeights;
            var markers=new List<Vector2>(mesh.vertexCount);
            for(int i=0;i<mesh.vertexCount;i++) {
                float weight=0;
                if(weights.Length==mesh.vertexCount) {
                    var w=weights[i]; weight=AvatarViewer.AvatarHeadMask.Weight(flags,w.boneIndex0,w.weight0)+AvatarViewer.AvatarHeadMask.Weight(flags,w.boneIndex1,w.weight1)+AvatarViewer.AvatarHeadMask.Weight(flags,w.boneIndex2,w.weight2)+AvatarViewer.AvatarHeadMask.Weight(flags,w.boneIndex3,w.weight3);
                }
                markers.Add(new Vector2(weight,0));
            }
            mesh.SetUVs(2,markers); originals.Add(Tuple.Create(skin,source)); skin.sharedMesh=mesh; ownedMeshes.Add(mesh);
            skin.updateWhenOffscreen=true;
        }
        foreach(var renderer in Avatar.GetComponentsInChildren<Renderer>(true)) {
            if(!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            renderer.gameObject.layer=30;
            if(portableShader!=null && info?.originalMaterials!=true) {
                var previous=renderer.sharedMaterials; originalMaterials.Add(Tuple.Create(renderer,previous));
                renderer.sharedMaterials=previous.Select(material=>{
                    if(material==null) return null;
                    if(!ownedMaterials.TryGetValue(material,out var copy)) { copy=new Material(material) { shader=portableShader }; ownedMaterials.Add(material,copy); }
                    return copy;
                }).ToArray();
            }
            var block=new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            block.SetFloat("_AvatarHeadObject",BelongsToHead(renderer.transform)?1:0); renderer.SetPropertyBlock(block);
        }
        Debug.Log("AVATAR_EMBODIMENT prepared baked="+baked+" runtimeMeshCopies="+ownedMeshes.Count+" ms="+timer.ElapsedMilliseconds);
        renderPrepared=true;
    }
    bool BelongsToHead(Transform bone) {
        for(var t=bone;t!=null && t!=Avatar.transform;t=t.parent) if(t==Head || Canonical(t.name)=="head") return true;
        return false;
    }
    public void Unbind() {
        standingHeight?.Cancel();
        AutomaticFit?.Cancel(); AutomaticFit=null;
        if(Avatar!=null) foreach(var chain in Avatar.GetComponentsInChildren<AvatarViewer.SpringChain>(true)) {chain.garmentCollision=null; chain.motionStrength=1;}
        propChains=new AvatarViewer.SpringChain[0];
        foreach(var chain in footAttachments) if(chain!=null) { chain.rigidAttachment=false; chain.ResetSimulation(); } footAttachments=new AvatarViewer.SpringChain[0];
        Active=Calibrated=false; RestorePose(); rest.Clear(); restPositions.Clear(); restScales.Clear(); humanoid.Clear();
        RestoreMeshes();
        Avatar=null; Head=LeftEye=RightEye=null; Shader.SetGlobalFloat("_AvatarFirstPerson",0);
    }
    void RestoreMeshes() {
        renderPrepared=false;
        foreach(var item in originals) if(item.Item1!=null) item.Item1.sharedMesh=item.Item2; originals.Clear();
        foreach(var item in originalFilters) if(item.Item1!=null) item.Item1.sharedMesh=item.Item2; originalFilters.Clear();
        foreach(var mesh in ownedMeshes) if(mesh!=null) { if(Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); } ownedMeshes.Clear();
        foreach(var item in originalMaterials) if(item.Item1!=null) item.Item1.sharedMaterials=item.Item2; originalMaterials.Clear();
        foreach(var material in ownedMaterials.Values) if(Application.isPlaying) Destroy(material); else DestroyImmediate(material); ownedMaterials.Clear();
    }
    void OnDisable() { AutomaticFit?.Cancel("已取消 · 原设置保持不变");standingHeight?.Cancel(); Shader.SetGlobalFloat("_AvatarFirstPerson",0); }
    void OnDestroy() { Unbind(); }
}
