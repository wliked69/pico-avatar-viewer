using UnityEngine;

public sealed class ModelHandManipulation
{
    public struct Hand { public bool valid,pinched; public Vector3 position; public Quaternion rotation; }
    public int Mode { get; private set; }
    public int GrabCount { get; private set; }
    public Quaternion ViewRotation=Quaternion.identity;
    public float DepthGain=3f;
    Hand lastLeft,lastRight;
    float lostLeft,lostRight;
    Vector3 startHand,startPosition;
    Quaternion dragFrame;
    float startYaw,startSize,startHandYaw,startDistance,gain;
    public void Cancel() { Mode=0; lostLeft=lostRight=1; lastLeft=lastRight=default; }
    Hand Stabilize(Hand input,ref Hand last,ref float lost,float dt) {
        if(input.valid) { last=input; lost=0; return input; }
        lost+=dt;
        // Keep the last pose for short occlusions, but honor a deliberate release sooner.
        if(last.valid && last.pinched && lost<.18f) return last;
        return input;
    }
    public void Step(Hand left,Hand right,bool canAcquire,ref Vector3 position,ref float yaw,ref float size,float dt=1f/90f)
    {
        dt=Mathf.Clamp(dt,0,.25f);
        bool freeze=(Mode==1 && !left.valid) || (Mode==2 && !right.valid) || (Mode==3 && (!left.valid || !right.valid));
        left=Stabilize(left,ref lastLeft,ref lostLeft,dt);
        right=Stabilize(right,ref lastRight,ref lostRight,dt);
        int mask=(left.valid && left.pinched?1:0)|(right.valid && right.pinched?2:0);
        if(Mode==0) {
            // Allow pinch-first / look-second as well as look-first / pinch-second.
            if(canAcquire && mask!=0) { Mode=mask; GrabCount++; Capture(left,right,position,yaw,size); }
        } else if(mask!=Mode) {
            Mode=mask;
            if(Mode!=0) Capture(left,right,position,yaw,size);
        }
        if(Mode==0 || freeze) return;
        Vector3 hand=Mode==3?(left.position+right.position)*.5f:(Mode==1?left.position:right.position);
        Vector3 delta=Quaternion.Inverse(dragFrame)*(hand-startHand);
        delta=new Vector3(delta.x*1.4f,delta.y,delta.z*gain);
        Vector3 target=startPosition+dragFrame*delta;
        float blend=1-Mathf.Exp(-dt/.045f);
        position=Vector3.Lerp(position,target,blend);
        if(Mode==3) {
            float distance=Vector3.Distance(left.position,right.position);
            size=Mathf.Lerp(size,Mathf.Clamp(startSize*distance/startDistance,.15f,3f),blend);
            Vector3 axis=right.position-left.position;
            if(new Vector2(axis.x,axis.z).magnitude>.08f) {
                float targetYaw=startYaw+Mathf.DeltaAngle(startHandYaw,Mathf.Atan2(axis.z,axis.x)*Mathf.Rad2Deg);
                yaw=Mathf.LerpAngle(yaw,targetYaw,blend);
            }
        }
        // One hand only translates. Wrist orientation is deliberately ignored.
    }
    void Capture(Hand left,Hand right,Vector3 position,float yaw,float size) {
        startPosition=position; startYaw=yaw; startSize=size;
        dragFrame=Quaternion.Euler(0,ViewRotation.eulerAngles.y,0); gain=Mathf.Clamp(DepthGain,1,4);
        startHand=Mode==3?(left.position+right.position)*.5f:(Mode==1?left.position:right.position);
        if(Mode==3) {
            Vector3 axis=right.position-left.position;
            startDistance=Mathf.Max(.01f,axis.magnitude);
            startHandYaw=Mathf.Atan2(axis.z,axis.x)*Mathf.Rad2Deg;
        }
    }
}
