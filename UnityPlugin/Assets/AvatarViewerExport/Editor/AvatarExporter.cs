using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AvatarViewer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class AvatarExporter
{
    const string Generated = "Assets/AvatarViewerExport/Generated";
    [MenuItem("Avatar Viewer/Export scene avatar for PICO")]
    public static void Run()
    {
        string scenePath=Argument("-avatarScene","");
        string rootName=Argument("-avatarRoot","");
        if(string.IsNullOrEmpty(scenePath) || string.IsNullOrEmpty(rootName)) throw new ArgumentException("Provide -avatarScene and -avatarRoot, or use PICO Avatar / USB 模型上传.");
        var scene = EditorSceneManager.OpenScene(scenePath);
        var original = scene.GetRootGameObjects().Single(x => x.name == rootName);
        Export(original,Argument("-avatarName",original.name),Path.GetFullPath(Argument("-viewerProject","../App")),Path.GetFullPath("../Exports"));
    }
    public static void Export(GameObject original,string displayName,string viewer,string output)
    {
        if(original==null) throw new ArgumentNullException(nameof(original));
        string scenePath=original.scene.path;
        Directory.CreateDirectory(Generated);
        AssetDatabase.Refresh();
        // Use the installed NDMF public editor API to bake Modular Avatar outfits.
        var processor=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("nadena.dev.ndmf.AvatarProcessor")).FirstOrDefault(t=>t!=null);
        var bake=processor?.GetMethods(BindingFlags.Public|BindingFlags.Static).FirstOrDefault(m=>m.Name=="ManualProcessAvatar" && m.GetParameters().Length==2);
        var avatar = bake != null ? (GameObject)bake.Invoke(null,new object[]{original,null}):Object.Instantiate(original);
        try {
        avatar.name = displayName;
        avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        avatar.transform.localScale = Vector3.one;
        var warnings = new List<string>();
        AvatarMaterialExporter.Begin(warnings,avatar);
        // Reactive outfit processing can delete body sections for avatar FX controllers.
        // This viewer has no FX controller: retain the complete source body and its skin binding.
        var sourceBody=original.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s=>s.name=="Body_base");
        var bakedBody=avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s=>s.name=="Body_base");
        if(sourceBody!=null && bakedBody!=null) {
            bakedBody.sharedMesh=sourceBody.sharedMesh; bakedBody.sharedMaterials=sourceBody.sharedMaterials;
            bakedBody.bones=sourceBody.bones.Select(b=>b==null?null:FindOrCopyBone(b,original.transform,avatar.transform)).ToArray();
            bakedBody.rootBone=sourceBody.rootBone!=null?FindOrCopyBone(sourceBody.rootBone,original.transform,avatar.transform):null;
            bakedBody.enabled=true; bakedBody.gameObject.SetActive(true); bakedBody.updateWhenOffscreen=true;
            for(int i=0;i<sourceBody.sharedMesh.blendShapeCount;i++) {
                string shape=sourceBody.sharedMesh.GetBlendShapeName(i);
                bakedBody.SetBlendShapeWeight(i,shape.IndexOf("shrink",StringComparison.OrdinalIgnoreCase)>=0?0:sourceBody.GetBlendShapeWeight(i));
            }
            warnings.Add("Restored complete Body_base from source; reactive body shrink masks omitted for standalone viewing.");
        }
        var components = avatar.GetComponentsInChildren<Component>(true).Where(c => c != null).ToArray();
        var colliderMap = new Dictionary<Component,SpringCollider>();
        foreach (var component in components.Where(c => c.GetType().Name == "VRCPhysBoneCollider"))
        {
            var collider = component.gameObject.AddComponent<SpringCollider>();
            collider.enabled = ((Behaviour)component).enabled;
            var root = ReadObject(component,"rootTransform") as Transform;
            if (root != null) { Object.DestroyImmediate(collider); collider=root.gameObject.AddComponent<SpringCollider>(); }
            collider.center = Read(component,"position",Vector3.zero);
            var rotation = ReadObject(component,"rotation");
            collider.rotation = rotation is Quaternion q ? q : rotation is Vector3 e ? Quaternion.Euler(e) : Quaternion.identity;
            collider.radius=Read(component,"radius",.05f); collider.height=Read(component,"height",0f);
            int shape=Convert.ToInt32(ReadObject(component,"shapeType") ?? 0);
            if (shape > 1) warnings.Add("Unsupported collider shape approximated as sphere: " + component.name);
            collider.capsule=shape==1; collider.inside=Read(component,"insideBounds",false);
            colliderMap.Add(component,collider);
        }
        int chainCount=0;
        foreach (var component in components.Where(c => c.GetType().Name == "VRCPhysBone"))
        {
            var chain=component.gameObject.AddComponent<SpringChain>();
            chain.enabled=((Behaviour)component).enabled;
            chain.root=ReadObject(component,"rootTransform") as Transform; if(chain.root==null) chain.root=component.transform;
            chain.endpoint=Read(component,"endpointPosition",Vector3.zero);
            chain.pull=Read(component,"pull",.2f); chain.spring=Read(component,"spring",.5f);
            chain.stiffness=Read(component,"stiffness",.1f); chain.immobile=Read(component,"immobile",0f);
            chain.gravity=Read(component,"gravity",0f); chain.radius=Read(component,"radius",.01f);
            chain.radiusCurve=Read(component,"radiusCurve",AnimationCurve.Linear(0,1,1,1));
            if (chain.radiusCurve == null || chain.radiusCurve.length == 0) chain.radiusCurve=AnimationCurve.Linear(0,1,1,1);
            int limitType=Convert.ToInt32(ReadObject(component,"limitType") ?? 0);
            chain.maxAngle=limitType==0 ? 180 : Read(component,"maxAngleX",180f);
            chain.directionalLimit=limitType+1; chain.maxYaw=Read(component,"maxAngleZ",180f); chain.limitRotation=Read(component,"limitRotation",Vector3.zero);
            chain.angleCurve=ReadObject(component,"maxAngleXCurve") as AnimationCurve; chain.yawCurve=ReadObject(component,"maxAngleZCurve") as AnimationCurve;
            chain.rotationXCurve=ReadObject(component,"limitRotationXCurve") as AnimationCurve; chain.rotationYCurve=ReadObject(component,"limitRotationYCurve") as AnimationCurve; chain.rotationZCurve=ReadObject(component,"limitRotationZCurve") as AnimationCurve;
            var allow=ReadObject(component,"allowCollision"); chain.allowHandCollision=allow==null || allow.ToString()!="False";
            chain.ignored=Objects(component,"ignoreTransforms").OfType<Transform>().ToArray();
            chain.colliders=Objects(component,"colliders").OfType<Component>().Where(c=>colliderMap.ContainsKey(c)).Select(c=>colliderMap[c]).ToArray();
            chainCount++;
        }
        var materials=new Dictionary<Material,Material>(); var meshes=new Dictionary<Mesh,Mesh>(); int vertices=0, activeRenderers=0;
        var shader=Shader.Find("AvatarViewer/PortableToon");
        if (shader == null) throw new Exception("PortableToon shader missing");
        foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            // Detach meshes from FBX importer dependencies (which can pull in editor animation/controller assets).
            var sourceMesh=renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if(sourceMesh != null) {
                if(!meshes.TryGetValue(sourceMesh,out var cleanMesh)) {
                    cleanMesh=Object.Instantiate(sourceMesh); cleanMesh.name=sourceMesh.name;
                    AssetDatabase.CreateAsset(cleanMesh,Generated+"/mesh-"+meshes.Count+".asset"); meshes.Add(sourceMesh,cleanMesh);
                }
                if(renderer is SkinnedMeshRenderer skinned) skinned.sharedMesh=cleanMesh;
                else renderer.GetComponent<MeshFilter>().sharedMesh=cleanMesh;
            }
            renderer.sharedMaterials=renderer.sharedMaterials.Select(material => {
                if (material == null) return null;
                if (materials.TryGetValue(material,out var cached)) return cached;
                var portable=AvatarMaterialExporter.Convert(material);
                AssetDatabase.CreateAsset(portable,Generated+"/material-"+materials.Count+".mat");
                materials.Add(material,portable); return portable;
            }).ToArray();
            if (renderer.gameObject.activeInHierarchy && renderer.enabled) {
                activeRenderers++;
                var mesh=renderer is SkinnedMeshRenderer sk ? sk.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh != null) vertices+=mesh.vertexCount;
            }
        }
        // Preserve the humanoid mapping before stripping Animator and VRC scripts.
        var rigNames=new List<string>(); var rigBones=new List<Transform>();
        var animator=avatar.GetComponentsInChildren<Animator>(true).FirstOrDefault(a=>a.isHuman && a.avatar!=null && a.avatar.isValid);
        if(animator!=null) foreach(HumanBodyBones id in Enum.GetValues(typeof(HumanBodyBones))) {
            if(id==HumanBodyBones.LastBone) continue;
            var bone=animator.GetBoneTransform(id);
            if(bone!=null && bone.IsChildOf(avatar.transform)) { rigNames.Add(id.ToString()); rigBones.Add(bone); }
        }
        int constraintCount=AvatarConstraintExporter.Capture(avatar,warnings);
        var expressionMenu=AvatarMenuExporter.Capture(avatar,materials,warnings);
        // Strict allowlist: no SDK scripts, contacts, audio, cameras or arbitrary plugin behaviours are shipped.
        // Remove dependent scripts before native components they require (e.g.
        // spatial-audio scripts require AudioSource). Otherwise Unity refuses the
        // first removal and leaves an unintended native component behind.
        for(int pass=0;pass<2;pass++) foreach (var component in avatar.GetComponentsInChildren<Component>(true).OrderBy(c=>c is MonoBehaviour?0:1))
        {
            if (component == null) continue;
            if (component is Transform || component is MeshFilter || component is MeshRenderer || component is SkinnedMeshRenderer ||
                component is SpringChain || component is SpringCollider || component is AvatarConstraint || component is AvatarConstraintSolver || component is AvatarMenuData || (expressionMenu!=null && component==expressionMenu.effects)) continue;
            Object.DestroyImmediate(component);
        }
        foreach(var t in avatar.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        var info=avatar.AddComponent<AvatarPackageInfo>(); info.displayName=avatar.name; info.unityVersion=Application.unityVersion;
        info.buildPlatform="Android"; info.convertedChains=chainCount; info.convertedColliders=colliderMap.Count;
        info.humanoidBoneNames=rigNames.ToArray(); info.humanoidBones=rigBones.ToArray();
        info.originalMaterials=true;
        AvatarHeadMask.Bake(avatar);
        PrefabUtility.SaveAsPrefabAsset(avatar,Generated+"/avatar.prefab");
        Object.DestroyImmediate(avatar); AssetDatabase.SaveAssets();
        Directory.CreateDirectory(output);
        // Build in the clean viewer project: source SDK assemblies such as Harmony cannot be player dependencies.
        int copiedAssets=0;
        // Unity does not consistently enumerate nested shader include files as
        // prefab dependencies. Ship the complete isolated shader source graph.
        var dependencies=AssetDatabase.GetDependencies(Generated+"/avatar.prefab",true)
            .Concat(Directory.GetFiles(Generated+"/OriginalShaders","*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".meta")).Select(p=>p.Replace('\\','/'))).Distinct().ToArray();
        File.WriteAllLines(Path.Combine(output,"dependencies.txt"),dependencies);
        foreach(var dependency in dependencies)
        {
            if(dependency.StartsWith("Assets/AvatarViewerExport/Runtime/")) continue; // identical shared script GUIDs already in App
            if(!dependency.StartsWith("Assets/") && !dependency.StartsWith("Packages/")) throw new Exception("Unexpected dependency: "+dependency);
            string extension=Path.GetExtension(dependency).ToLowerInvariant();
            if(extension==".cs" || extension==".dll" || extension==".asmdef") throw new Exception("Unexpected script dependency: "+dependency);
            string destination=dependency.StartsWith("Packages/") ? "Assets/AvatarViewerExport/External/"+dependency.Substring(9):dependency;
            string target=Path.Combine(viewer,destination); Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(dependency,target,true); if(File.Exists(dependency+".meta")) File.Copy(dependency+".meta",target+".meta",true);
            copiedAssets++;
        }
        warnings.Add("Original editable shaders and material properties are retained when they compile for the viewer. World lighting and platform-specific effects can differ; unsupported shader sources use a basic fallback. SPS contacts are not implemented.");
        warnings.Add("Secondary motion approximates PhysBones; grabbing, stretching, parameter outputs and complex limits are not implemented.");
        warnings.Add("Expression menu and supported FX animations are exported. Contacts, audio, arbitrary scripts and unsupported platform behaviours are omitted.");
        File.WriteAllText(Path.Combine(output,"conversion-report.json"),JsonUtility.ToJson(new ConversionReport {
            platform="Android",unityVersion=Application.unityVersion, sourceScene=scenePath, ndmfBaked=bake!=null,
            convertedChains=chainCount, convertedColliders=colliderMap.Count, materials=materials.Count,
            activeRenderers=activeRenderers,activeVertices=vertices,copiedAssets=copiedAssets,warnings=warnings.ToArray()
        },true));
        Debug.Log("AVATAR_EXPORT_COMPLETE chains="+chainCount+" colliders="+colliderMap.Count+" vertices="+vertices);
        } finally { if(avatar!=null) Object.DestroyImmediate(avatar); }
    }
    [Serializable] sealed class ConversionReport { public string platform,unityVersion,sourceScene; public bool ndmfBaked; public int convertedChains,convertedColliders,materials,activeRenderers,activeVertices,copiedAssets; public string[] warnings; }
    static object ReadObject(object obj,string name) {
        var type=obj.GetType();
        return type.GetField(name,BindingFlags.Public|BindingFlags.Instance)?.GetValue(obj)
            ?? type.GetProperty(name,BindingFlags.Public|BindingFlags.Instance)?.GetValue(obj);
    }
    static T Read<T>(object obj,string name,T fallback) { var value=ReadObject(obj,name); return value is T typed ? typed : fallback; }
    static IEnumerable<object> Objects(object obj,string name) { var list=ReadObject(obj,name) as IEnumerable; if(list != null) foreach(var item in list) yield return item; }
    static string Argument(string key,string fallback) { var args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,key); return i>=0 && i+1<args.Length ? args[i+1]:fallback; }
    static Transform FindOrCopyBone(Transform bone,Transform sourceRoot,Transform targetRoot) {
        if(bone==sourceRoot) return targetRoot;
        if(bone.parent==null || !bone.IsChildOf(sourceRoot)) throw new Exception("Body bone outside avatar: "+bone.name);
        var parent=FindOrCopyBone(bone.parent,sourceRoot,targetRoot); var target=parent.Find(bone.name);
        if(target==null) {
            target=new GameObject(bone.name).transform; target.SetParent(parent,false);
            target.localPosition=bone.localPosition; target.localRotation=bone.localRotation; target.localScale=bone.localScale;
        }
        return target;
    }
}
