using ByteDance.PICO.XR;
using UnityEngine;

// Uses the official hand mesh and rig with the same joints as interaction.
public sealed class TrackedVirtualHand : MonoBehaviour
{
    public PXR_Hand rig;
    Renderer[] renderers;
    bool displayAllowed=true;
    HandJointLocations freshJoints;
    public bool Visible { get; private set; }
    void Awake() { Initialize(); }
    void Initialize() {
        if(rig==null) rig=GetComponent<PXR_Hand>();
        if(rig!=null) rig.enabled=false;
        // Hide the entire prefab: imported hand prefabs can have renderers outside Basemesh.
        renderers=GetComponentsInChildren<Renderer>(true);
        SetVisible(false);
    }
    void SetVisible(bool value) {
        if(Visible==value && renderers!=null) { foreach(var r in renderers) if(r.enabled!=value) r.enabled=value; return; }
        Visible=value;
        foreach(var renderer in renderers) renderer.enabled=value;
    }
    public void Apply(HandJointLocations joints) {
        if(renderers==null) Initialize();
        bool valid=displayAllowed && rig!=null && joints.isActive!=0 && joints.jointLocations!=null && joints.jointCount>=26 && joints.jointLocations.Length>=26;
        if(valid) {
            var wrist=joints.jointLocations[(int)HandJoint.JointWrist];
            var flags=HandLocationStatus.PositionValid|HandLocationStatus.OrientationValid|HandLocationStatus.PositionTracked;
            valid=(wrist.locationStatus & flags)==flags && AvatarEmbodiment.Finite(wrist.pose.Position.ToVector3());
        }
        SetVisible(valid);
        if(!valid) return;
        for(int i=0;i<rig.handJoints.Count && i<26;i++) {
            var bone=rig.handJoints[i]; if(bone==null) continue;
            var joint=joints.jointLocations[i];
            if((joint.locationStatus & HandLocationStatus.OrientationValid)==0) continue;
            if(i==(int)HandJoint.JointWrist) {
                bone.localPosition=joint.pose.Position.ToVector3(); bone.localRotation=joint.pose.Orientation.ToQuat();
            } else {
                int parent=(i==0 || i==2 || i==6 || i==11 || i==16 || i==21)?1:i-1;
                bone.localRotation=Quaternion.Inverse(joints.jointLocations[parent].pose.Orientation.ToQuat())*joint.pose.Orientation.ToQuat();
            }
        }
    }
    public void SetDisplayAllowed(bool value) { displayAllowed=value; if(!value) Hide(); }
    void OnEnable() { Application.onBeforeRender+=RefreshBeforeRender; }
    void OnDisable() { Application.onBeforeRender-=RefreshBeforeRender; Hide(); }
    void RefreshBeforeRender() {
        #if UNITY_ANDROID && !UNITY_EDITOR
        if(!displayAllowed || !Application.isFocused || rig==null || PXR_HandTracking.GetActiveInputDevice()!=ActiveInputDevice.HandTrackingActive) { Hide(); return; }
        if(PXR_HandTracking.GetJointLocations(rig.handType,ref freshJoints)) Apply(freshJoints); else Hide();
        #endif
    }
    public void Hide() { if(renderers==null) Initialize(); else SetVisible(false); }
}
