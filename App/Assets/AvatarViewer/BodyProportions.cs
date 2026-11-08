using System;
using System.Collections.Generic;
using UnityEngine;

// Model-specific proportions. Hand dimensions remain in HandSizeCalibration.
public sealed class BodyProportions {
    [Serializable] public sealed class Settings { public float head=.9f,arms=1,shoulders=1,propSway=.65f,shoulderHeight,overall=1; public int fitRevision; }
    public Settings Value { get; private set; }
    public string Key { get; private set; }
    public string Status { get; internal set; }="头部、臂长、肩宽可分别调整";
    public bool Busy { get; private set; }
    public bool CalibrationSucceeded { get; private set; }
    bool saveOnFinish=true;
    float collectAt,finishAt;
    readonly List<float> samples=new List<float>();
    string rejectedReason="双手保持在视野内";
    public int SampleCount=>samples.Count;
    public BodyProportions(string model) {
        Key="AvatarBodyProportions.v1."+model;
        try { Value=JsonUtility.FromJson<Settings>(PlayerPrefs.GetString(Key,"")); } catch { Value=null; }
        if(Value==null) Value=new Settings(); Clamp();
        // 0.3.20 reduced head scale when enlarging the body. Repair those
        // auto-fit profiles once; subsequent manual head adjustments persist.
        if(Value.fitRevision<1) {
            bool legacyAutoFit=Mathf.Abs(Value.overall-1)>.0001f;
            Value.fitRevision=1;
            if(legacyAutoFit) {Value.head=1; Status="已恢复原始头身比例"; Save();}
        }
        if(Value.fitRevision<2) {
            bool resized=Mathf.Abs(Value.overall-1)>.0001f;
            if(resized) {
                // Remove hand-derived stature, retaining approximately the
                // selected physical head size and calibrated arm reach.
                Value.head*=Value.overall;Value.arms*=Value.overall;Value.overall=1;
                Status="已恢复站立身高 · 保留头部大小和臂长校准";
            }
            Value.fitRevision=2;if(resized)Save();
        }
        // 0.3.11 inferred shoulder height from the hands. A low-held pose can
        // lower the shoulder by 25 cm; migrate that setting back to the rig.
        if(Mathf.Abs(Value.shoulderHeight)>.0001f) { Value.shoulderHeight=0; Status="肩位已恢复 · 请重新校准臂长"; Save(); }
    }
    void Clamp() { if(float.IsNaN(Value.overall)||float.IsInfinity(Value.overall)||Value.overall<=0)Value.overall=1; Value.overall=Mathf.Clamp(Value.overall,.85f,1.15f); Value.head=Mathf.Clamp(Value.head,.65f,1.25f); Value.arms=Mathf.Clamp(Value.arms,.7f,1.5f); Value.shoulders=Mathf.Clamp(Value.shoulders,.7f,1.5f); Value.propSway=Mathf.Clamp(Value.propSway,.2f,1); Value.shoulderHeight=Mathf.Clamp(Value.shoulderHeight,-.3f,.3f); }
    internal void Store() { Clamp(); PlayerPrefs.SetString(Key,JsonUtility.ToJson(Value)); }
    void Save() { Store(); PlayerPrefs.Save(); }
    internal void Restore(Settings original) { Busy=false; CalibrationSucceeded=false; Value=original; Clamp(); Status="已保留原身体设置"; }
    public void Adjust(float head,float arms,float shoulders) { Value.head+=head; Value.arms+=arms; Value.shoulders+=shoulders; Save(); }
    public void AdjustSway(float change) { Value.propSway+=change; Save(); }
    public void Reset() { Busy=false; float sway=Value.propSway; Value=new Settings { head=1,propSway=sway }; Status="已恢复模型原始比例"; Save(); }
    public void Begin(float now,bool persist=true) { Busy=true; CalibrationSucceeded=false; saveOnFinish=persist; collectAt=now+3; finishAt=collectAt+4; samples.Clear(); rejectedReason="双手保持在视野内"; Status="3 秒后采集 · 双臂向前伸直，与肩同高"; }
    public void Tick(float now,bool valid,float leftRatio,float rightRatio,string invalidReason=null) {
        if(!Busy) return;
        if(now<collectAt) { Status=Mathf.CeilToInt(collectAt-now)+" 秒后采集 · 双臂向前伸直，与肩同高"; return; }
        if(now<=finishAt) {
            bool range=leftRatio>=.7f && leftRatio<=1.5f && rightRatio>=.7f && rightRatio<=1.5f;
            bool symmetric=Mathf.Abs(leftRatio-rightRatio)<.18f;
            if(valid && range && symmetric && samples.Count<240) samples.Add((leftRatio+rightRatio)*.5f);
            else if(!valid) rejectedReason=invalidReason??"双手未充分追踪";
            else if(!range) rejectedReason="前伸距离超出可校准范围";
            else if(!symmetric) rejectedReason="双臂前伸距离不一致";
            Status=(valid && range && symmetric?"保持伸直":rejectedReason)+" · "+Mathf.CeilToInt(finishAt-now)+" 秒\n有效样本 "+samples.Count+" / 至少 15"; return;
        }
        Busy=false;
        if(samples.Count<15) { Status="未保存 · 采样不足（"+samples.Count+"/15）\n"+rejectedReason+"，请重试"; return; }
        samples.Sort(); Value.arms=samples[samples.Count/2]; Value.shoulderHeight=0; CalibrationSucceeded=true; if(saveOnFinish) Save(); Status=(saveOnFinish?"已保存":"已采集")+" · 臂长 "+Mathf.RoundToInt(Value.arms*100)+"% · 肩位保持原样";
    }
}
