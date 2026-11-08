using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarViewer {
    // Standalone constraint data and evaluation. No platform SDK executes on device.
    public abstract class AvatarConstraint : MonoBehaviour {
        [Serializable] public struct Source {
            public Transform SourceTransform; public float Weight;
            public Vector3 ParentPositionOffset,ParentRotationOffset;
        }
        // Unity does not animate arbitrary MonoBehaviour array elements. Fixed
        // source slots retain the same animation paths used by the source SDK.
        [Serializable] public struct SourceList : IEnumerable<Source> {
            public Source source0,source1,source2,source3,source4,source5,source6,source7,source8,source9,source10,source11,source12,source13,source14,source15;
            public int count;
            public int Length => count;
            public SourceList(Source[] values) { this=default; count=Math.Min(values.Length,16); for(int i=0;i<count;i++) this[i]=values[i]; }
            public Source this[int i] {
                get { switch(i) {case 0:return source0;case 1:return source1;case 2:return source2;case 3:return source3;case 4:return source4;case 5:return source5;case 6:return source6;case 7:return source7;case 8:return source8;case 9:return source9;case 10:return source10;case 11:return source11;case 12:return source12;case 13:return source13;case 14:return source14;case 15:return source15;default:throw new IndexOutOfRangeException();} }
                set {switch(i) {case 0:source0=value;break;case 1:source1=value;break;case 2:source2=value;break;case 3:source3=value;break;case 4:source4=value;break;case 5:source5=value;break;case 6:source6=value;break;case 7:source7=value;break;case 8:source8=value;break;case 9:source9=value;break;case 10:source10=value;break;case 11:source11=value;break;case 12:source12=value;break;case 13:source13=value;break;case 14:source14=value;break;case 15:source15=value;break;default:throw new IndexOutOfRangeException();} }
            }
            public struct Enumerator : IEnumerator<Source> { readonly SourceList list; int i; public Enumerator(SourceList value) {list=value;i=-1;} public Source Current=>list[i]; object IEnumerator.Current=>Current; public bool MoveNext()=>++i<list.count; public void Reset(){i=-1;} public void Dispose(){} }
            public Enumerator GetEnumerator()=>new Enumerator(this);
            IEnumerator<Source> IEnumerable<Source>.GetEnumerator()=>GetEnumerator(); IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        public bool IsActive=true,SolveInLocalSpace,FreezeToWorld,RebakeOffsetsWhenUnfrozen;
        public float GlobalWeight=1;
        public Transform TargetTransform;
        public SourceList Sources=new SourceList();
        public Vector3 PositionAtRest,RotationAtRest,ScaleAtRest=Vector3.one;
        public Vector3 PositionOffset,RotationOffset,ScaleOffset=Vector3.one;
        public bool AffectsPositionX=true,AffectsPositionY=true,AffectsPositionZ=true;
        public bool AffectsRotationX=true,AffectsRotationY=true,AffectsRotationZ=true;
        public bool AffectsScaleX=true,AffectsScaleY=true,AffectsScaleZ=true;
        public Vector3 AimAxis=Vector3.forward,UpAxis=Vector3.up,WorldUpVector=Vector3.up;
        public Transform WorldUpTransform; public string WorldUp="SceneUp";
        public abstract int Kind { get; } // parent, position, rotation, scale, aim
        public Transform Target => TargetTransform!=null?TargetTransform:transform;
        bool frozen; Vector3 frozenPosition,frozenScale; Quaternion frozenRotation;
        static Vector3 Divide(Vector3 a,Vector3 b) => new Vector3(Mathf.Abs(b.x)>1e-7f?a.x/b.x:0,Mathf.Abs(b.y)>1e-7f?a.y/b.y:0,Mathf.Abs(b.z)>1e-7f?a.z/b.z:0);
        static Vector3 Axes(Vector3 current,Vector3 value,bool x,bool y,bool z) => new Vector3(x?value.x:current.x,y?value.y:current.y,z?value.z:current.z);
        public void Evaluate() {
            if(!isActiveAndEnabled || !IsActive) return;
            var t=Target; var parent=t.parent;
            if(FreezeToWorld) {
                if(!frozen) { frozenPosition=t.position; frozenRotation=t.rotation; frozenScale=t.lossyScale; frozen=true; }
                t.SetPositionAndRotation(frozenPosition,frozenRotation); t.localScale=parent!=null?Divide(frozenScale,parent.lossyScale):frozenScale; return;
            }
            if(frozen && RebakeOffsetsWhenUnfrozen && Kind==0) for(int i=0;i<Sources.Length;i++) {
                var s=Sources[i]; if(s.SourceTransform==null) continue;
                s.ParentPositionOffset=s.SourceTransform.InverseTransformPoint(t.position);
                s.ParentRotationOffset=(Quaternion.Inverse(s.SourceTransform.rotation)*t.rotation).eulerAngles; Sources[i]=s;
            }
            frozen=false;
            Vector3 restPosition=SolveInLocalSpace || parent==null?PositionAtRest:parent.TransformPoint(PositionAtRest);
            Quaternion restRotation=SolveInLocalSpace || parent==null?Quaternion.Euler(RotationAtRest):parent.rotation*Quaternion.Euler(RotationAtRest);
            Vector3 restScale=SolveInLocalSpace || parent==null?ScaleAtRest:Vector3.Scale(parent.lossyScale,ScaleAtRest);
            float sum=0; foreach(var s in Sources) if(s.SourceTransform!=null) sum+=Mathf.Max(0,s.Weight);
            float normalization=Mathf.Max(1,sum),remaining=Mathf.Max(0,1-sum);
            Vector3 position=restPosition*remaining,scale=restScale*remaining;
            Quaternion rotation=restRotation; float rotationWeight=remaining;
            foreach(var s in Sources) {
                var source=s.SourceTransform; if(source==null || s.Weight<=0) continue;
                float weight=s.Weight/normalization;
                Vector3 p=SolveInLocalSpace?source.localPosition:source.position;
                Quaternion r=SolveInLocalSpace?source.localRotation:source.rotation;
                if(Kind==0) { p+=r*Vector3.Scale(SolveInLocalSpace?source.localScale:source.lossyScale,s.ParentPositionOffset); r*=Quaternion.Euler(s.ParentRotationOffset); }
                position+=p*weight; scale+=(SolveInLocalSpace?source.localScale:source.lossyScale)*weight;
                rotation=Quaternion.Slerp(rotation,r,weight/Mathf.Max(.00001f,rotationWeight+weight)); rotationWeight+=weight;
            }
            float blend=Mathf.Clamp01(GlobalWeight);
            if(Kind==0 || Kind==1) {
                var p=Vector3.Lerp(restPosition,position+(Kind==1?PositionOffset:Vector3.zero),blend);
                if(!SolveInLocalSpace && parent!=null) p=parent.InverseTransformPoint(p);
                t.localPosition=Axes(t.localPosition,p,AffectsPositionX,AffectsPositionY,AffectsPositionZ);
            }
            if(Kind==0 || Kind==2 || Kind==4) {
                if(Kind==4) {
                    Vector3 target=position-(SolveInLocalSpace?t.localPosition:t.position),up=WorldUpVector;
                    if(WorldUp=="SceneUp") up=Vector3.up;
                    else if(WorldUp=="ObjectUp" && WorldUpTransform!=null) up=WorldUpTransform.position-t.position;
                    else if(WorldUp=="ObjectRotationUp" && WorldUpTransform!=null) up=WorldUpTransform.TransformDirection(WorldUpVector);
                    else if(WorldUp=="None") up=t.up;
                    if(target.sqrMagnitude>1e-8f && Vector3.Cross(target,up).sqrMagnitude>1e-8f) rotation=Quaternion.LookRotation(target,up)*Quaternion.Inverse(Quaternion.LookRotation(AimAxis,UpAxis));
                }
                if(Kind!=0) rotation*=Quaternion.Euler(RotationOffset);
                var q=Quaternion.Slerp(restRotation,rotation,blend);
                if(!SolveInLocalSpace && parent!=null) q=Quaternion.Inverse(parent.rotation)*q;
                t.localRotation=Quaternion.Euler(Axes(t.localEulerAngles,q.eulerAngles,AffectsRotationX,AffectsRotationY,AffectsRotationZ));
            }
            if(Kind==3) {
                var s=Vector3.Lerp(restScale,Vector3.Scale(scale,ScaleOffset),blend);
                if(!SolveInLocalSpace && parent!=null) s=Divide(s,parent.lossyScale);
                t.localScale=Axes(t.localScale,s,AffectsScaleX,AffectsScaleY,AffectsScaleZ);
            }
        }
    }
}
