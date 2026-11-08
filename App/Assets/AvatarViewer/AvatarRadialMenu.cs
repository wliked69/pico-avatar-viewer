using System.Collections.Generic;
using AvatarViewer;
using UnityEngine;
using UnityEngine.UI;
using ByteDance.PICO.XR;
using UnityEngine.XR;

// World-space radial UI. Hit testing uses a ray/plane and polar coordinates,
// so it does not depend on the headset exposing a 2D Android mirror surface.
public sealed class AvatarRadialMenu : MonoBehaviour
{
    public Canvas Canvas { get; private set; }
    public bool Visible => Canvas!=null && Canvas.gameObject.activeSelf;
    public int PageCount => data!=null?data.pages.Length:0;
    Camera eyes; Font font; AvatarMenuData data; int page;
    readonly Stack<int> history=new Stack<int>();
    readonly List<Button> sectors=new List<Button>();
    readonly List<Graphic> temporary=new List<Graphic>();
    readonly List<Sprite> sprites=new List<Sprite>();
    Button center; Text title,detail;
    AvatarMenuData.Control puppet;
    string pulse; float pulseUntil;
    string pageTitle="模型菜单";
    readonly Stack<string> titles=new Stack<string>();
    float puppetHover;
    RadialSectorGraphic progress;
    Text centerText;
    System.Func<bool> mirrorState;
    UnityEngine.Events.UnityAction toggleMirror,moveMirror,calibrate;
    System.Func<float> floorOffset;
    System.Action<float> setFloorOffset;
    AvatarEmbodiment handRig;
    ViewerWheelOptions options;int libraryPage;
    string helpModel,helpTracking,helpStatus;bool helpCurrent;
    bool HasHome => toggleMirror!=null;
    // 0/1: prefer left/right wrist; 2: follow the hand that summoned the wheel.
    public int DockMode { get; private set; }
    public bool AnchorTracked { get; private set; }
    const float DefaultHeight=.32f;
    float heightOffset=DefaultHeight,depthOffset=.10f;
    bool anchored;
    int summonHand;
    HandJointLocations cachedLeft,cachedRight;Transform cachedTracking;bool hasAnchorInput;
    public void SetDockMode(int mode) {
        DockMode=Mathf.Clamp(mode,0,2); anchored=false;
        PlayerPrefs.SetInt("WheelHandPreferenceRevision",1);
        PlayerPrefs.SetInt("WheelDockMode",DockMode); PlayerPrefs.Save();
        if(Visible && hasAnchorInput) SupplyAnchor(cachedLeft,cachedRight,cachedTracking,1);else PlaceInFront(); Rebuild();
    }
    void AdjustPosition(float height,float depth) {
        heightOffset=Mathf.Clamp(heightOffset+height,-.10f,.50f); depthOffset=Mathf.Clamp(depthOffset+depth,0,.50f);
        PlayerPrefs.SetFloat("WheelHeight",heightOffset); PlayerPrefs.SetFloat("WheelDepth",depthOffset); PlayerPrefs.Save();
        if(Visible && hasAnchorInput) SupplyAnchor(cachedLeft,cachedRight,cachedTracking,1); Rebuild();
    }
    void ResetPosition() {
        heightOffset=DefaultHeight; depthOffset=.10f; AdjustPosition(0,0);
    }
    void PlaceInFront() {
        if(Canvas==null || eyes==null) return;
        var q=Quaternion.Euler(0,eyes.transform.eulerAngles.y,0);
        Canvas.transform.localScale=Vector3.one*.00072f;
        Canvas.transform.SetPositionAndRotation(eyes.transform.position+q*new Vector3(0,heightOffset-.22f,1+depthOffset-.10f),q);
    }
    public void SupplyAnchor(HandJointLocations left,HandJointLocations right,Transform tracking,float delta) {
        cachedLeft=left;cachedRight=right;cachedTracking=tracking;hasAnchorInput=true;
        if(!Visible) { AnchorTracked=false; return; }
        int preferred=DockMode==2?summonHand:DockMode;
        bool Wrist(HandJointLocations value,out Vector3 point)=>AvatarEmbodiment.Joint(value,1,tracking,out point) && (value.jointLocations[1].locationStatus & HandLocationStatus.PositionTracked)!=0;
        bool valid=Wrist(preferred==0?left:right,out var wrist);
        if(!valid) valid=Wrist(preferred==0?right:left,out wrist);
        AnchorTracked=valid;
        if(!valid) {anchored=false;SetVisible(false);return;}
        var q=Quaternion.Euler(0,eyes.transform.eulerAngles.y,0);
        var target=wrist+Vector3.up*heightOffset+q*Vector3.forward*depthOffset;
        var toward=target-eyes.transform.position;
        var rotation=toward.sqrMagnitude>.0001f?Quaternion.LookRotation(toward,Vector3.up):q;
        float blend=anchored?1-Mathf.Exp(-Mathf.Max(0,delta)*22):1;
        Canvas.transform.SetPositionAndRotation(Vector3.Lerp(Canvas.transform.position,target,blend),Quaternion.Slerp(Canvas.transform.rotation,rotation,blend));
        anchored=true;
    }
    public void ConfigureViewer(System.Func<bool> state,UnityEngine.Events.UnityAction toggle,UnityEngine.Events.UnityAction move,UnityEngine.Events.UnityAction recalibrate) {
        mirrorState=state; toggleMirror=toggle; moveMirror=move; calibrate=recalibrate; history.Clear();titles.Clear();page=-1; pageTitle="快捷菜单"; Rebuild();
    }
    public void ConfigureOptions(ViewerWheelOptions value) {options=value;Rebuild();}
    public void Refresh() {Rebuild();}
    public void NotifyStatus(string value) {
        if(!Visible || (page!=-1 && page!=-8 && page!=-12)) return;
        value=value.Length>130?value.Substring(0,130)+"…":value;
        detail.text=(page==-1?(options?.ModelTitle?.Invoke()??"")+"\n":"")+value+(page==-1?"\n拇指与小指持续接触可召唤轮盘":"");
    }
    public void ShowHome(int hand=-1) {
        if(hand>=0 && hand<=1) summonHand=hand;
        if(puppet!=null) ResetPuppet();puppet=null;history.Clear();titles.Clear();page=-1;pageTitle="快捷菜单";SetVisible(true);
    }
    void Enter(int target,string label) {history.Push(page);titles.Push(pageTitle);page=target;pageTitle=label;Rebuild();}
    public void ConfigureFloor(System.Func<float> offset,System.Action<float> set) { floorOffset=offset; setFloorOffset=set; Rebuild(); }
    public void ConfigureHands(AvatarEmbodiment rig) { handRig=rig; Rebuild(); }
    public void Initialize(Camera view,Font uiFont) {
        if(Canvas!=null) {Canvas.gameObject.SetActive(false);Delete(Canvas.gameObject);Canvas=null;}
        sectors.Clear();temporary.Clear();
        foreach(var sprite in sprites) if(sprite!=null) Delete(sprite);sprites.Clear();
        eyes=view; font=uiFont;
        DockMode=Mathf.Clamp(PlayerPrefs.GetInt("WheelDockMode",2),0,2);
        if(PlayerPrefs.GetInt("WheelHandPreferenceRevision",0)<1) {
            DockMode=2;PlayerPrefs.SetInt("WheelDockMode",2);PlayerPrefs.SetInt("WheelHandPreferenceRevision",1);PlayerPrefs.Save();
        }
        heightOffset=Mathf.Clamp(PlayerPrefs.GetFloat("WheelHeight",DefaultHeight),-.10f,.50f); depthOffset=Mathf.Clamp(PlayerPrefs.GetFloat("WheelDepth",.10f),0,.50f);
        // Raise existing placements too, rather than only changing defaults.
        if(PlayerPrefs.GetInt("WheelPlacementRevision",0)<1) {
            if(PlayerPrefs.HasKey("WheelHeight")) heightOffset=Mathf.Clamp(heightOffset+.12f,-.10f,.50f);
            PlayerPrefs.SetFloat("WheelHeight",heightOffset);PlayerPrefs.SetInt("WheelPlacementRevision",1);PlayerPrefs.Save();
        }
        var go=new GameObject("VRC model radial menu",typeof(RectTransform),typeof(Canvas));
        // Native hierarchy ownership also works when edit-mode MonoBehaviour
        // destruction callbacks are not invoked during validation.
        go.transform.SetParent(transform,false);
        #if UNITY_EDITOR
        go.hideFlags=HideFlags.DontSave;
        #endif
        Canvas=go.GetComponent<Canvas>(); Canvas.renderMode=RenderMode.WorldSpace; Canvas.worldCamera=eyes;
        var rect=(RectTransform)go.transform; rect.sizeDelta=new Vector2(620,1020); rect.localScale=Vector3.one*.00125f;
        Ring("Outer rim",0,299,new Color(.46f,.49f,.52f,.95f));
        Ring("Wheel background",0,296,new Color(.12f,.13f,.15f,.97f));
        title=TextAt("模型菜单",new Vector2(0,330),new Vector2(620,65),27);
        detail=TextAt("看向扇区并捏合 / 按扳机选择",new Vector2(0,425),new Vector2(620,115),18);
        center=MakeButton("返回",new Vector2(0,0),88,0,360);
        centerText=center.GetComponentInChildren<Text>();
        center.onClick.AddListener(Back);
        go.SetActive(false);
    }
    public void Bind(GameObject avatar) {
        if(!string.IsNullOrEmpty(pulse)) data?.Set(pulse,0); pulse=null;
        data=avatar.GetComponent<AvatarMenuData>(); history.Clear(); titles.Clear(); pageTitle=HasHome?"快捷菜单":"模型菜单"; page=HasHome?-1:0; puppet=null;
        data?.InitializeParameters(); Rebuild();
    }
    public void Toggle() { SetVisible(!Visible); }
    public void SetVisible(bool value) {
        if(Canvas==null) return;
        Canvas.gameObject.SetActive(value);
        if(value) {
            anchored=false;if(hasAnchorInput)SupplyAnchor(cachedLeft,cachedRight,cachedTracking,1);else PlaceInFront();
            Rebuild();
        } else if(puppet!=null && puppet.type!="RadialPuppet") ResetPuppet();
    }
    void Update() {
        if(!string.IsNullOrEmpty(pulse) && Time.unscaledTime>=pulseUntil) { data?.Set(pulse,0); pulse=null; }
        if(Visible && page==-4 && handRig!=null) UpdateHandDetail();
        if(Visible && page==-5 && handRig?.Proportions!=null) UpdateBodyDetail();
        if(Visible && page==-9) UpdateHelp();
    }
    void UpdateHelp() {
        string model=options?.ModelTitle?.Invoke()??"",tracking=options?.Tracking?.Invoke()??"",status=options?.Status?.Invoke()??"";
        if(helpCurrent && model==helpModel && tracking==helpTracking && status==helpStatus)return;
        helpModel=model;helpTracking=tracking;helpStatus=status;helpCurrent=true;
        if(status.Length>120)status=status.Substring(0,120)+"…";detail.text=model+"\n"+tracking+"\n"+status;
    }
    void UpdateHandDetail() {
        var calibration=handRig.HandCalibration;
        detail.text=(handRig.AutomaticFit?.Busy==true?handRig.AutomaticFit.Status:calibration.Status)+"\n大小 "+Mathf.RoundToInt(calibration.Value.size*100)+"% · 掌宽 "+Mathf.RoundToInt(calibration.Value.width*100)+"%";
    }
    void UpdateBodyDetail() {
        var body=handRig.Proportions; var v=body.Value;
        var height=handRig.StandingHeight;
        title.text=body.Busy || height.Busy?"身体比例 · 正在校准":"身体比例";
        string status=height.Busy || height.HasAttempt || !height.Valid?height.Status:string.IsNullOrEmpty(handRig.AutomaticFit?.Status)?body.Status:handRig.AutomaticFit.Status;
        detail.text=status+"\n"+(height.Valid?"站立眼高 "+Mathf.RoundToInt(height.EyeHeight*100)+" cm · "+height.Stance:"先站直，完成「站立身高校准」")+"\n头部 "+Mathf.RoundToInt(v.head*100)+"% · 臂长 "+Mathf.RoundToInt(v.arms*100)+"% · 肩宽 "+Mathf.RoundToInt(v.shoulders*100)+"%";
        if(page==-5 && sectors.Count>0) sectors[0].GetComponentInChildren<Text>().text=handRig.AutomaticFit?.Busy==true?"取消自动校准":"自动匹配比例";
        if(page==-5 && sectors.Count>1)sectors[1].GetComponentInChildren<Text>().text=height.Busy?"取消身高校准":"站立身高校准";
    }
    void ChangeProportion(System.Action change) { handRig.StandingHeight.Cancel();handRig.StandingHeight.ClearStatus();handRig.AutomaticFit?.Cancel(); handRig.AutomaticFit?.ClearStatus(); change(); UpdateBodyDetail(); }
    void ToggleAutomaticFit() { if(handRig.AutomaticFit?.Busy==true) handRig.AutomaticFit.Cancel(); else handRig.BeginAutomaticCalibration(); UpdateBodyDetail(); }
    void Back() {
        if(puppet!=null) { ResetPuppet(); puppet=null; Rebuild(); }
        else if(history.Count>0) { page=history.Pop(); pageTitle=titles.Pop(); Rebuild(); }
        else SetVisible(false);
    }
    void ResetPuppet() {
        if(puppet==null) return;
        data?.Set(puppet.parameter,0);
        if(puppet.type!="RadialPuppet") foreach(var key in puppet.subParameters) data?.Set(key,0);
    }
    void Select(AvatarMenuData.Control control) {
        if(control.type=="SubMenu") {
            if(control.submenu<0 || control.submenu>=PageCount) return;
            history.Push(page); titles.Push(pageTitle); pageTitle=control.name; page=control.submenu; data.Set(control.parameter,control.value); Rebuild(); return;
        }
        if(control.type=="Toggle") {
            bool on=Mathf.Abs(data.Get(control.parameter)-control.value)<.001f;
            data.Set(control.parameter,on?0:control.value); data.RecordControl(control.name,control.parameter,on?0:control.value); Rebuild(); return;
        }
        if(control.type=="Button") { data.Set(control.parameter,control.value); data.RecordControl(control.name,control.parameter,control.value); pulse=control.parameter; pulseUntil=Time.unscaledTime+.25f; return; }
        if(control.type=="RadialPuppet" || control.type=="TwoAxisPuppet" || control.type=="FourAxisPuppet") {
            puppet=control; data.Set(control.parameter,control.value); Rebuild();
        }
    }
    void SetPuppet(int index) {
        if(puppet==null) return;
        var keys=puppet.subParameters;
        float a=index*Mathf.PI/4;
        if(puppet.type=="RadialPuppet" && keys.Length>0) data.Set(keys[0],index/7f);
        if(puppet.type=="TwoAxisPuppet" && keys.Length>=2) { data.Set(keys[0],Mathf.Sin(a)); data.Set(keys[1],Mathf.Cos(a)); }
        if(puppet.type=="FourAxisPuppet" && keys.Length>=4) {
            data.Set(keys[0],Mathf.Max(0,Mathf.Cos(a))); data.Set(keys[1],Mathf.Max(0,Mathf.Sin(a)));
            data.Set(keys[2],Mathf.Max(0,-Mathf.Cos(a))); data.Set(keys[3],Mathf.Max(0,-Mathf.Sin(a)));
        }
        Rebuild();
    }
    void Rebuild() {
        if(Canvas==null) return;
        foreach(var b in sectors) Delete(b.gameObject); sectors.Clear();
        foreach(var g in temporary) if(g!=null) Delete(g.gameObject); temporary.Clear();
        foreach(var sprite in sprites) Delete(sprite); sprites.Clear();
        centerText.text=puppet!=null || history.Count>0?"↶\n返回":"×\n关闭";
        progress=null;
        helpCurrent=false;
        if(HasHome && page==-12) {
            var files=options?.Models?.Invoke()??new ViewerWheelOptions.ModelFile[0];int pages=Mathf.Max(1,(files.Length+5)/6);libraryPage=Mathf.Clamp(libraryPage,0,pages-1);
            title.text="模型库 · "+(libraryPage+1)+" / "+pages;detail.text="共 "+files.Length+" 个文件 · 选择后加载\n文件管理与电脑备份可使用 Unity 插件";
            if(files.Length==0) {MakeSector("导入模型包",null,0,2,()=>options?.Import?.Invoke(),options?.Import!=null,false);MakeSector("重新扫描",null,1,2,()=>{options?.Scan?.Invoke();Rebuild();},options?.Scan!=null,false);return;}
            int start=libraryPage*6,count=Mathf.Min(6,files.Length-start);
            for(int i=0;i<count;i++) {var f=files[start+i];string label=f.label.Length>24?f.label.Substring(0,14)+"…"+f.label.Substring(f.label.Length-8):f.label;MakeSector(label,null,i,count+2,()=>options?.LoadModel?.Invoke(f.path),options?.LoadModel!=null,false);}
            MakeSector("上一页",null,count,count+2,()=>{libraryPage--;Rebuild();},libraryPage>0,false);
            MakeSector("下一页",null,count+1,count+2,()=>{libraryPage++;Rebuild();},libraryPage+1<pages,false);return;
        }
        if(HasHome && page==-10) {
            title.text="退出应用？";detail.text="中心返回不会退出 · 校准与设置已保存在本地";
            MakeSector("确认退出",null,0,2,()=>options?.Quit?.Invoke(),options?.Quit!=null,false);
            MakeSector("继续使用",null,1,2,Back,true,false);return;
        }
        if(HasHome && page==-9) {
            title.text="状态与帮助";UpdateHelp();
            MakeSector("重新扫描",null,0,1,()=>{options?.Scan?.Invoke();Rebuild();},options?.Scan!=null,false);return;
        }
        if(HasHome && page==-8) {
            title.text="应用设置";detail.text="导入和扫描本地模型 · 退出需要二次确认";
            MakeSector("导入模型包",null,0,4,()=>options?.Import?.Invoke(),options?.Import!=null,false);
            MakeSector("重新扫描",null,1,4,()=>{options?.Scan?.Invoke();Rebuild();},options?.Scan!=null,false);
            MakeSector("状态与帮助",null,2,4,()=>Enter(-9,"状态与帮助"),true,false,"SubMenu");
            MakeSector("退出应用",null,3,4,()=>Enter(-10,"退出应用"),options?.Quit!=null,false,"SubMenu");return;
        }
        if(HasHome && page==-7) {
            title.text="角色设置";detail.text="附身与追踪 · 个人校准及物品摆动";
            bool IsOn(System.Func<bool> value)=>value?.Invoke()==true;
            MakeSector("附身："+(IsOn(options?.Embodied)?"开":"关"),null,0,7,()=>{options?.ToggleEmbodied?.Invoke();Rebuild();},options?.ToggleEmbodied!=null,IsOn(options?.Embodied),"Toggle");
            MakeSector("动骨："+(IsOn(options?.Bones)?"开":"关"),null,1,7,()=>{options?.ToggleBones?.Invoke();Rebuild();},options?.ToggleBones!=null,IsOn(options?.Bones),"Toggle");
            MakeSector("眼动面捕："+(IsOn(options?.Expressions)?"开":"关"),null,2,7,()=>{options?.ToggleExpressions?.Invoke();Rebuild();},options?.ToggleExpressions!=null,IsOn(options?.Expressions),"Toggle");
            MakeSector("手部校准",null,3,7,()=>Enter(-4,"手部校准"),handRig!=null,false,"SubMenu");
            MakeSector("身体比例",null,4,7,()=>Enter(-5,"身体比例"),handRig?.Proportions!=null,false,"SubMenu");
            MakeSector("物品摆动",null,5,7,()=>Enter(-6,"物品摆动"),handRig?.Proportions!=null,false,"SubMenu");
            MakeSector("地面高度",null,6,7,()=>Enter(-3,"地面高度"),floorOffset!=null,false,"SubMenu");return;
        }
        if(HasHome && page==-6 && handRig?.Proportions!=null) {
            title.text="物品摆动"; detail.text="物品及挂饰的动骨 · 当前幅度 "+Mathf.RoundToInt(handRig.Proportions.Value.propSway*100)+"%\n静态道具的位置不受此项影响";
            MakeSector("减小 10%",null,0,4,()=>{handRig.Proportions.AdjustSway(-.1f); Rebuild();},true,false);
            MakeSector("增加 10%",null,1,4,()=>{handRig.Proportions.AdjustSway(.1f); Rebuild();},true,false);
            MakeSector("轻微摆动",null,2,4,()=>{handRig.Proportions.AdjustSway(.35f-handRig.Proportions.Value.propSway); Rebuild();},true,false);
            MakeSector("原始幅度",null,3,4,()=>{handRig.Proportions.AdjustSway(1-handRig.Proportions.Value.propSway); Rebuild();},true,false);
            return;
        }
        if(HasHome && page==-5 && handRig?.Proportions!=null) {
            title.text="身体比例"; UpdateBodyDetail();
            MakeSector("自动匹配比例",null,0,10,ToggleAutomaticFit,true,false);
            MakeSector("站立身高校准",null,1,10,()=>{handRig.BeginStandingHeightCalibration();UpdateBodyDetail();},true,false);
            MakeSector("校准臂长",null,2,10,()=>{handRig.BeginArmCalibration(); UpdateBodyDetail();},true,false);
            MakeSector("头部缩小 5%",null,3,10,()=>ChangeProportion(()=>handRig.Proportions.Adjust(-.05f,0,0)),true,false);
            MakeSector("头部放大 5%",null,4,10,()=>ChangeProportion(()=>handRig.Proportions.Adjust(.05f,0,0)),true,false);
            MakeSector("手臂缩短 5%",null,5,10,()=>ChangeProportion(()=>handRig.Proportions.Adjust(0,-.05f,0)),true,false);
            MakeSector("手臂加长 5%",null,6,10,()=>ChangeProportion(()=>handRig.Proportions.Adjust(0,.05f,0)),true,false);
            MakeSector("肩宽减小 5%",null,7,10,()=>ChangeProportion(()=>handRig.Proportions.Adjust(0,0,-.05f)),true,false);
            MakeSector("肩宽增加 5%",null,8,10,()=>ChangeProportion(()=>handRig.Proportions.Adjust(0,0,.05f)),true,false);
            MakeSector("恢复原始比例",null,9,10,()=>ChangeProportion(()=>{handRig.Proportions.Reset();handRig.Recalibrate();}),true,false);
            return;
        }
        if(HasHome && page==-4 && handRig!=null) {
            title.text="手部校准"; UpdateHandDetail();
            MakeSector("开始校准",null,0,6,()=>{handRig.BeginHandCalibration(); UpdateHandDetail();},true,false);
            MakeSector("整体放大 5%",null,1,6,()=>{ChangeProportion(()=>handRig.HandCalibration.Adjust(.05f,0)); UpdateHandDetail();},true,false);
            MakeSector("整体缩小 5%",null,2,6,()=>{ChangeProportion(()=>handRig.HandCalibration.Adjust(-.05f,0)); UpdateHandDetail();},true,false);
            MakeSector("掌宽增加 5%",null,3,6,()=>{ChangeProportion(()=>handRig.HandCalibration.Adjust(0,.05f)); UpdateHandDetail();},true,false);
            MakeSector("掌宽减少 5%",null,4,6,()=>{ChangeProportion(()=>handRig.HandCalibration.Adjust(0,-.05f)); UpdateHandDetail();},true,false);
            MakeSector("重置手部",null,5,6,()=>{ChangeProportion(handRig.HandCalibration.Reset); UpdateHandDetail();},true,false);
            return;
        }
        if(HasHome && page==-3 && floorOffset!=null) {
            title.text="地面高度"; detail.text="当前偏移 "+Mathf.RoundToInt(floorOffset()*100)+" cm · 站直后调整";
            MakeSector("降低 5cm",null,0,4,()=>{setFloorOffset(floorOffset()-.05f); Rebuild();},true,false);
            MakeSector("升高 5cm",null,1,4,()=>{setFloorOffset(floorOffset()+.05f); Rebuild();},true,false);
            MakeSector("原始高度",null,2,4,()=>{setFloorOffset(0); Rebuild();},true,false);
            MakeSector("降低至 -15cm",null,3,4,()=>{setFloorOffset(-.15f); Rebuild();},true,false);
            return;
        }
        if(HasHome && page==-2) {
            title.text="菜单位置"; detail.text="跟随有效手腕 · 双手未追踪时自动关闭";
            MakeSector("吸附左手",null,0,8,()=>SetDockMode(0),true,DockMode==0,"Toggle");
            MakeSector("吸附右手",null,1,8,()=>SetDockMode(1),true,DockMode==1,"Toggle");
            MakeSector("随召唤手",null,2,8,()=>SetDockMode(2),true,DockMode==2,"Toggle");
            MakeSector("上移 3cm",null,3,8,()=>AdjustPosition(.03f,0),true,false);
            MakeSector("下移 3cm",null,4,8,()=>AdjustPosition(-.03f,0),true,false);
            MakeSector("远离 3cm",null,5,8,()=>AdjustPosition(0,.03f),true,false);
            MakeSector("靠近 3cm",null,6,8,()=>AdjustPosition(0,-.03f),true,false);
            MakeSector("重置偏移",null,7,8,ResetPosition,true,false);
            return;
        }
        if(HasHome && page==-1) {
            title.text="快捷菜单"; detail.text=(options?.ModelTitle?.Invoke()??"模型换装 / 道具在「模型菜单」中")+"\n拇指与小指持续接触可召唤 · 中心关闭";
            int count=8;
            MakeSector("模型菜单",null,0,count,()=>{history.Push(-1); titles.Push("快捷菜单"); page=0; pageTitle="模型菜单"; Rebuild();},PageCount>0,false,"SubMenu");
            MakeSector("镜子："+(mirrorState()?"开":"关"),null,1,count,()=>{toggleMirror(); Rebuild();},true,mirrorState(),"Toggle");
            MakeSector("镜子移到面前",null,2,count,()=>moveMirror(),true,false);
            MakeSector("重新校准",null,3,count,()=>calibrate(),true,false);
            MakeSector("菜单位置",null,4,count,()=>Enter(-2,"菜单位置"),true,false,"SubMenu");
            MakeSector("模型库",null,5,count,()=>{libraryPage=0;Enter(-12,"模型库");},options?.Models!=null,false,"SubMenu");
            MakeSector("角色设置",null,6,count,()=>Enter(-7,"角色设置"),true,false,"SubMenu");
            MakeSector("应用设置",null,7,count,()=>Enter(-8,"应用设置"),true,false,"SubMenu");
            return;
        }
        if(data==null || PageCount==0) { title.text="模型没有导入轮盘菜单"; detail.text="请用新版 Unity 上传插件重新转换模型"; return; }
        title.text=puppet!=null?puppet.name:pageTitle;
        detail.text="看向扇区并捏合 / 按扳机 · 中心返回";
        if(puppet!=null) {
            if(puppet.type=="RadialPuppet") {
                var slider=MakeButton("环形调节",Vector2.zero,285,0,360,95);
                slider.GetComponentInChildren<Text>().text=""; slider.onClick.AddListener(ApplyRadialValue); sectors.Add(slider);
                progress=Ring("Current value",267,281,new Color(.05f,.6f,.93f,1)); temporary.Add(progress);
                UpdateRadialValue(); detail.text="看向圆环，捏住并转头调节 · 中心返回"; return;
            }
            for(int i=0;i<8;i++) {
                int index=i;
                string label=puppet.type=="RadialPuppet"?Mathf.RoundToInt(i/7f*100)+"%":new[]{"上","右上","右","右下","下","左下","左","左上"}[i];
                MakeSector(label,null,i,8,()=>SetPuppet(index),true,false);
            }
            return;
        }
        var controls=data.pages[page].controls;
        for(int i=0;i<controls.Length;i++) {
            var c=controls[i];
            bool supported=c.type=="SubMenu" || c.type=="Toggle" || c.type=="Button" || c.type.EndsWith("Puppet");
            bool active=c.type=="Toggle" && Mathf.Abs(data.Get(c.parameter)-c.value)<.001f;
            bool valid=supported && (c.type=="SubMenu" || data.Has(c.parameter) || c.subParameters.Length>0);
            MakeSector(c.name,c.icon,i,controls.Length,()=>Select(c),valid,active,c.type);
        }
        if(data.effects==null) detail.text=data.compatibility;
    }
    void MakeSector(string label,Texture2D icon,int index,int count,UnityEngine.Events.UnityAction action,bool valid,bool active,string type="Button") {
        float width=360f/Mathf.Max(1,count),angle=index*width;
        var button=MakeButton(label,Vector2.zero,285,angle-width*.5f+.8f,width-1.6f,95);
        button.onClick.AddListener(action); button.interactable=valid;
        button.GetComponent<RadialSectorGraphic>().color=active?new Color(.04f,.44f,.67f,.98f):new Color(.22f,.24f,.27f,.98f);
        var text=button.GetComponentInChildren<Text>();
        float radians=angle*Mathf.Deg2Rad; var point=new Vector2(Mathf.Sin(radians),Mathf.Cos(radians))*190;
        ((RectTransform)text.transform).anchoredPosition=point+new Vector2(0,-32);
        text.fontSize=19; ((RectTransform)text.transform).sizeDelta=new Vector2(145,70);
        if(icon!=null) {
            var go=new GameObject("Menu icon",typeof(RectTransform),typeof(Image)); go.transform.SetParent(button.transform,false);
            var r=(RectTransform)go.transform; r.sizeDelta=new Vector2(56,56); r.anchoredPosition=point+new Vector2(0,22);
            var image=go.GetComponent<Image>(); var sprite=Sprite.Create(icon,new Rect(0,0,icon.width,icon.height),new Vector2(.5f,.5f)); sprites.Add(sprite); image.sprite=sprite; image.preserveAspect=true;
        } else {
            var go=new GameObject("Menu symbol",typeof(RectTransform),typeof(RadialMenuSymbol)); go.transform.SetParent(button.transform,false);
            var r=(RectTransform)go.transform; r.sizeDelta=new Vector2(56,56); r.anchoredPosition=point+new Vector2(0,24);
            var symbol=go.GetComponent<RadialMenuSymbol>(); symbol.kind=type; symbol.selected=active; symbol.color=Color.white; symbol.raycastTarget=false;
        }
        sectors.Add(button);
    }
    Button MakeButton(string label,Vector2 position,float outer,float start,float sweep,float inner=0) {
        var go=new GameObject(label,typeof(RectTransform),typeof(RadialSectorGraphic),typeof(Button)); go.transform.SetParent(Canvas.transform,false);
        var rect=(RectTransform)go.transform; rect.sizeDelta=Vector2.one*outer*2; rect.anchoredPosition=position;
        var graphic=go.GetComponent<RadialSectorGraphic>(); graphic.outer=outer; graphic.inner=inner; graphic.start=start; graphic.sweep=sweep; graphic.color=new Color(.2f,.22f,.25f,.98f);
        var button=go.GetComponent<Button>(); button.targetGraphic=graphic;
        var colors=button.colors; colors.highlightedColor=new Color(.18f,2.5f,3.8f,1); colors.pressedColor=new Color(.1f,1.7f,3.1f,1); colors.colorMultiplier=1; colors.fadeDuration=.06f; button.colors=colors;
        var text=TextAt(label,Vector2.zero,new Vector2(145,65),22,go.transform); return button;
    }
    Text TextAt(string label,Vector2 point,Vector2 size,int fontSize,Transform parent=null) {
        var go=new GameObject(label,typeof(RectTransform),typeof(Text)); go.transform.SetParent(parent!=null?parent:Canvas.transform,false);
        var rect=(RectTransform)go.transform; rect.anchoredPosition=point; rect.sizeDelta=size;
        var t=go.GetComponent<Text>(); t.font=font; t.fontSize=fontSize; t.text=label; t.color=Color.white; t.alignment=TextAnchor.MiddleCenter; t.raycastTarget=false; return t;
    }
    public Button HitTest(Ray ray,out Vector3 point) {
        point=ray.origin+ray.direction;
        if(!Visible) return null;
        var plane=new Plane(Canvas.transform.forward,Canvas.transform.position);
        if(!plane.Raycast(ray,out float distance) || distance<=0 || distance>5) return null;
        point=ray.GetPoint(distance); var p=Canvas.transform.InverseTransformPoint(point);
        if(p.magnitude<88) return center;
        if(p.magnitude<95 || p.magnitude>285 || sectors.Count==0) return null;
        float angle=Mathf.Repeat(Mathf.Atan2(p.x,p.y)*Mathf.Rad2Deg,360);
        if(puppet!=null && puppet.type=="RadialPuppet") { puppetHover=angle/360; return sectors[0]; }
        int i=Mathf.FloorToInt(Mathf.Repeat(angle+180f/sectors.Count,360)/(360f/sectors.Count));
        return sectors[i].interactable?sectors[i]:null;
    }
    RadialSectorGraphic Ring(string name,float inner,float outer,Color color) {
        var go=new GameObject(name,typeof(RectTransform),typeof(RadialSectorGraphic)); go.transform.SetParent(Canvas.transform,false);
        ((RectTransform)go.transform).sizeDelta=Vector2.one*outer*2;
        var graphic=go.GetComponent<RadialSectorGraphic>(); graphic.inner=inner; graphic.outer=outer; graphic.sweep=360; graphic.color=color; graphic.raycastTarget=false; return graphic;
    }
    void ApplyRadialValue() { if(puppet!=null && puppet.subParameters.Length>0) { data.Set(puppet.subParameters[0],puppetHover); UpdateRadialValue(); } }
    void UpdateRadialValue() {
        float value=puppet.subParameters.Length>0?Mathf.Clamp01(data.Get(puppet.subParameters[0])):0;
        centerText.text=Mathf.RoundToInt(value*100)+"%\n↶ 返回";
        if(progress!=null) { progress.sweep=value*360; progress.SetVerticesDirty(); }
    }
    public void ProcessHeld(Ray ray,bool held) {
        if(!held || puppet==null || puppet.type!="RadialPuppet") return;
        var target=HitTest(ray,out _); if(target!=null && target!=center) ApplyRadialValue();
    }
    static void Delete(Object target) { if(Application.isPlaying) Destroy(target); else DestroyImmediate(target); }
    void OnDestroy() { if(Canvas!=null) Delete(Canvas.gameObject); foreach(var s in sprites) if(s!=null) Delete(s); }
}



