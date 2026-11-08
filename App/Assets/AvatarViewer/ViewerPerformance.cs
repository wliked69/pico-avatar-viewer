using System;
using System.IO;
using AvatarViewer;
using UnityEngine;

// Small rolling on-device measurements; excludes headset suspension and loading pauses.
[DefaultExecutionOrder(200)]
public sealed class ViewerPerformance : MonoBehaviour
{
    AvatarEmbodiment rig; AvatarMirror mirror;
    readonly float[] samples=new float[900]; readonly float[] sorted=new float[900];
    readonly FrameTiming[] timings=new FrameTiming[1];
    int count; float duration,nextReport; double springs,pose,reflection;
    public void Initialize(AvatarEmbodiment embodiment,AvatarMirror avatarMirror) { rig=embodiment; mirror=avatarMirror; nextReport=Time.unscaledTime+10; }
    [Serializable] sealed class Report {
        public string utc; public int frames; public bool focused,embodied,mirror;
        public float fps,medianFrameMs,p95FrameMs,maxFrameMs,springMs,poseMs,mirrorCopyMs;
        public double cpuFrameMs,gpuFrameMs;
    }
    void LateUpdate() {
        if(rig==null || !rig.Calibrated || !Application.isFocused || Time.unscaledDeltaTime>5) { ResetWindow(); return; }
        FrameTimingManager.CaptureFrameTimings();
        if(count<samples.Length) samples[count++]=Time.unscaledDeltaTime*1000;
        duration+=Time.unscaledDeltaTime; springs+=SpringChain.LastFrameMilliseconds; pose+=rig.PoseMilliseconds; reflection+=mirror.CopyMilliseconds;
        if(Time.unscaledTime<nextReport) return;
        Array.Copy(samples,sorted,count); Array.Sort(sorted,0,count);
        var report=new Report { utc=DateTime.UtcNow.ToString("O"),frames=count,focused=true,embodied=rig.Active,mirror=mirror.Visible,fps=count/Mathf.Max(duration,.001f),medianFrameMs=count>0?sorted[count/2]:0,p95FrameMs=count>0?sorted[Mathf.Min(count-1,(int)(count*.95f))]:0,maxFrameMs=count>0?sorted[count-1]:0,springMs=(float)(springs/Math.Max(count,1)),poseMs=(float)(pose/Math.Max(count,1)),mirrorCopyMs=(float)(reflection/Math.Max(count,1)) };
        if(FrameTimingManager.GetLatestTimings(1,timings)>0) {report.cpuFrameMs=timings[0].cpuFrameTime; report.gpuFrameMs=timings[0].gpuFrameTime;}
        File.WriteAllText(Path.Combine(Application.persistentDataPath,"performance-status.json"),JsonUtility.ToJson(report,true));
        Debug.Log("AVATAR_PERFORMANCE "+JsonUtility.ToJson(report)); ResetWindow();
    }
    void ResetWindow() { count=0; duration=0; springs=pose=reflection=0; nextReport=Time.unscaledTime+5; }
    void OnApplicationPause(bool paused) { ResetWindow(); }
}
