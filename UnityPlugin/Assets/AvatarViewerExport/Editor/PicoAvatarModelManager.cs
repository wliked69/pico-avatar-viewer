using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class PicoAvatarModelManager : EditorWindow
{
    [SerializeField] string adbPath,device="",search="",selected="",rename="",message="连接 USB 头显后点击“连接并刷新模型”。";
    [SerializeField] string[] devices=new string[0];
    PicoAvatarModelLibrary.ModelFile[] files=new PicoAvatarModelLibrary.ModelFile[0];
    string listedDevice;bool busy,reloadLocked,pendingConnectionRefresh;Vector2 listScroll;
    double nextLibraryRefresh;string focusFile;
    CancellationTokenSource snapshotRead;
    CancellationTokenSource cancellation;IDisposable lease;
    [MenuItem("PICO Avatar/头显模型管理")]
    public static void Open()=>Open(null,null);
    public static void Open(string adb,string serial) {
        var window=GetWindow<PicoAvatarModelManager>("头显模型管理");window.minSize=new Vector2(720,580);
        if(!string.IsNullOrEmpty(adb)) window.adbPath=adb;
        if(!string.IsNullOrEmpty(serial) && window.device!=serial) {window.device=serial;window.devices=new[]{serial};window.ClearList();}
        window.Show();
    }
    void OnEnable() {
        adbPath=PicoAvatarUsbConnection.Resolve(EditorPrefs.GetString("PicoAvatarUploader.adb",""))??"";
        PicoAvatarModelLibrary.ModelsChanged-=OnModelsChanged;PicoAvatarModelLibrary.ModelsChanged+=OnModelsChanged;
        PicoAvatarUsbConnection.Watch(OnUsbChanged);
    }
    void OnDisable() {PicoAvatarUsbConnection.Unwatch(OnUsbChanged);PicoAvatarModelLibrary.ModelsChanged-=OnModelsChanged;snapshotRead?.Cancel();}
    void OnModelsChanged(string serial,string file) {
        if(serial!=device)return;
        pendingConnectionRefresh=true;focusFile=file;
        if(!busy && snapshotRead==null && !string.IsNullOrEmpty(file))RefreshCommittedFile(serial);
    }
    async void RefreshCommittedFile(string serial) {
        snapshotRead=new CancellationTokenSource();var current=snapshotRead;current.CancelAfter(20000);
        try {
            var latest=await new PicoAvatarModelLibrary(adbPath,serial).List(current.Token);
            if(this==null || device!=serial || current.IsCancellationRequested)return;
            ApplyFiles(latest,serial);SetMessage("文件已保存，模型列表已更新。目标："+serial);
        }catch(OperationCanceledException){}catch(Exception) {pendingConnectionRefresh=true;}
        finally {snapshotRead=null;current.Dispose();}
    }
    void OnUsbChanged(PicoAvatarUsbConnection.Snapshot snapshot) {
        if(busy || this==null)return;
        string previous=device;adbPath=snapshot.adbPath;devices=snapshot.devices;device=PicoAvatarUsbConnection.SelectDevice(device,devices);
        if(previous!=device || string.IsNullOrEmpty(device))ClearList();
        if(!string.IsNullOrEmpty(device) && listedDevice!=device)pendingConnectionRefresh=true;
        if(files.Length==0)message=snapshot.message;
        Repaint();
    }
    void OnInspectorUpdate() {
        bool due=pendingConnectionRefresh || EditorApplication.timeSinceStartup>=nextLibraryRefresh;
        if(due && snapshotRead==null && !string.IsNullOrEmpty(device) && !EditorGUIUtility.editingTextField && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !PicoAvatarModelLibrary.OperationInProgress) {bool silent=!pendingConnectionRefresh;pendingConnectionRefresh=false;RefreshInternal(silent);}
    }
    void ClearList() {files=new PicoAvatarModelLibrary.ModelFile[0];listedDevice=null;selected=rename="";focusFile=null;}
    void OnGUI() {
        EditorGUILayout.Space();EditorGUILayout.LabelField("头显模型管理",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("通过 USB 管理 Avatar Viewer 的模型库。加载需要保持头显唤醒；备份、重命名和删除不需要佩戴。",MessageType.Info);
        using(new EditorGUI.DisabledScope(PicoAvatarModelLibrary.OperationInProgress)) {
            EditorGUILayout.BeginHorizontal();adbPath=EditorGUILayout.TextField("ADB 路径",adbPath);
            if(GUILayout.Button("选择…",GUILayout.Width(65))) {string value=EditorUtility.OpenFilePanel("选择 adb","","exe");if(!string.IsNullOrEmpty(value)) adbPath=value;}
            EditorGUILayout.EndHorizontal();
            if(devices.Length>0) {
                var choices=new[]{"请选择目标头显"}.Concat(devices).ToArray();int index=Array.IndexOf(devices,device)+1;
                int next=EditorGUILayout.Popup("目标头显",index,choices);string target=next>0?devices[next-1]:"";
                if(device!=target) {device=target;ClearList();pendingConnectionRefresh=!string.IsNullOrEmpty(device);}
            } else EditorGUILayout.LabelField("目标头显",string.IsNullOrEmpty(device)?"尚未检测 USB 设备":device);
            if(GUILayout.Button("连接并刷新模型",GUILayout.Height(30))) Refresh();
        }
        search=EditorGUILayout.TextField("搜索文件名",search);
        EditorGUILayout.LabelField("共 "+files.Length+" 个模型 · "+Size(files.Sum(f=>f.bytes))+" · 按修改时间排序",EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();GUILayout.Label("模型文件名",EditorStyles.boldLabel);GUILayout.Label("大小",EditorStyles.boldLabel,GUILayout.Width(90));GUILayout.Label("修改时间",EditorStyles.boldLabel,GUILayout.Width(150));EditorGUILayout.EndHorizontal();
        listScroll=EditorGUILayout.BeginScrollView(listScroll,GUILayout.MinHeight(150));
        using(new EditorGUI.DisabledScope(busy)) foreach(var file in files.Where(f=>f.name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)) {
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Toggle(selected==file.name,file.name,"Button",GUILayout.Height(26)) && selected!=file.name) {selected=file.name;rename=Path.GetFileNameWithoutExtension(file.name);}
            GUILayout.Label(Size(file.bytes),GUILayout.Width(90));GUILayout.Label(file.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),GUILayout.Width(150));EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        using(new EditorGUI.DisabledScope(PicoAvatarModelLibrary.OperationInProgress || !files.Any(f=>f.name==selected) || listedDevice!=device)) {
            EditorGUILayout.Space();EditorGUILayout.LabelField("已选："+(string.IsNullOrEmpty(selected)?"无":selected));
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("在头显加载",GUILayout.Height(30))) Perform(async(l,n,t)=> {SetMessage("正在发送加载请求…");string result=await l.Load(n,t,SetMessage);SetMessage("头显已确认加载："+n+"\n"+result);});
            if(GUILayout.Button("备份到电脑…",GUILayout.Height(30))) Backup();
            if(GUILayout.Button("删除…",GUILayout.Height(30))) Delete();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();rename=EditorGUILayout.TextField("新文件名",rename);
            if(GUILayout.Button("重命名",GUILayout.Width(100))) Rename();EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.HelpBox(message,MessageType.None);
        if(busy && GUILayout.Button("取消当前操作")) cancellation?.Cancel();
        EditorGUILayout.LabelField("只处理模型包，不操作校准与应用设置；重命名不改变模型内部名称。",EditorStyles.miniLabel);
    }
    static string Size(long bytes)=>bytes<1024*1024?(bytes/1024.0).ToString("F1")+" KB":(bytes/(1024.0*1024)).ToString("F1")+" MB";
    async Task Detect(CancellationToken token) {
        var snapshot=await PicoAvatarUsbConnection.Probe(adbPath,token);adbPath=snapshot.adbPath;devices=snapshot.devices;
        string target=PicoAvatarUsbConnection.SelectDevice(device,devices);
        if(target!=device) {device=target;ClearList();}
        if(devices.Length==0) {ClearList();throw new IOException(snapshot.message);}
        if(string.IsNullOrEmpty(device)) {ClearList();throw new IOException("检测到多台 USB 头显，请先选择目标设备。");}
    }
    bool Begin() {
        if(PicoAvatarModelLibrary.OperationInProgress) return false;
        lease=PicoAvatarModelLibrary.BeginOperation();busy=true;cancellation=new CancellationTokenSource();
        EditorApplication.LockReloadAssemblies();reloadLocked=true;return true;
    }
    void End() {busy=false;cancellation?.Dispose();cancellation=null;lease?.Dispose();lease=null;if(reloadLocked) {reloadLocked=false;EditorApplication.UnlockReloadAssemblies();}if(this!=null) Repaint();}
    void SetMessage(string value) {message=value;if(this!=null) Repaint();}
    void Failed(Exception error) {SetMessage(error is OperationCanceledException?"操作已取消。已完成的文件操作不会回滚；加载请求可能已发送，请刷新列表查看。":error.Message);}
    void Refresh()=>RefreshInternal(false);
    async void RefreshInternal(bool silent) {
        if(!Begin()) return;
        try {if(!silent)SetMessage("正在连接并读取模型列表…");await Detect(cancellation.Token);await Read(new PicoAvatarModelLibrary(adbPath,device),cancellation.Token);if(!silent || message.StartsWith("已读取"))SetMessage("已读取 "+files.Length+" 个模型。目标："+device);}
        catch(Exception error) {ClearList();nextLibraryRefresh=EditorApplication.timeSinceStartup+10;Failed(error);}finally {End();}
    }
    async Task Read(PicoAvatarModelLibrary library,CancellationToken token) {
        ApplyFiles(await library.List(token),library.Device);
    }
    void ApplyFiles(PicoAvatarModelLibrary.ModelFile[] latest,string serial) {
        files=latest;listedDevice=serial;
        nextLibraryRefresh=EditorApplication.timeSinceStartup+10;pendingConnectionRefresh=false;
        if(!string.IsNullOrEmpty(focusFile) && files.Any(f=>f.name==focusFile)) {selected=focusFile;rename=Path.GetFileNameWithoutExtension(focusFile);search="";focusFile=null;}
        if(!files.Any(f=>f.name==selected)) selected=rename="";
    }
    async void Perform(Func<PicoAvatarModelLibrary,string,CancellationToken,Task> action) {
        if(listedDevice!=device || !files.Any(f=>f.name==selected) || !Begin()) return;
        string name=selected;var library=new PicoAvatarModelLibrary(adbPath,device);
        try {await action(library,name,cancellation.Token);await Read(library,cancellation.Token);}
        catch(Exception error) {Failed(error);}finally {End();}
    }
    void Backup() {
        string path=EditorUtility.SaveFilePanel("备份模型（请使用未存在的文件名）",EditorPrefs.GetString("PicoAvatarBackupFolder",""),Path.GetFileNameWithoutExtension(selected),"avatarbundle");
        if(string.IsNullOrEmpty(path)) return;EditorPrefs.SetString("PicoAvatarBackupFolder",Path.GetDirectoryName(path));
        Perform(async(l,n,t)=> {SetMessage("正在备份并校验："+n);await l.Backup(n,path,t);SetMessage("备份成功，SHA-256 校验通过：\n"+path);});
    }
    void Rename() {
        string target;try {target=PicoAvatarModelLibrary.NormalizeRename(rename);}catch(Exception e) {Failed(e);return;}
        if(!EditorUtility.DisplayDialog("重命名头显模型",selected+"\n→ "+target+"\n目标头显："+device+"\n不会覆盖同名文件。","重命名","取消")) return;
        Perform(async(l,n,t)=> {await l.Rename(n,target,t);selected=target;SetMessage("已重命名："+n+" → "+target+"。当前显示的模型保持加载；请重新扫描头显模型库查看新文件名。");});
    }
    void Delete() {
        if(!EditorUtility.DisplayDialog("删除头显中的模型文件？","文件："+selected+"\n目标头显："+device+"\n只删除这一份模型包，无法撤销。建议先备份。已加载的角色会保留到切换或退出应用；内置示例可能在重启时重新生成。","删除这份文件","取消")) return;
        Perform(async(l,n,t)=> {await l.Delete(n,t);SetMessage("已删除文件："+n+"。头显中已加载的角色不会立即卸载，请重新扫描模型库。");});
    }
    void OnDestroy() {cancellation?.Cancel();lease?.Dispose();lease=null;if(reloadLocked) {reloadLocked=false;EditorApplication.UnlockReloadAssemblies();}}
}
