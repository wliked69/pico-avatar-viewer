using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug=UnityEngine.Debug;

public sealed class PicoAvatarUploader : EditorWindow
{
    const string Package="com.psp.vrcavatarviewer";
    const string DeviceFolder="/sdcard/Android/data/"+Package+"/files";
    const string Plugin="Assets/AvatarViewerExport";
    [SerializeField] GameObject avatar;
    [SerializeField] string adbPath,device="",message="选择场景中的 VRC 模型，然后检查 USB 设备。";
    [SerializeField] string lastBundle="";
    [SerializeField] string[] devices=new string[0];
    bool busy;
    bool reloadLocked;
    bool batchBuildOnly;
    CancellationTokenSource cancel;
    IDisposable operation;
    string progressStage="",publishedFile="";
    long uploadBytes,uploadTotal;
    bool progressDeterminate,filePublished;
    double phaseStarted;
    Vector2 scroll;

    [MenuItem("PICO Avatar/USB 模型上传")]
    public static void Open() { GetWindow<PicoAvatarUploader>("PICO 模型上传").minSize=new Vector2(520,440); }
    void OnEnable() {
        adbPath=PicoAvatarUsbConnection.Resolve(EditorPrefs.GetString("PicoAvatarUploader.adb",""))??"";
        if(Selection.activeGameObject!=null) avatar=FindAvatarRoot(Selection.activeGameObject);
        PicoAvatarUsbConnection.Watch(OnUsbChanged);
    }
    void OnDisable() {PicoAvatarUsbConnection.Unwatch(OnUsbChanged);}
    void OnInspectorUpdate() {if(busy)Repaint();}
    void OnUsbChanged(PicoAvatarUsbConnection.Snapshot snapshot) {
        if(busy || this==null)return;
        string previous=device;adbPath=snapshot.adbPath;devices=snapshot.devices;device=PicoAvatarUsbConnection.SelectDevice(device,devices);
        if(devices.Length==0 || previous!=device || message.StartsWith("选择场景") || message.StartsWith("等待") || message.StartsWith("USB 检测"))message=snapshot.message;
        Repaint();
    }
    void OnGUI() {
        EditorGUILayout.Space(); EditorGUILayout.LabelField("上传 VRC 模型到 PICO",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Unity 2022.3 + Android Build Support。将模型放入场景并设置好装扮；转换保留网格、基础材质、骨骼、表情和近似动骨。",MessageType.Info);
        if(GUILayout.Button("管理头显中的模型文件…")) PicoAvatarModelManager.Open(adbPath,device);
        using(new EditorGUI.DisabledScope(busy || PicoAvatarModelLibrary.OperationInProgress)) {
            avatar=(GameObject)EditorGUILayout.ObjectField("场景模型",avatar,typeof(GameObject),true);
            if(GUILayout.Button("使用当前选中的模型")) avatar=Selection.activeGameObject!=null?FindAvatarRoot(Selection.activeGameObject):null;
            EditorGUILayout.BeginHorizontal(); adbPath=EditorGUILayout.TextField("ADB 路径",adbPath);
            if(GUILayout.Button("选择…",GUILayout.Width(65))) { string path=EditorUtility.OpenFilePanel("选择 Android SDK 的 adb","","exe"); if(!string.IsNullOrEmpty(path)) adbPath=path; }
            EditorGUILayout.EndHorizontal();
            if(GUILayout.Button("检查 USB 连接")) RefreshDevices();
            if(devices.Length>0) {
                var choices=new[]{"请选择目标头显"}.Concat(devices).ToArray();int index=Array.IndexOf(devices,device)+1;
                index=EditorGUILayout.Popup("目标头显",index,choices);device=index>0?devices[index-1]:"";
            }
            else EditorGUILayout.LabelField("目标头显","尚未检测到已授权的 USB 设备");
            EditorGUILayout.Space();
            if(GUILayout.Button("转换并上传到头显",GUILayout.Height(42))) BuildAndUpload();
            if(!string.IsNullOrEmpty(lastBundle) && GUILayout.Button("重新上传上次的模型包")) UploadLast();
        }
        if(!string.IsNullOrEmpty(progressStage)) {
            string label=progressStage;
            if(progressDeterminate && uploadTotal>0)label+=" · "+(100.0*uploadBytes/uploadTotal).ToString("F0")+"% · "+EditorUtility.FormatBytes(uploadBytes)+" / "+EditorUtility.FormatBytes(uploadTotal);
            else if(busy)label+=" · 已用 "+Math.Max(0,(int)(EditorApplication.timeSinceStartup-phaseStarted))+" 秒";
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false,25),progressDeterminate && uploadTotal>0?(float)uploadBytes/uploadTotal:0,label);
        }
        if(busy && GUILayout.Button("取消当前操作")) cancel?.Cancel();
        EditorGUILayout.Space(); scroll=EditorGUILayout.BeginScrollView(scroll); EditorGUILayout.HelpBox(message,MessageType.None); EditorGUILayout.EndScrollView();
        EditorGUILayout.LabelField("模型仅通过 USB 传到选中的头显，无云端上传。",EditorStyles.miniLabel);
    }
    public static GameObject FindAvatarRoot(GameObject selected) {
        foreach(var t in selected.GetComponentsInParent<Transform>(true))
            if(t.GetComponents<Component>().Any(c=>c!=null && c.GetType().Name=="VRCAvatarDescriptor")) return t.gameObject;
        return selected;
    }
    public static string FindAdb()=>PicoAvatarUsbConnection.Resolve(EditorPrefs.GetString("PicoAvatarUploader.adb",""))??"";
    async void RefreshDevices() {
        if(busy || PicoAvatarModelLibrary.OperationInProgress) return; Begin();
        try { await Detect(cancel.Token); }
        catch(Exception error) { Fail(error); } finally { End(); }
    }
    async Task Detect(CancellationToken token) {
        var snapshot=await PicoAvatarUsbConnection.Probe(adbPath,token);adbPath=snapshot.adbPath;devices=snapshot.devices;
        device=PicoAvatarUsbConnection.SelectDevice(device,devices);
        if(devices.Length==0)throw new Exception(snapshot.message);
        SetMessage(snapshot.message);
    }
    public static string[] ParseUsbDevices(string output)=>PicoAvatarUsbConnection.Parse(output,null).devices;
    async void BuildAndUpload() {
        if(busy) return; Begin();
        try {
            if(avatar==null || !avatar.scene.IsValid() || EditorUtility.IsPersistent(avatar)) throw new Exception("请先把模型放入场景，再选择模型根对象。");
            if(!Application.unityVersion.StartsWith("2022.3.")) throw new Exception("此版本查看器使用 Unity 2022.3 模型包。请在 Unity 2022.3 的 VRC 工程里使用本插件。");
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android,BuildTarget.Android)) throw new Exception("当前 Unity 缺少 Android Build Support，请为此编辑器安装该模块。");
            await Detect(cancel.Token);
            if(devices.Length>1 && !devices.Contains(device)) throw new Exception("检测到多台头显，请先选择目标设备。");
            string job=Guid.NewGuid().ToString("N");
            string root=Path.GetFullPath("Library/PicoAvatarUpload/"+job),builder=Path.Combine(root,"Builder"),output=Path.Combine(root,"Output");
            Directory.CreateDirectory(Path.Combine(builder,"Assets/AvatarViewerExport/Runtime")); Directory.CreateDirectory(Path.Combine(builder,"Assets/Editor")); Directory.CreateDirectory(Path.Combine(builder,"ProjectSettings")); Directory.CreateDirectory(Path.Combine(builder,"Packages"));
            File.Copy("ProjectSettings/ProjectVersion.txt",Path.Combine(builder,"ProjectSettings/ProjectVersion.txt"));
            File.WriteAllText(Path.Combine(builder,"Packages/manifest.json"),"{\"dependencies\":{\"com.unity.modules.assetbundle\":\"1.0.0\",\"com.unity.modules.animation\":\"1.0.0\",\"com.unity.modules.physics\":\"1.0.0\",\"com.unity.modules.jsonserialize\":\"1.0.0\"}}");
            foreach(var file in Directory.GetFiles(Plugin+"/Runtime")) {
                string extension=Path.GetExtension(file);
                if(extension==".cs" || extension==".shader" || extension==".asmdef" || extension==".meta") File.Copy(file,Path.Combine(builder,"Assets/AvatarViewerExport/Runtime",Path.GetFileName(file)),true);
            }
            File.Copy(Plugin+"/Editor/PicoAvatarBundleBuilder.txt",Path.Combine(builder,"Assets/Editor/PicoAvatarBundleBuilder.cs"));
            SetMessage("正在转换模型与装扮…");
            Stage("转换模型");
            EditorUtility.DisplayProgressBar("PICO 模型上传","正在转换模型…",.15f);
            AvatarExporter.Export(avatar,avatar.name,builder,output);
            EditorUtility.ClearProgressBar(); cancel.Token.ThrowIfCancellationRequested();
            SetMessage("转换完成，正在独立工程构建 Android 模型包。首次构建可能需要几分钟…\n日志："+Path.Combine(root,"bundle-build.log"));
            Stage("构建 Android 模型包（尚未开始 USB 传输）");
            await Run(EditorApplication.applicationPath,new[]{"-batchmode","-nographics","-quit","-projectPath",builder,"-buildTarget","Android","-executeMethod","PicoAvatarBundleBuilder.Build","-avatarOutput",output,"-logFile",Path.Combine(root,"bundle-build.log")},cancel.Token);
            lastBundle=Path.Combine(output,"uploaded.avatarbundle");
            if(!File.Exists(lastBundle)) throw new Exception("Unity 未生成模型包，请查看 bundle-build.log。");
            if(batchBuildOnly) {Stage("模型包已构建，尚未上传");SetMessage("构建成功："+lastBundle); return; }
            await Upload(lastBundle,device,cancel.Token);
        } catch(Exception error) { Fail(error); } finally { End(); }
    }
    async void UploadLast() {
        Begin(); try { await Detect(cancel.Token); await Upload(lastBundle,device,cancel.Token); } catch(Exception error) { Fail(error); } finally { End(); }
    }
    async Task Upload(string bundle,string serial,CancellationToken token) {
        if(!devices.Contains(serial)) throw new Exception("请选择已连接的 USB 头显。");
        if(!File.Exists(bundle) || new FileInfo(bundle).Length>512L*1024*1024) throw new Exception("模型包不存在或超过 512 MB。");
        string installed=await Adb(serial,token,"shell","pm","path",Package);
        if(!installed.Contains("package:")) throw new Exception("头显尚未安装 Avatar Viewer 0.2.0 或更新版本。请先安装配套 APK。");
        string id=Guid.NewGuid().ToString("N"),file="usb-"+id+".avatarbundle";
        SetMessage("正在通过 USB 上传模型…");
        var library=new PicoAvatarModelLibrary(adbPath,serial);
        string digest=await library.Upload(bundle,file,token,p=> {if(progressStage!=p.stage)phaseStarted=EditorApplication.timeSinceStartup;progressStage=p.stage;uploadBytes=p.bytes;uploadTotal=p.total;progressDeterminate=p.determinate;Repaint();});
        filePublished=true;publishedFile=file;
        string request=Path.Combine(Path.GetDirectoryName(bundle),"usb-request.json");
        File.WriteAllText(request,JsonUtility.ToJson(new UploadRequest { id=id,file=file }));
        await Adb(serial,token,"push",request,DeviceFolder+"/usb-request.json.part");
        await Adb(serial,token,"shell","mv",DeviceFolder+"/usb-request.json.part",DeviceFolder+"/usb-request.json");
        string activity=await Adb(serial,token,"shell","cmd","package","resolve-activity","--brief","-c","android.intent.category.LAUNCHER",Package);
        string component=activity.Split('\n').Select(s=>s.Trim()).LastOrDefault(s=>s.StartsWith(Package+"/"));
        if(component==null) throw new Exception("模型已传入，但未找到查看器启动入口。请在头显中打开 Avatar Viewer。");
        await Adb(serial,token,"shell","am","start","-n",component);
        SetMessage("文件校验通过，等待头显加载模型…请佩戴头显并完成应用权限提示。");
        Stage("文件已保存，等待头显加载确认");
        for(int attempt=0;attempt<60;attempt++) {
            await Task.Delay(1000,token);
            string result=await Adb(serial,token,"shell","sh","-c","'if [ -f "+DeviceFolder+"/usb-result.json ]; then cat "+DeviceFolder+"/usb-result.json; fi'");
            if(!result.TrimStart().StartsWith("{")) continue;
            var ack=JsonUtility.FromJson<UploadResult>(result);
            if(ack==null || ack.id!=id) continue;
            if(!ack.loaded) throw new Exception("头显加载失败："+ack.message);
            Stage("完成：模型已保存并确认加载");SetMessage("上传成功，头显已确认加载："+ack.model+"\n文件："+file+"\n目标设备："+serial+"\nSHA-256："+digest+"\n"+ack.message); return;
        }
        throw new Exception("文件已完整上传，但未收到加载确认。请在头显里打开 Avatar Viewer 0.2.0+ 并检查权限；可从模型库选择刚上传的模型。");
    }
    Task<string> Adb(string serial,CancellationToken token,params string[] args) => Run(adbPath,new[]{"-s",serial}.Concat(args).ToArray(),token);
    void Begin() { operation=PicoAvatarModelLibrary.BeginOperation(); busy=true;filePublished=false;publishedFile="";uploadBytes=uploadTotal=0;Stage("检查 USB 连接"); cancel=new CancellationTokenSource(); EditorApplication.LockReloadAssemblies(); reloadLocked=true; }
    void End() { busy=false; cancel?.Dispose(); cancel=null; operation?.Dispose();operation=null; EditorUtility.ClearProgressBar(); if(reloadLocked) { reloadLocked=false; EditorApplication.UnlockReloadAssemblies(); } Repaint(); }
    void SetMessage(string value) { message=value; Repaint(); Debug.Log("PICO_AVATAR_UPLOAD: "+value); }
    void Stage(string value) {progressStage=value;progressDeterminate=false;phaseStarted=EditorApplication.timeSinceStartup;Repaint();}
    void Fail(Exception error) {
        string previous=progressStage;Stage(error is OperationCanceledException?"已取消 · "+(filePublished?"文件已保存":"尚未保存模型到头显"):"未完成 · "+previous);
        SetMessage(error is OperationCanceledException?(filePublished?"操作已取消；模型文件已完整保存，可在模型管理中找到：\n"+publishedFile:"操作已取消，尚未保存完整模型到头显。取消阶段："+previous):error.Message+(filePublished?"\n模型文件已完整保存："+publishedFile:""));
        if(!(error is OperationCanceledException))Debug.LogException(error);
    }
    public static async Task<string> Run(string executable,IEnumerable<string> args,CancellationToken token) {
        using(var process=new Process()) {
            process.StartInfo=new ProcessStartInfo(executable,string.Join(" ",args.Select(Quote))) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
            process.Start(); var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
            using(token.Register(()=>{try { if(!process.HasExited) process.Kill(); } catch(InvalidOperationException) {}})) await Task.Run(()=>process.WaitForExit());
            string output=await stdout,error=await stderr; token.ThrowIfCancellationRequested();
            if(process.ExitCode!=0) throw new Exception(Path.GetFileName(executable)+" 退出码 "+process.ExitCode+"\n"+output+"\n"+error);
            return output;
        }
    }
    // Windows CommandLineToArgvW quoting, including paths ending in a backslash.
    public static string Quote(string arg) {
        var value=new StringBuilder("\""); int slashes=0;
        foreach(char c in arg) {
            if(c=='\\') { slashes++; continue; }
            if(c=='\"') { value.Append('\\',slashes*2+1); value.Append(c); slashes=0; continue; }
            value.Append('\\',slashes); slashes=0; value.Append(c);
        }
        value.Append('\\',slashes*2); return value.Append('"').ToString();
    }
    [Serializable] sealed class UploadRequest { public string id,file; }
    [Serializable] sealed class UploadResult { public string id,message,model; public bool loaded; }
    void OnDestroy() { cancel?.Cancel();operation?.Dispose();operation=null; if(reloadLocked) { reloadLocked=false; EditorApplication.UnlockReloadAssemblies(); } }
    public static void ExportPlugin() {
        var assets=AssetDatabase.FindAssets("",new[]{Plugin+"/Runtime"}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>!Directory.Exists(p))
            .Concat(new[]{Plugin+"/Editor/AvatarExporter.cs",Plugin+"/Editor/AvatarConstraintExporter.cs",Plugin+"/Editor/AvatarMaterialExporter.cs",Plugin+"/Editor/AvatarMenuExporter.cs",Plugin+"/Editor/PicoAvatarUploader.cs",Plugin+"/Editor/PicoAvatarUsbConnection.cs",Plugin+"/Editor/PicoAvatarModelLibrary.cs",Plugin+"/Editor/PicoAvatarModelManager.cs",Plugin+"/Editor/PicoAvatarBundleBuilder.txt"}).ToArray();
        string path=Path.GetFullPath("../Builds/PicoAvatarUploader-0.3.8.unitypackage"); Directory.CreateDirectory(Path.GetDirectoryName(path));
        AssetDatabase.ExportPackage(assets,path,ExportPackageOptions.Default);
        File.WriteAllText(Path.GetFullPath("../plugin-package-result.json"),"{\"exported\":true,\"assetCount\":"+assets.Length+"}");
        Debug.Log("PICO_UPLOADER_PACKAGE "+path);
    }
    // Exercises the same conversion/build/upload path as the window; used by batch validation.
    public static void SmokeUpload() {
        ExportPlugin();
        var args=Environment.GetCommandLineArgs();
        string scene=Argument(args,"-avatarScene","Assets/shinano Maylian.unity");
        string root=Argument(args,"-avatarRoot","Shinano");
        var loaded=EditorSceneManager.OpenScene(scene);
        var window=CreateInstance<PicoAvatarUploader>();
        window.avatar=loaded.GetRootGameObjects().Single(g=>g.name==root);
        window.adbPath=Argument(args,"-adbPath",FindAdb());
        window.batchBuildOnly=args.Contains("-buildOnly");
        window.lastBundle=Argument(args,"-bundle","");
        if(string.IsNullOrEmpty(window.lastBundle)) window.BuildAndUpload(); else window.UploadLast();
        EditorApplication.CallbackFunction poll=null;
        poll=()=>{
            if(window.busy) return;
            EditorApplication.update-=poll;
            bool success=window.message.StartsWith("上传成功") || (window.batchBuildOnly && window.message.StartsWith("构建成功"));
            File.WriteAllText(Path.GetFullPath(window.batchBuildOnly?"../plugin-build-smoke-result.json":"../plugin-smoke-result.json"),JsonUtility.ToJson(new SmokeResult { success=success,message=window.message,bundle=window.lastBundle },true));
            EditorApplication.Exit(success?0:1);
        };
        EditorApplication.update+=poll;
    }
    static string Argument(string[] args,string key,string fallback) { int index=Array.IndexOf(args,key); return index>=0 && index+1<args.Length?args[index+1]:fallback; }
    [Serializable] sealed class SmokeResult { public bool success; public string message,bundle; }
}
