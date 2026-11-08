using System.Collections.Generic;
using UnityEngine;

namespace AvatarViewer
{
    // Independent secondary-motion solver. No VRChat runtime code is included.
    public sealed class SpringChain : MonoBehaviour
    {
        public Transform root;
        public Transform[] ignored = new Transform[0];
        public SpringCollider[] colliders = new SpringCollider[0];
        [System.NonSerialized] public SpringCollider[] interactionColliders=new SpringCollider[0];
        [System.NonSerialized] public System.Func<Transform,Vector3,Vector3,Vector3,Vector3> garmentCollision;
        [System.NonSerialized] public float motionStrength=1;
        public int HandContactEvents { get; private set; }
        public bool HasHandGrab => grabbedJoint>=0;
        int grabbedJoint=-1;
        Vector3 handTarget,grabOffset;
        public Vector3 endpoint;
        [Range(0,1)] public float pull = .2f, spring = .5f, stiffness = .1f, immobile;
        public float gravity, radius = .01f, maxAngle = 180;
        // 0 keeps the legacy cone; 1 none, 2 angle, 3 hinge, 4 polar.
        public int directionalLimit;
        public float maxYaw=180;
        public Vector3 limitRotation;
        public AnimationCurve angleCurve,yawCurve,rotationXCurve,rotationYCurve,rotationZCurve;
        public bool allowHandCollision=true;
        public AnimationCurve radiusCurve = AnimationCurve.Linear(0,1,1,1);
        public bool simulate = true;
        [System.NonSerialized] public bool rigidAttachment;
        public float wind;

        sealed class Joint
        {
            public Transform bone;
            public Quaternion rest;
            public Vector3 localTail, current, previous;
            public float fraction;
            public Quaternion limitFrame;
            public float pitch,yaw;
            public Vector3 lastRestTail;
            public int quietSteps;
        }
        readonly List<Joint> joints = new List<Joint>();
        Vector3 anchor;
        float accumulator;
        bool previousSimulation = true;
        const float Step = 1f / 90f;
        readonly List<SpringCollider> activeColliders=new List<SpringCollider>();
        readonly List<SpringCollider> activeHands=new List<SpringCollider>();
        static int measuredFrame=-1;
        public static double LastFrameMilliseconds { get; private set; }

        void OnEnable() { Rebuild(); }
        void OnDisable() { ReleaseHandGrab(); Restore(); }
        void OnApplicationPause(bool paused) { if (!paused) ResetSimulation(); }
        public void Rebuild()
        {
            Restore(); joints.Clear();
            if (root == null) root = transform;
            Gather(root, new HashSet<Transform>(ignored));
            for (int i=0; i<joints.Count; ++i) {
                var joint=joints[i]; joint.fraction=joints.Count < 2 ? 0 : (float)i/(joints.Count-1);
                float Curve(AnimationCurve curve)=>curve!=null && curve.length>0?curve.Evaluate(joint.fraction):1;
                joint.pitch=Mathf.Clamp(maxAngle*Curve(angleCurve),0,180); joint.yaw=Mathf.Clamp(maxYaw*Curve(yawCurve),0,180);
                var rotation=new Vector3(limitRotation.x*Curve(rotationXCurve),limitRotation.y*Curve(rotationYCurve),limitRotation.z*Curve(rotationZCurve));
                joint.limitFrame=Quaternion.FromToRotation(Vector3.up,joint.localTail.normalized)*Quaternion.Euler(rotation);
            }
            ResetSimulation();
        }
        void Gather(Transform bone, HashSet<Transform> excludes)
        {
            if (excludes.Contains(bone)) return;
            Vector3 tail = Vector3.zero;
            int children = 0;
            foreach (Transform child in bone)
                if (!excludes.Contains(child)) { tail += child.localPosition; children++; }
            if (children > 0) tail /= children; else tail = endpoint;
            if (tail.sqrMagnitude > .0000001f)
                joints.Add(new Joint { bone=bone, rest=bone.localRotation, localTail=tail });
            foreach (Transform child in bone) Gather(child, excludes);
        }
        void Restore() { foreach (var joint in joints) if (joint.bone != null) joint.bone.localRotation = joint.rest; }
        public void ResetSimulation()
        {
            ReleaseHandGrab();
            Restore();
            foreach (var joint in joints) if (joint.bone != null) { joint.current=joint.previous=joint.lastRestTail=joint.bone.TransformPoint(joint.localTail); joint.quietSteps=0; }
            anchor = root != null ? root.position : transform.position;
            accumulator = 0;
        }
        void LateUpdate() {
            if(measuredFrame!=Time.frameCount) { measuredFrame=Time.frameCount; LastFrameMilliseconds=0; }
            long start=System.Diagnostics.Stopwatch.GetTimestamp(); Advance(Time.deltaTime);
            LastFrameMilliseconds+=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        }
        public void Advance(float deltaTime)
        {
            if (simulate != previousSimulation) { ResetSimulation(); previousSimulation=simulate; }
            if(rigidAttachment) { Restore(); accumulator=0; return; }
            if (!simulate || root == null) return;
            activeColliders.Clear(); activeHands.Clear();
            foreach(var c in colliders) if(c!=null && c.isActiveAndEnabled) activeColliders.Add(c);
            if(allowHandCollision) foreach(var c in interactionColliders) if(c!=null && c.isActiveAndEnabled) activeHands.Add(c);
            Vector3 motion=root.position-anchor; anchor=root.position;
            if (motion.sqrMagnitude > 1f) { ResetSimulation(); return; }
            foreach (var joint in joints) { joint.current += motion*immobile; joint.previous += motion*immobile; }
            // Bound catch-up work after a dropped frame instead of multiplying a hitch.
            accumulator = Mathf.Min(accumulator + Mathf.Max(0,deltaTime), Step*2);
            while (accumulator >= Step) { Integrate(); accumulator-=Step; }
        }
        public bool FindHandGrab(Vector3 hand,float reach,out int index,out float distance) {
            index=-1; distance=reach;
            if(!simulate || !isActiveAndEnabled || HasHandGrab) return false;
            for(int i=0;i<joints.Count;i++) {
                float d=Vector3.Distance(hand,joints[i].current);
                if(d<distance) { index=i; distance=d; }
            }
            return index>=0;
        }
        public bool BeginHandGrab(int index,Vector3 hand) {
            if(index<0 || index>=joints.Count || !simulate) return false;
            grabbedJoint=index; grabOffset=joints[index].current-hand; handTarget=hand; return true;
        }
        public void MoveHandGrab(Vector3 hand) { handTarget=hand; }
        public void ReleaseHandGrab() {
            grabbedJoint=-1;
            foreach(var joint in joints) joint.previous=joint.current;
        }
        void Integrate()
        {
            Restore();
            for(int jointIndex=0;jointIndex<joints.Count;jointIndex++)
            {
                var joint=joints[jointIndex];
                if (joint.bone == null) continue;
                var start = joint.bone.position;
                var restTail = joint.bone.TransformPoint(joint.localTail);
                float length = Vector3.Distance(start, restTail);
                if (length < .00001f) continue;
                var restDirection = (restTail-start)/length;
                var frame=joint.bone.rotation*joint.limitFrame;
                // Very high retained velocity makes long cloth chains amplify
                // tiny tracking changes down to their final bones.
                var velocity = (joint.current-joint.previous) * Mathf.Lerp(.65f,.90f,spring);
                var next = joint.current + velocity + (restTail-joint.current)*Mathf.Lerp(.005f,.15f,pull);
                next += (Vector3.down*gravity + Vector3.right*(Mathf.Sin(Time.time*2f)*wind)) * (length*.003f);
                if(jointIndex==grabbedJoint) next=handTarget+grabOffset;
                var direction = (next-start).normalized;
                direction = Vector3.Slerp(direction,restDirection,stiffness*.2f);
                float angle = Vector3.Angle(restDirection,direction);
                if (directionalLimit==0 && angle > maxAngle && angle > .0001f) direction=Vector3.Slerp(restDirection,direction,maxAngle/angle);
                if(directionalLimit>0) direction=LimitDirection(direction,frame,directionalLimit,joint.pitch,joint.yaw);
                next=start+direction*length;
                var scale=joint.bone.lossyScale;
                float particleRadius=radius*radiusCurve.Evaluate(joint.fraction)*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
                Vector3 contactNormal=Vector3.zero;
                bool garmentContact=false;
                for (int pass=0; pass<2; pass++)
                {
                    foreach (var c in activeColliders) {
                        var resolved=c.Resolve(next,particleRadius); contactNormal+=resolved-next; next=resolved;
                    }
                    foreach(var c in activeHands) if(jointIndex!=grabbedJoint) {
                        var resolved=c.Resolve(next,particleRadius);
                        if((resolved-next).sqrMagnitude>.00000001f) HandContactEvents++;
                        contactNormal+=resolved-next;
                        next=resolved;
                    }
                    if(garmentCollision!=null) { var resolved=garmentCollision(joint.bone,start,next,restDirection); if((resolved-next).sqrMagnitude>1e-10f) garmentContact=true; contactNormal+=resolved-next; next=resolved; }
                    direction=(next-start).normalized;
                    if (direction.sqrMagnitude < .0001f) direction=restDirection;
                    // Collision projection must not bypass the authored angle
                    // limit and flip the cloth to the other side of its anchor.
                    angle=Vector3.Angle(restDirection,direction);
                    if(directionalLimit==0 && angle>maxAngle && angle>.0001f) direction=Vector3.Slerp(restDirection,direction,maxAngle/angle);
                    if(directionalLimit>0) direction=LimitDirection(direction,frame,directionalLimit,joint.pitch,joint.yaw);
                    next=start+direction*length;
                }
                // Scale the physical displacement around the authored rest pose.
                // This also scales internal state, so reduced motion cannot
                // accumulate hidden energy and snap on a later frame.
                if(motionStrength<.999f) { direction=Vector3.Slerp(restDirection,direction,Mathf.Clamp01(motionStrength)); next=start+direction*length; }
                // Let settled contacts sleep instead of continuously exciting
                // millimetre-scale changes down long decorative chains. Moving
                // the anchor, a hand grab or a larger force wakes it immediately.
                float quietDistance=.00075f*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
                float quietSquared=quietDistance*quietDistance;
                if(jointIndex!=grabbedJoint && (next-joint.current).sqrMagnitude<quietSquared && (restTail-joint.lastRestTail).sqrMagnitude<quietSquared) joint.quietSteps++;
                else joint.quietSteps=0;
                joint.lastRestTail=restTail;
                bool sleeping=joint.quietSteps>=45;
                if(sleeping) { direction=(joint.current-start).normalized; if(direction.sqrMagnitude<.001f) direction=restDirection; next=start+direction*length; }
                var displacement=next-joint.current;
                joint.previous=joint.current; joint.current=next;
                // A positional collision correction is not impact velocity.
                // Remove its normal component rather than reinjecting it as
                // spring energy on the following frame.
                if(contactNormal.sqrMagnitude>.0000000001f) joint.previous=next-Vector3.ProjectOnPlane(displacement,contactNormal.normalized)*.5f;
                if(garmentContact) joint.previous=next;
                if(sleeping) joint.previous=next;
                joint.bone.rotation=Quaternion.FromToRotation(restDirection,direction)*joint.bone.rotation;
            }
        }
        public static Vector3 LimitDirection(Vector3 direction,Quaternion frame,int type,float pitchLimit,float yawLimit) {
            if(type==1) return direction;
            var local=Quaternion.Inverse(frame)*direction.normalized;
            if(type==2) { float angle=Vector3.Angle(Vector3.up,local); if(angle>pitchLimit && angle>.0001f) local=Vector3.Slerp(Vector3.up,local,pitchLimit/angle); }
            else {
                float sign=local.z>=0?1:-1;
                float pitch=Mathf.Atan2(Mathf.Sqrt(local.x*local.x+local.z*local.z)*sign,local.y)*Mathf.Rad2Deg;
                float yaw=type==3?0:Mathf.Atan2(local.x*sign,local.z*sign)*Mathf.Rad2Deg;
                pitch=Mathf.Clamp(pitch,-pitchLimit,pitchLimit)*Mathf.Deg2Rad; yaw=Mathf.Clamp(yaw,-yawLimit,yawLimit)*Mathf.Deg2Rad;
                local=new Vector3(Mathf.Sin(pitch)*Mathf.Sin(yaw),Mathf.Cos(pitch),Mathf.Sin(pitch)*Mathf.Cos(yaw));
            }
            return (frame*local).normalized;
        }
    }
}
