using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// USB file operations are confined to the viewer's Models directory. No VRC SDK dependency.
public sealed class PicoAvatarModelLibrary
{
    public const string Package="com.psp.vrcavatarviewer";
    public const string DeviceFolder="/sdcard/Android/data/"+Package+"/files";
    public const string ModelsFolder=DeviceFolder+"/Models";
    public sealed class ModelFile {
        public string name; public long bytes,modified;
        public DateTime ModifiedUtc=>new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(modified);
    }
    public delegate Task<string> Executor(IEnumerable<string> args,CancellationToken token);
    readonly Executor execute;
    public string Device { get; }
    public static bool OperationInProgress { get; private set; }
    public static event Action<string,string> ModelsChanged;
    public sealed class TransferProgress {public string stage;public long bytes,total;public bool determinate;}
    public static void NotifyModelsChanged(string device,string file=null) {
        if(ModelsChanged==null)return;
        foreach(Action<string,string> listener in ModelsChanged.GetInvocationList())try {listener(device,file);}catch(Exception error){Debug.LogException(error);}
    }
    sealed class Lease : IDisposable { bool disposed; public void Dispose() {if(disposed)return;disposed=true;OperationInProgress=false;} }
    public static IDisposable BeginOperation() {
        if(OperationInProgress) throw new InvalidOperationException("另一个模型上传或管理操作正在进行，请稍后再试。");
        OperationInProgress=true;return new Lease();
    }
    public PicoAvatarModelLibrary(string adb,string device,Executor executor=null) {
        if(string.IsNullOrWhiteSpace(device) || device.Any(char.IsWhiteSpace)) throw new ArgumentException("请先选择目标 USB 头显。");
        Device=device;execute=executor??((args,token)=>PicoAvatarUploader.Run(adb,args,token));
    }
    Task<string> Adb(CancellationToken token,params string[] args)=>execute(new[]{"-s",Device}.Concat(args),token);
    Task<string> Shell(string command,CancellationToken token)=>Adb(token,"shell",command);
    public static string ShellQuote(string text)=>"'"+text.Replace("'","'\"'\"'")+"'";
    public static string ValidateName(string name) {
        if(string.IsNullOrWhiteSpace(name) || name.Length<=13 || name.Length>180 || name=="." || name==".." || name.IndexOfAny(new[]{'/','\\',':','*','?','\"','<','>','|'})>=0 || name.Any(char.IsControl) || name!=name.Trim() || !name.EndsWith(".avatarbundle",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请输入单个模型文件名（.avatarbundle），不能包含路径、控制字符或 Windows 文件名禁用符号。");
        return name;
    }
    public static string NormalizeRename(string name) {
        name=(name??"").Trim();if(!name.EndsWith(".avatarbundle",StringComparison.OrdinalIgnoreCase)) name+=".avatarbundle";
        else name=name.Substring(0,name.Length-13)+".avatarbundle";
        return ValidateName(name);
    }
    static string FilePath(string name)=>ModelsFolder+"/"+ValidateName(name);
    static string Guard(string name) {
        string p=ShellQuote(FilePath(name));
        return "[ ! -L "+ShellQuote(ModelsFolder)+" ] && [ -f "+p+" ] && [ ! -L "+p+" ] || { echo '模型不存在或不是普通文件'; exit 1; }; ";
    }
    public static ModelFile[] ParseListing(string text) {
        var result=new List<ModelFile>();var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(string raw in text.Split('\n')) {
            string line=raw.TrimEnd('\r');if(line.Length==0) continue;
            int a=line.IndexOf('\t'),b=a<0?-1:line.IndexOf('\t',a+1);
            if(a<1 || b<a+2 || !long.TryParse(line.Substring(0,a),NumberStyles.None,CultureInfo.InvariantCulture,out long size) || !long.TryParse(line.Substring(a+1,b-a-1),NumberStyles.None,CultureInfo.InvariantCulture,out long time)) throw new FormatException("无法解析头显文件列表，请刷新重试。");
            string path=line.Substring(b+1);
            if(!path.StartsWith(ModelsFolder+"/",StringComparison.Ordinal)) throw new FormatException("文件不在模型目录中。");
            string name=path.Substring(ModelsFolder.Length+1);
            try {ValidateName(name);} catch(ArgumentException) {continue;} // Non-portable names cannot be managed safely on Windows.
            var file=new ModelFile {name=name,bytes=size,modified=time};_ = file.ModifiedUtc;
            if(names.Add(name)) result.Add(file);
        }
        return result.OrderByDescending(f=>f.modified).ThenBy(f=>f.name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async Task<ModelFile[]> List(CancellationToken token) {
        string folder=ShellQuote(ModelsFolder);
        string script="if [ ! -d "+folder+" ]; then exit 0; fi; [ ! -L "+folder+" ] || exit 1; for f in "+folder+"/*; do [ -f \"$f\" ] && [ ! -L \"$f\" ] || continue; case \"$f\" in *.[aA][vV][aA][tT][aA][rR][bB][uU][nN][dD][lL][eE]) stat -c '%s\t%Y\t%n' -- \"$f\" || exit 1;; esac; done";
        return ParseListing(await Shell(script,token));
    }
    public async Task<string> Upload(string localPath,string name,CancellationToken token,Action<TransferProgress> progress=null) {
        ValidateName(name);var local=new FileInfo(localPath);
        if(!local.Exists || local.Length<=0 || local.Length>512L*1024*1024)throw new IOException("模型包不存在、为空或超过 512 MB。");
        long total=local.Length,transferred=0;string remote=FilePath(name),partial=remote+".part";
        void Report(string stage,bool determinate=false) {progress?.Invoke(new TransferProgress {stage=stage,bytes=transferred,total=total,determinate=determinate});}
        Report("计算文件校验值");
        string digest=await Task.Run(()=> {using(var input=File.OpenRead(localPath))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();},token);
        token.ThrowIfCancellationRequested();
        await Shell("[ ! -L "+ShellQuote(ModelsFolder)+" ] && mkdir -p "+ShellQuote(ModelsFolder),token);
        Report("USB 传输",true);
        var push=Adb(token,"push",localPath,partial);
        try {
            while(!push.IsCompleted) {
                if(await Task.WhenAny(push,Task.Delay(350))==push)break;
                token.ThrowIfCancellationRequested();
                try {
                    using(var sample=CancellationTokenSource.CreateLinkedTokenSource(token)) {
                        sample.CancelAfter(3000);
                        string count=await Shell("if [ -f "+ShellQuote(partial)+" ]; then stat -c %s -- "+ShellQuote(partial)+"; else echo 0; fi",sample.Token);
                        if(long.TryParse(count.Trim(),out long bytes)) {transferred=Math.Max(transferred,Math.Min(Math.Max(0,bytes),total-1));Report("USB 传输",true);}
                    }
                }catch(Exception) {token.ThrowIfCancellationRequested();} // A failed progress sample must not abort a healthy transfer.
            }
            await push;
        }catch {try {await push;}catch{}throw;}
        token.ThrowIfCancellationRequested();transferred=total;Report("USB 传输",true);
        Report("校验头显文件");
        string remoteHash=await Shell("sha256sum -- "+ShellQuote(partial),token);
        if(!remoteHash.StartsWith(digest,StringComparison.OrdinalIgnoreCase))throw new IOException("USB 文件校验失败，未发布到模型库。");
        Report("保存到模型库");
        await Shell("[ ! -e "+ShellQuote(remote)+" ] && [ ! -L "+ShellQuote(remote)+" ] && mv -n -- "+ShellQuote(partial)+" "+ShellQuote(remote)+" && [ ! -e "+ShellQuote(partial)+" ]",token);
        NotifyModelsChanged(Device,name);
        return digest;
    }
    public async Task Rename(string name,string destination,CancellationToken token) {
        ValidateName(name);ValidateName(destination);if(name==destination) throw new ArgumentException("新名称与原名称相同。");
        string source=ShellQuote(FilePath(name)),target=ShellQuote(FilePath(destination));
        await Shell(Guard(name)+"[ ! -e "+target+" ] && [ ! -L "+target+" ] || { echo '目标文件已存在，不会覆盖'; exit 1; }; mv -n -- "+source+" "+target+" && [ ! -e "+source+" ]",token);
    }
    public Task Delete(string name,CancellationToken token)=>Shell(Guard(name)+"rm -- "+ShellQuote(FilePath(name))+" && [ ! -e "+ShellQuote(FilePath(name))+" ]",token);
    async Task<string> RemoteHash(string name,CancellationToken token) {
        string result=await Shell(Guard(name)+"sha256sum -- "+ShellQuote(FilePath(name)),token);
        string digest=result.Split(new[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if(digest==null || digest.Length!=64 || digest.Any(c=>!Uri.IsHexDigit(c))) throw new IOException("头显未返回有效的 SHA-256 校验值。");return digest;
    }
    public async Task Backup(string name,string localPath,CancellationToken token) {
        ValidateName(name);string full=Path.GetFullPath(localPath);
        if(File.Exists(full) || Directory.Exists(full)) throw new IOException("备份目标已经存在，请换一个文件名；不会覆盖已有文件。");
        if(!Directory.Exists(Path.GetDirectoryName(full))) throw new DirectoryNotFoundException("备份目录不存在。");
        string partial=full+".partial-"+Guid.NewGuid().ToString("N");
        try {
            string expected=await RemoteHash(name,token);
            await Adb(token,"pull",FilePath(name),partial);token.ThrowIfCancellationRequested();
            string actual=await Task.Run(()=> {using(var input=File.OpenRead(partial)) using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");},token);
            string after=await RemoteHash(name,token);token.ThrowIfCancellationRequested();
            if(!expected.Equals(actual,StringComparison.OrdinalIgnoreCase) || !expected.Equals(after,StringComparison.OrdinalIgnoreCase)) throw new IOException("备份校验失败或源文件已变化，未生成最终备份。");
            File.Move(partial,full);
        } finally {if(File.Exists(partial)) File.Delete(partial);}
    }
    [Serializable] sealed class LoadRequest {public string id,file;}
    [Serializable] sealed class LoadResult {public string id,message,model;public bool loaded;}
    public static bool TryReadResult(string text,string id,out bool loaded,out string message) {
        loaded=false;message=null;if(string.IsNullOrEmpty(text) || !text.TrimStart().StartsWith("{")) return false;
        try {var ack=JsonUtility.FromJson<LoadResult>(text);if(ack==null || ack.id!=id) return false;loaded=ack.loaded;message=ack.message;return true;}
        catch(ArgumentException) {return false;}
    }
    public async Task<string> Load(string name,CancellationToken token,Action<string> progress=null) {
        string installed=await Shell(Guard(name)+"pm path "+Package,token);
        if(!installed.Contains("package:")) throw new IOException("目标头显尚未安装 Avatar Viewer，请先安装配套应用。");
        string id=Guid.NewGuid().ToString("N"),local=Path.GetFullPath("Library/PicoAvatarUpload/Requests/"+id+".json");
        string remote=DeviceFolder+"/usb-request.json",partial=remote+"."+id+".part";
        Directory.CreateDirectory(Path.GetDirectoryName(local));File.WriteAllText(local,JsonUtility.ToJson(new LoadRequest {id=id,file=name}));
        try {
            await Adb(token,"push",local,partial);await Shell("mv -- "+ShellQuote(partial)+" "+ShellQuote(remote),token);
            string activity=await Adb(token,"shell","cmd","package","resolve-activity","--brief","-c","android.intent.category.LAUNCHER",Package);
            string component=activity.Split('\n').Select(s=>s.Trim()).LastOrDefault(s=>s.StartsWith(Package+"/",StringComparison.Ordinal));
            if(component==null) throw new IOException("加载请求已发出，请在头显中打开 Avatar Viewer。");
            await Adb(token,"shell","am","start","-a","android.intent.action.MAIN","-c","android.intent.category.LAUNCHER","-f","0x10200000","-n",component);
            progress?.Invoke("加载请求已发送，等待头显确认。请佩戴头显并打开 Avatar Viewer…");
            for(int i=0;i<60;i++) {
                await Task.Delay(1000,token);
                string text=await Shell("if [ -f "+ShellQuote(DeviceFolder+"/usb-result.json")+" ]; then cat "+ShellQuote(DeviceFolder+"/usb-result.json")+"; fi",token);
                if(!TryReadResult(text,id,out bool loaded,out string message)) continue;
                if(!loaded) throw new IOException("头显加载失败："+message);return message;
            }
            throw new IOException("未收到头显加载确认。请唤醒头显并打开 Avatar Viewer；加载请求可能仍在等待处理。");
        } finally {if(File.Exists(local)) File.Delete(local);}
    }
}
