using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// Shared read-only discovery. Poll only while a plugin window is open, and never
// interrupt an upload/file operation or restart the ADB server to reconnect.
[InitializeOnLoad]
public static class PicoAvatarUsbConnection {
    const string Preference="PicoAvatarUploader.adb";
    public sealed class Snapshot {
        public string adbPath,message;
        public string[] devices=new string[0];
    }
    static Action<Snapshot> listeners;
    static CancellationTokenSource scan;
    static double nextPoll;
    public static Snapshot Current {get;private set;}
    static PicoAvatarUsbConnection() {
        EditorApplication.update+=Update;
        AssemblyReloadEvents.beforeAssemblyReload+=()=>scan?.Cancel();
    }
    public static void Watch(Action<Snapshot> listener) {
        if(Application.isBatchMode) return;
        listeners-=listener;listeners+=listener;nextPoll=0;
        if(Current!=null)listener(Current);
    }
    public static void Unwatch(Action<Snapshot> listener) {
        listeners-=listener;if(listeners==null)scan?.Cancel();
    }
    public static string FindExecutable(IEnumerable<string> candidates) {
        foreach(string candidate in candidates) {
            if(string.IsNullOrWhiteSpace(candidate))continue;
            string path=candidate.Trim().Trim('"');
            try {if(File.Exists(path))return Path.GetFullPath(path);}catch(ArgumentException){}catch(NotSupportedException){}
        }
        return null;
    }
    public static string Resolve(string preferred=null) {
        string executable=Application.platform==RuntimePlatform.WindowsEditor?"adb.exe":"adb";
        var candidates=new List<string>();
        if(!string.IsNullOrWhiteSpace(preferred))candidates.Add(preferred);
        void Sdk(string root) {if(!string.IsNullOrWhiteSpace(root))candidates.Add(Path.Combine(root,"platform-tools",executable));}
        Sdk(EditorPrefs.GetString("AndroidSdkRoot",""));
        Sdk(Path.Combine(EditorApplication.applicationContentsPath,"PlaybackEngines/AndroidPlayer/SDK"));
        Sdk(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"));Sdk(Environment.GetEnvironmentVariable("ANDROID_HOME"));
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if(!string.IsNullOrEmpty(local))Sdk(Path.Combine(local,"Android","Sdk"));
        if(!string.IsNullOrEmpty(user)) {Sdk(Path.Combine(user,"Library","Android","sdk"));Sdk(Path.Combine(user,"Android","Sdk"));}
        foreach(string entry in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
            if(!string.IsNullOrWhiteSpace(entry))try {candidates.Add(Path.Combine(entry.Trim().Trim('"'),executable));}catch(ArgumentException){}
        return FindExecutable(candidates);
    }
    static IEnumerable<string[]> Rows(string output) {
        foreach(string line in output.Split('\n')) {
            var parts=line.Trim().Split(new[]{' ','\t','\r'},StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length>=2 && !parts[0].StartsWith("emulator-",StringComparison.Ordinal) && !parts[0].Contains(":"))yield return parts;
        }
    }
    public static Snapshot Parse(string output,string adb) {
        var rows=Rows(output).ToArray();
        var devices=rows.Where(p=>p[1]=="device").Select(p=>p[0]).Distinct().ToArray();
        string message=devices.Length==0?"等待 USB 头显接入。请检查数据线，并在头显内启用 USB 调试。":devices.Length==1?"已自动连接头显："+devices[0]:"检测到多台 USB 头显，请确认目标设备。";
        var unauthorized=rows.Where(p=>p[1]=="unauthorized").Select(p=>p[0]).ToArray();
        var offline=rows.Where(p=>p[1]=="offline").Select(p=>p[0]).ToArray();
        if(unauthorized.Length>0)message+="\n请在头显内接受本电脑的 USB 调试授权："+string.Join("、",unauthorized);
        if(offline.Length>0)message+="\n设备离线，请唤醒头显或重新插拔数据线："+string.Join("、",offline);
        return new Snapshot {adbPath=adb??"",devices=devices,message=message};
    }
    public static string SelectDevice(string current,string[] devices) =>devices.Contains(current)?current:devices.Length==1?devices[0]:"";
    public static async Task<Snapshot> Probe(string preferred,CancellationToken token) {
        string adb=Resolve(preferred);
        if(adb==null)return new Snapshot {adbPath="",message="未找到 ADB。请安装 Android SDK Platform-Tools，或在 ADB 路径中选择 adb 程序。"};
        EditorPrefs.SetString(Preference,adb);
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)) {
            timeout.CancelAfter(20000);
            try {return Parse(await PicoAvatarUploader.Run(adb,new[]{"devices","-l"},timeout.Token),adb);}
            catch(OperationCanceledException) when(!token.IsCancellationRequested) {return new Snapshot {adbPath=adb,message="USB 检测超时，稍后将自动重试。"};}
        }
    }
    static async void Update() {
        if(listeners==null || scan!=null || EditorApplication.timeSinceStartup<nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorGUIUtility.editingTextField || PicoAvatarModelLibrary.OperationInProgress)return;
        scan=new CancellationTokenSource();var currentScan=scan;
        try {
            var snapshot=await Probe(EditorPrefs.GetString(Preference,""),currentScan.Token);
            currentScan.Token.ThrowIfCancellationRequested();
            if(PicoAvatarModelLibrary.OperationInProgress)return;
            bool changed=Current==null || Current.adbPath!=snapshot.adbPath || Current.message!=snapshot.message || !Current.devices.SequenceEqual(snapshot.devices);
            Current=snapshot;
            if(changed && listeners!=null)foreach(Action<Snapshot> listener in listeners.GetInvocationList())try {listener(snapshot);}catch(Exception error){Debug.LogException(error);}
        }catch(OperationCanceledException){}catch(Exception error) {
            Current=new Snapshot {adbPath=Resolve()??"",message="USB 检测失败，将自动重试："+error.Message};
            if(listeners!=null)foreach(Action<Snapshot> listener in listeners.GetInvocationList())try {listener(Current);}catch(Exception failure){Debug.LogException(failure);}
        }finally {scan=null;currentScan.Dispose();nextPoll=EditorApplication.timeSinceStartup+3;}
    }
}
