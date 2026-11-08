using UnityEngine;

// Joint calibration uses measured hands and reach; hand size alone cannot identify
// a person's head size, shoulder width, or height. No anthropometric guesses.
public sealed class AutomaticProportionCalibration
{
    readonly HandSizeCalibration hands;
    readonly BodyProportions body;
    HandSizeCalibration.Settings originalHands;
    BodyProportions.Settings originalBody;
    public bool Busy { get; private set; }
    public bool Succeeded { get; private set; }
    public string Status { get; private set; }="";
    public event System.Action<bool> Finished;
    public System.Func<bool> PrepareWholeBody;
    public AutomaticProportionCalibration(HandSizeCalibration hands,BodyProportions body) { this.hands=hands; this.body=body; }
    public bool Begin(float now) {
        if(Busy) return false;
        if(hands.Busy || body.Busy) { Reject("请等待当前校准结束后重试"); return false; }
        originalHands=JsonUtility.FromJson<HandSizeCalibration.Settings>(JsonUtility.ToJson(hands.Value));
        originalBody=JsonUtility.FromJson<BodyProportions.Settings>(JsonUtility.ToJson(body.Value));
        Busy=true; Succeeded=false;
        hands.Begin(now,false); body.Begin(now,false);
        Status="站直 · 张开双手，双臂向正前方伸直\n与肩同高，保持约 7 秒";
        return true;
    }
    public void Tick() {
        if(!Busy) return;
        if(hands.Busy || body.Busy) { Status=(body.Busy?body.Status:hands.Status)+"\n手掌保持张开"; return; }
        if(!hands.LeftSucceeded || !hands.RightSucceeded || !body.CalibrationSucceeded) {
            string reason=!hands.LeftSucceeded || !hands.RightSucceeded?"需要同时追踪到两只张开的手":body.Status;
            Cancel("未保存，已保留原设置\n"+reason); return;
        }
        if(PrepareWholeBody!=null && !PrepareWholeBody()) {Cancel("未保存 · 整体与臂长未能同时匹配\n保持站直、双臂前伸后重试");return;}
        hands.Value.size=hands.Value.width=1;
        hands.Store(); body.Store(); PlayerPrefs.Save();
        Busy=false; Succeeded=true; originalHands=null; originalBody=null;
        Status="已保存 · 身高、双手和臂长已校准\n头部与肩宽可继续微调";
        Finished?.Invoke(true);
    }
    public void Cancel(string message="已取消 · 原设置保持不变") {
        if(!Busy) return;
        hands.Restore(originalHands); body.Restore(originalBody);
        originalHands=null; originalBody=null; Busy=false; Succeeded=false; Status=message;
        Finished?.Invoke(false);
    }
    public void Reject(string message) { if(Busy) Cancel(message); else {Succeeded=false;Status=message;} }
    public void ClearStatus() { if(!Busy) Status=""; }
}
