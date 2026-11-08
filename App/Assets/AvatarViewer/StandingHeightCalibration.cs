using System;
using System.Collections.Generic;
using UnityEngine;

// Personal standing eye height, measured in the physical floor tracking space.
// A low head can mean sitting or crouching; it is not a measurement of stature.
public sealed class StandingHeightCalibration {
    public const string PreferenceKey="AvatarStandingEyeHeight.v1";
    public float EyeHeight {get;private set;}
    public bool Valid=>EyeHeight>=.7f && EyeHeight<=2.4f;
    public bool Busy {get;private set;}
    public bool Succeeded {get;private set;}
    public bool HasAttempt {get;private set;}
    public string Status {get;private set;}="请先站直校准身高";
    public string Stance {get;private set;}="未校准站立眼高";
    readonly List<float> samples=new List<float>();
    float collectAt,finishAt,candidateAt;
    string candidate="";
    public StandingHeightCalibration() {EyeHeight=PlayerPrefs.GetFloat(PreferenceKey,0);if(float.IsNaN(EyeHeight)||float.IsInfinity(EyeHeight))EyeHeight=0;}
    public void Begin(float now) {HasAttempt=true;Busy=true;Succeeded=false;samples.Clear();collectAt=now+2;finishAt=collectAt+3;Status="2 秒后采集 · 站直、平视并保持不动";}
    public void ClearStatus() {if(!Busy)HasAttempt=false;}
    public void Cancel(string message="已取消 · 原站立眼高保持不变") {if(!Busy)return;Busy=false;Succeeded=false;Status=message;}
    public void Reject(string message) {HasAttempt=true;Status=message;}
    public void Tick(float now,float height,bool tracked,bool floorOrigin) {
        bool usable=tracked && floorOrigin && !float.IsNaN(height) && !float.IsInfinity(height) && height>=.7f && height<=2.4f;
        if(Valid && usable) {
            float ratio=height/EyeHeight;
            string next=ratio>=.94f?"站立":ratio<=.86f?"坐姿或下蹲":"姿态过渡";
            if(candidate!=next) {candidate=next;candidateAt=now;}
            if(now-candidateAt>=.35f)Stance=next;
        } else {Stance=Valid?"追踪未就绪":"未校准站立眼高";candidate="";}
        if(!Busy)return;
        if(now<collectAt) {Status=Mathf.CeilToInt(collectAt-now)+" 秒后采集 · 站直、平视";return;}
        if(now<=finishAt) {
            if(usable && samples.Count<360)samples.Add(height);
            Status=(!floorOrigin?"等待地面追踪":!usable?"等待有效头部追踪":"保持站直")+" · "+Mathf.CeilToInt(finishAt-now)+" 秒";return;
        }
        Busy=false;
        if(!usable || samples.Count<30) {Status="未保存 · 追踪或采样不足，请重试";return;}
        samples.Sort();
        if(samples[samples.Count-1]-samples[0]>.06f) {Status="未保存 · 高度变化过大，请站直保持不动";return;}
        EyeHeight=samples[samples.Count/2];PlayerPrefs.SetFloat(PreferenceKey,EyeHeight);PlayerPrefs.Save();Succeeded=true;
        Status="已保存站立眼高 "+Mathf.RoundToInt(EyeHeight*100)+" cm · 地面到头显";
    }
}
