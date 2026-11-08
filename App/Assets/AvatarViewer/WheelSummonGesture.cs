using UnityEngine;
using ByteDance.PICO.XR;

// Independent, same-hand thumb/little contact. A held pose fires once and
// must visibly separate before rearming; missing tracking never counts as contact.
public sealed class WheelSummonGesture {
    sealed class HandState {public bool contact,latched;public float held,released;}
    readonly HandState left=new HandState(),right=new HandState();
    float lastTime=-1;bool selectionNeedsRelease;
    public bool Enabled=>true;
    public float HoldSeconds=>2f;
    public int LastHand {get;private set;}
    public bool ContactActive=>left.contact || right.contact;
    public float Progress=>Mathf.Clamp01(Mathf.Max(left.held,right.held)/HoldSeconds);
    public int ProgressHand=>right.held>left.held?1:0;
    public float LeftGap {get;private set;}=-1;
    public float RightGap {get;private set;}=-1;
    public int SummonCount {get;private set;}
    public void Cancel() {left.contact=right.contact=false;left.held=right.held=left.released=right.released=0;lastTime=-1;selectionNeedsRelease=true;}
    public bool Tick(HandJointLocations l,HandJointLocations r,float now,bool focused) {
        float delta=lastTime<0?0:now-lastTime;lastTime=now;
        if(!Enabled) {left.contact=right.contact=false;left.held=right.held=0;LeftGap=RightGap=-1;return false;}
        if(!focused || delta<0 || delta>.2f) {Cancel();LeftGap=RightGap=-1;return false;}
        bool a=Step(left,l,delta,out float lg),b=Step(right,r,delta,out float rg);LeftGap=lg;RightGap=rg;
        if(ContactActive) selectionNeedsRelease=true;
        if(!a && !b) return false;
        if(left.contact) left.latched=true;if(right.contact) right.latched=true;
        LastHand=a?0:1;SummonCount++;selectionNeedsRelease=true;return true;
    }
    bool Step(HandState state,HandJointLocations joints,float delta,out float gap) {
        gap=-1;
        bool valid=AvatarEmbodiment.Joint(joints,(int)HandJoint.JointThumbTip,null,out var thumb)
            && AvatarEmbodiment.Joint(joints,(int)HandJoint.JointLittleTip,null,out _)
            && AvatarEmbodiment.Joint(joints,(int)HandJoint.JointIndexTip,null,out _)
            && AvatarEmbodiment.Joint(joints,(int)HandJoint.JointWrist,null,out _)
            && (joints.jointLocations[(int)HandJoint.JointWrist].locationStatus & HandLocationStatus.PositionTracked)!=0;
        if(!valid) {state.contact=false;state.held=state.released=0;return false;}
        var little=joints.jointLocations[(int)HandJoint.JointLittleTip].pose.Position.ToVector3();
        var index=joints.jointLocations[(int)HandJoint.JointIndexTip].pose.Position.ToVector3();
        gap=Vector3.Distance(thumb,little);
        if(gap>.04f) {state.contact=false;state.held=0;state.released+=delta;if(state.released>=.15f)state.latched=false;return false;}
        state.released=0;
        // An ordinary thumb/index pinch, even with curled fingers, is not a summon.
        if(Vector3.Distance(thumb,index)<.035f || (gap>.025f && !state.contact)) {state.contact=false;state.held=0;return false;}
        state.contact=true;
        if(state.latched) return false;
        state.held+=delta;
        if(state.held<HoldSeconds) return false;state.latched=true;return true;
    }
    public bool FilterSelection(bool pressed) {
        if(ContactActive) selectionNeedsRelease=true;
        if(!ContactActive && !pressed) selectionNeedsRelease=false;
        return pressed && !ContactActive && !selectionNeedsRelease;
    }
}
