using System;
using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;

// Personal hand dimensions, independent of the imported model and its height.
public sealed class HandSizeCalibration {
    public const string PreferenceKey="AvatarHandCalibration.v1";
    [Serializable] public sealed class Side {
        public float palmLength,palmWidth;
        public float[] segments=new float[15];
        public bool Valid=>palmLength>.035f && palmWidth>.025f;
    }
    [Serializable] public sealed class Settings {
        public Side left=new Side(),right=new Side();
        public float size=1,width=1;
    }
    public Settings Value { get; private set; }
    public bool Busy { get; private set; }
    public bool LeftSucceeded { get; private set; }
    public bool RightSucceeded { get; private set; }
    bool saveOnFinish=true;
    public string Status { get; private set; }="未校准 · 张开双手后开始";
    readonly List<float[]> leftSamples=new List<float[]>(),rightSamples=new List<float[]>();
    float collectAt,finishAt;
    public HandSizeCalibration() {
        try { Value=JsonUtility.FromJson<Settings>(PlayerPrefs.GetString(PreferenceKey,"")); } catch { Value=null; }
        if(Value==null || Value.left==null || Value.right==null) Value=new Settings();
        Value.size=Mathf.Clamp(Value.size,.65f,1.75f); Value.width=Mathf.Clamp(Value.width,.65f,1.75f);
        if(Value.left.Valid || Value.right.Valid) Status="已载入个人手部校准";
    }
    public void Begin(float now,bool persist=true) { Busy=true; LeftSucceeded=RightSucceeded=false; saveOnFinish=persist; collectAt=now+3; finishAt=collectAt+1.5f; leftSamples.Clear(); rightSamples.Clear(); Status="3 秒后采集 · 张开双手，掌心朝自己"; }
    internal void Store() { PlayerPrefs.SetString(PreferenceKey,JsonUtility.ToJson(Value)); }
    void Save() { Store(); PlayerPrefs.Save(); }
    internal void Restore(Settings original) { Busy=false; LeftSucceeded=RightSucceeded=false; Value=original; Status="已保留原手部设置"; }
    public void Adjust(float size,float width) { Value.size=Mathf.Clamp(Value.size+size,.65f,1.75f); Value.width=Mathf.Clamp(Value.width+width,.65f,1.75f); Save(); }
    public void Reset() { Busy=false; Value=new Settings(); Status="已重置 · 张开双手后重新校准"; Save(); }
    public void Tick(HandJointLocations left,HandJointLocations right,Transform tracking,float now) {
        if(!Busy) return;
        if(now<collectAt) { Status=Mathf.CeilToInt(collectAt-now)+" 秒后采集 · 张开双手，掌心朝自己"; return; }
        if(now<=finishAt) {
            Add(left,leftSamples,tracking); Add(right,rightSamples,tracking);
            Status="正在采集 · 左 "+leftSamples.Count+" / 右 "+rightSamples.Count+" · 保持张开"; return;
        }
        bool l=Finish(leftSamples,Value.left),r=Finish(rightSamples,Value.right); Busy=false; LeftSucceeded=l; RightSucceeded=r;
        if(l || r) { if(saveOnFinish) Save(); Status=saveOnFinish?(l && r?"双手校准已保存":"已保存"+(l?"左手":"右手")+" · 另一只手未充分追踪，可重试"):"双手尺寸已采集 · 等待臂长校准"; }
        else Status="未采集到张开的手 · 把双手放入视野后重试";
    }
    static void Add(HandJointLocations joints,List<float[]> samples,Transform tracking) {
        if(samples.Count>=90 || joints.isActive==0 || joints.jointLocations==null || joints.jointCount<26 || joints.jointLocations.Length<26 || (joints.jointLocations[1].locationStatus & HandLocationStatus.PositionTracked)==0) return;
        bool Point(int i,out Vector3 p)=>AvatarEmbodiment.Joint(joints,i,tracking,out p);
        if(!Point(1,out var wrist) || !Point(7,out var index) || !Point(12,out var middle) || !Point(22,out var little) || !Point(15,out var tip)) return;
        float length=Vector3.Distance(wrist,middle),width=Vector3.Distance(index,little);
        if(length<.035f || length>.14f || width<.025f || width>.13f || Vector3.Distance(wrist,tip)<length*1.45f) return;
        var sample=new float[17]; sample[0]=length; sample[1]=width;
        int[] starts={2,7,12,17,22};
        for(int finger=0;finger<5;finger++) for(int segment=0;segment<3;segment++) {
            int joint=starts[finger]+segment; if(!Point(joint,out var a) || !Point(joint+1,out var b)) return;
            float distance=Vector3.Distance(a,b); if(distance<.004f || distance>.09f) return; sample[2+finger*3+segment]=distance;
        }
        samples.Add(sample);
    }
    static bool Finish(List<float[]> samples,Side side) {
        if(samples.Count<15) return false;
        float Median(int index) { var values=new float[samples.Count]; for(int i=0;i<values.Length;i++) values[i]=samples[i][index]; Array.Sort(values); return values[values.Length/2]; }
        side.palmLength=Median(0); side.palmWidth=Median(1); side.segments=new float[15]; for(int i=0;i<15;i++) side.segments[i]=Median(i+2); return true;
    }
}
