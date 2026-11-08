using AvatarViewer;
using ByteDance.PICO.XR;
using UnityEngine;

public sealed class HandBoneInteraction
{
    public readonly SpringCollider[] Colliders=new SpringCollider[12];
    public int GrabEvents { get; private set; }
    public int ActiveColliders { get { int count=0; foreach(var c in Colliders) if(c!=null && c.enabled) count++; return count; } }
    SpringChain[] chains=new SpringChain[0];
    SpringChain leftGrab,rightGrab;
    readonly int[] bases={3,7,12,17,22};
    readonly int[] tips={5,10,15,20,25};
    public bool LeftGrabbing => leftGrab!=null;
    public bool RightGrabbing => rightGrab!=null;
    public int ContactEvents { get { int count=0; foreach(var chain in chains) if(chain!=null) count+=chain.HandContactEvents; return count; } }
    public HandBoneInteraction() {
        for(int i=0;i<Colliders.Length;i++) {
            var go=new GameObject("Tracked hand bone collider "+i);
            Colliders[i]=go.AddComponent<SpringCollider>(); Colliders[i].enabled=false;
        }
    }
    public void Bind(SpringChain[] value) {
        Release(); chains=value;
        foreach(var chain in chains) chain.interactionColliders=Colliders;
    }
    public void Release() {
        if(leftGrab!=null) leftGrab.ReleaseHandGrab(); if(rightGrab!=null) rightGrab.ReleaseHandGrab();
        leftGrab=rightGrab=null; foreach(var c in Colliders) if(c!=null) c.enabled=false;
    }
    bool Joint(HandJointLocations joints,int index,Transform tracking,out Vector3 point) {
        point=default;
        if(joints.isActive==0 || joints.jointLocations==null || joints.jointCount<=index || joints.jointCount<2 || (joints.jointLocations[1].locationStatus & HandLocationStatus.PositionTracked)==0) return false;
        var joint=joints.jointLocations[index];
        if((joint.locationStatus & HandLocationStatus.PositionValid)==0) return false;
        point=joint.pose.Position.ToVector3(); if(tracking!=null) point=tracking.TransformPoint(point); return AvatarEmbodiment.Finite(point);
    }
    void UpdateColliders(HandJointLocations joints,int offset,Transform tracking) {
        var palm=Colliders[offset];
        palm.enabled=Joint(joints,0,tracking,out var center); palm.radius=.04f; palm.capsule=false;
        if(palm.enabled) palm.transform.position=center;
        for(int i=0;i<5;i++) {
            var c=Colliders[offset+i+1];
            bool a=Joint(joints,bases[i],tracking,out var start); bool b=Joint(joints,tips[i],tracking,out var end);
            c.enabled=a && b;
            if(!c.enabled) continue;
            var axis=end-start; c.radius=i==0?.014f:.011f; c.height=axis.magnitude+2*c.radius; c.capsule=true;
            c.transform.SetPositionAndRotation((start+end)*.5f,axis.sqrMagnitude>.000001f?Quaternion.FromToRotation(Vector3.up,axis):Quaternion.identity);
        }
    }
    void UpdateGrab(ref SpringChain grabbed,ModelHandManipulation.Hand hand,bool acquire) {
        if(grabbed!=null) {
            if(!hand.valid || !hand.pinched || !grabbed.simulate || !grabbed.HasHandGrab) { grabbed.ReleaseHandGrab(); grabbed=null; }
            else grabbed.MoveHandGrab(hand.position);
        }
        if(grabbed!=null || !acquire || !hand.valid || !hand.pinched) return;
        SpringChain nearest=null; int best=-1; float reach=.065f;
        foreach(var chain in chains) {
            if(chain==null) continue;
            if(chain.FindHandGrab(hand.position,reach,out var index,out var distance)) { nearest=chain; best=index; reach=distance; }
        }
        if(nearest!=null && nearest.BeginHandGrab(best,hand.position)) { grabbed=nearest; GrabEvents++; }
    }
    public void Update(HandJointLocations left,HandJointLocations right,Transform tracking,ModelHandManipulation.Hand leftPinch,ModelHandManipulation.Hand rightPinch,bool acquire) {
        UpdateColliders(left,0,tracking); UpdateColliders(right,6,tracking);
        UpdateGrab(ref leftGrab,leftPinch,acquire); UpdateGrab(ref rightGrab,rightPinch,acquire);
    }
    public void Destroy() { Release(); foreach(var collider in Colliders) if(collider!=null) Object.Destroy(collider.gameObject); }
}
