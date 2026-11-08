using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Export shader source and include files as ordinary assets. Shader importers,
// editor plugins and arbitrary source scripts are never copied into the viewer.
public static class AvatarMaterialExporter {
    const string Folder="Assets/AvatarViewerExport/Generated/OriginalShaders";
    static readonly Dictionary<string,string> shaders=new Dictionary<string,string>();
    static readonly Dictionary<string,string> includes=new Dictionary<string,string>();
    static readonly Dictionary<string,string> includeFallbacks=new Dictionary<string,string>();
    static List<string> warnings;
    static readonly HashSet<string> animatedProperties=new HashSet<string>();
    public static void Begin(List<string> messages,GameObject avatar=null) {
        shaders.Clear(); includes.Clear(); includeFallbacks.Clear(); animatedProperties.Clear(); warnings=messages; Directory.CreateDirectory(Folder+"/Includes");
        if(avatar!=null) foreach(var clip in EditorUtility.CollectDependencies(new UnityEngine.Object[]{avatar}).OfType<AnimationClip>()) {
            foreach(var binding in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip))) {
                var property=Regex.Match(binding.propertyName,@"material\.(_\w+)"); if(property.Success) animatedProperties.Add(property.Groups[1].Value);
            }
        }
    }
    static HashSet<string> DisabledFeatures(Material material) {
        var disabled=new HashSet<string>();
        for(int i=0;i<material.shader.GetPropertyCount();i++) {
            string property=material.shader.GetPropertyName(i);
            if(material.shader.GetPropertyType(i)==ShaderPropertyType.Texture && property.StartsWith("_") && material.GetTexture(property)==null && !animatedProperties.Contains(property)) disabled.Add("LIL_FEATURE_"+property.Substring(1));
        }
        var toggles=new Dictionary<string,string> {
            {"_UseShadow","SHADOW"},{"_UseRimShade","RIMSHADE"},{"_UseMain2ndTex","MAIN2ND"},{"_UseMain3rdTex","MAIN3RD"},
            {"_UseEmission","EMISSION_1ST"},{"_UseEmission2nd","EMISSION_2ND"},{"_UseBumpMap","NORMAL_1ST"},{"_UseBump2ndMap","NORMAL_2ND"},
            {"_UseAnisotropy","ANISOTROPY"},{"_UseReflection","REFLECTION"},{"_UseMatCap","MATCAP"},{"_UseMatCap2nd","MATCAP_2ND"},
            {"_UseRim","RIMLIGHT"},{"_UseGlitter","GLITTER"},{"_UseBacklight","BACKLIGHT"},{"_UseParallax","PARALLAX"},{"_UsePOM","POM"},
            {"_UseAudioLink","AUDIOLINK"},{"_UseDither","DITHER"}
        };
        foreach(var pair in toggles) if(material.HasProperty(pair.Key) && material.GetFloat(pair.Key)==0 && !animatedProperties.Contains(pair.Key)) disabled.Add("LIL_FEATURE_"+pair.Value);
        return disabled;
    }
    static string Hash(string text) { using(var hash=MD5.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant(); }
    static string Normalize(string path) => path.Replace('\\','/');
    static void WriteChanged(string path,string value) { if(!File.Exists(path) || File.ReadAllText(path)!=value) File.WriteAllText(path,value,new UTF8Encoding(false)); }
    static string RewriteIncludes(string text,string originalPath) {
        return Regex.Replace(text,@"(#include\s*[""<])([^"">]+)(["">])",match=>{
            string name=match.Groups[2].Value;
            string resolved=name.StartsWith("Assets/") || name.StartsWith("Packages/")?name:Normalize(Path.Combine(Path.GetDirectoryName(originalPath),name));
            if(!File.Exists(resolved) && name.StartsWith("Includes/lil_")) {
                if(!includeFallbacks.TryGetValue(name,out var fallback)) { fallback=AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(name)).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p=>p.EndsWith("/"+name,StringComparison.Ordinal)); includeFallbacks[name]=fallback; }
                if(fallback!=null) resolved=fallback;
            }
            if(!File.Exists(resolved)) return match.Value; // Unity's built-in includes.
            resolved=Normalize(Path.GetFullPath(resolved));
            if(!includes.TryGetValue(resolved,out var copied)) {
                copied=Folder+"/Includes/"+Hash(resolved)+Path.GetExtension(resolved); includes[resolved]=copied;
                WriteChanged(copied,RewriteIncludes(File.ReadAllText(resolved),resolved));
            }
            return "#include \""+copied+"\"";
        });
    }
    static string CopyShader(Shader shader,HashSet<string> disabled) {
        if(shader==null) return null;
        string path=AssetDatabase.GetAssetPath(shader);
        if(!File.Exists(path)) return shader.name; // Built-in shader.
        string key=path+"|"+string.Join(",",disabled.OrderBy(x=>x));
        if(shaders.TryGetValue(key,out string existing)) return existing;
        string source;
        if(Path.GetExtension(path)==".lilcontainer") {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("lilToon.lilShaderContainer")).FirstOrDefault(t=>t!=null);
            var method=type?.GetMethods(BindingFlags.Public|BindingFlags.Static).FirstOrDefault(m=>m.Name=="UnpackContainer" && m.GetParameters().Length==2);
            if(method==null) throw new Exception("lilToon public shader unpacker unavailable");
            source=method.Invoke(null,new object[]{path,null}) as string;
        } else source=File.ReadAllText(path);
        // GLES exposes fewer texture units than desktop GPUs. Retain features
        // actually used by this material, including properties animated by FX.
        source=Regex.Replace(source,@"(?m)^[ \t]*#define[ \t]+(LIL_FEATURE_\w+)[^\r\n]*",m=>disabled.Contains(m.Groups[1].Value)?"// AvatarViewer: unused "+m.Groups[1].Value:m.Value);
        string name="AvatarViewer/Original/"+Hash(key); shaders[key]=name;
        source=Regex.Replace(source,@"Shader\s+""[^""]+""","Shader \""+name+"\"",RegexOptions.None,TimeSpan.FromSeconds(2));
        source=Regex.Replace(source,@"UsePass\s+""([^""]+)""",match=>{
            string reference=match.Groups[1].Value; int split=reference.LastIndexOf('/'); if(split<0) return match.Value;
            var dependency=Shader.Find(reference.Substring(0,split)); if(dependency==null) return match.Value;
            return "UsePass \""+CopyShader(dependency,disabled)+reference.Substring(split)+"\"";
        });
        source=Regex.Replace(source,@"Fallback\s+""([^""]+)""",match=> { var dependency=Shader.Find(match.Groups[1].Value); return dependency==null?match.Value:"Fallback \""+CopyShader(dependency,disabled)+"\""; });
        source=Regex.Replace(source,@"(Dependency\s+""[^""]+""\s*=\s*)""([^""]+)""",match=> { var dependency=Shader.Find(match.Groups[2].Value); return dependency==null?match.Value:match.Groups[1].Value+"\""+CopyShader(dependency,disabled)+"\""; });
        source=RewriteIncludes(source,path);
        // Include files must be known to the asset database before shader import.
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string destination=Folder+"/"+Hash(key)+".shader"; WriteChanged(destination,source); AssetDatabase.ImportAsset(destination,ImportAssetOptions.ForceSynchronousImport);
        return name;
    }
    public static Material Convert(Material original) {
        try {
            string name=CopyShader(original.shader,DisabledFeatures(original)); var shader=Shader.Find(name);
            if(shader==null) throw new Exception("Copied shader was not imported");
            // Cloning a material variant retains its parent and its original
            // shader importer. Flatten the effective values into a plain asset.
            var material=new Material(shader) { name=original.name+"_Original" };
            for(int i=0;i<original.shader.GetPropertyCount();i++) {
                string property=original.shader.GetPropertyName(i);
                if(!material.HasProperty(property)) continue;
                switch(original.shader.GetPropertyType(i)) {
                    case ShaderPropertyType.Color: material.SetColor(property,original.GetColor(property)); break;
                    case ShaderPropertyType.Vector: material.SetVector(property,original.GetVector(property)); break;
                    case ShaderPropertyType.Texture:
                        material.SetTexture(property,original.GetTexture(property));
                        material.SetTextureScale(property,original.GetTextureScale(property));
                        material.SetTextureOffset(property,original.GetTextureOffset(property)); break;
                    case ShaderPropertyType.Int: material.SetInteger(property,original.GetInteger(property)); break;
                    default: material.SetFloat(property,original.GetFloat(property)); break;
                }
            }
            material.shaderKeywords=original.shaderKeywords;
            material.renderQueue=original.renderQueue;
            material.enableInstancing=original.enableInstancing;
            material.doubleSidedGI=original.doubleSidedGI;
            material.globalIlluminationFlags=original.globalIlluminationFlags;
            foreach(string tag in new[]{"RenderType","Queue","IgnoreProjector","DisableBatching","VRCFallback"}) {
                string value=original.GetTag(tag,false,"");
                if(value.Length>0) material.SetOverrideTag(tag,value);
            }
            for(int i=0;i<original.passCount;i++) {
                string pass=original.GetPassName(i);
                if(pass.Length>0) material.SetShaderPassEnabled(pass,original.GetShaderPassEnabled(pass));
            }
            return material;
        } catch(Exception error) {
            warnings?.Add("Shader fallback for "+original.name+": "+error.Message);
            var portable=new Material(Shader.Find("AvatarViewer/PortableToon")) { name=original.name+"_Fallback" };
            if(original.HasProperty("_MainTex")) { portable.mainTexture=original.GetTexture("_MainTex"); portable.mainTextureScale=original.mainTextureScale; portable.mainTextureOffset=original.mainTextureOffset; }
            if(original.HasProperty("_Color")) portable.color=original.GetColor("_Color");
            bool alpha=original.renderQueue>=3000; portable.SetFloat("_Cutoff",!alpha && original.renderQueue>=2450?(original.HasProperty("_Cutoff")?original.GetFloat("_Cutoff"):.5f):0);
            if(alpha) { portable.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha); portable.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha); portable.SetFloat("_ZWrite",0); }
            portable.renderQueue=original.renderQueue; return portable;
        }
    }
}
