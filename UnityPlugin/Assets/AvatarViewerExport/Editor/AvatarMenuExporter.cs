using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AvatarViewer;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object=UnityEngine.Object;

public static class AvatarMenuExporter
{
    const string Folder="Assets/AvatarViewerExport/Generated";
    static object Read(object obj,string field) {
        if(obj==null) return null;
        var t=obj.GetType(); return t.GetField(field,BindingFlags.Public|BindingFlags.Instance)?.GetValue(obj)
            ??t.GetProperty(field,BindingFlags.Public|BindingFlags.Instance)?.GetValue(obj);
    }
    static IEnumerable<object> Items(object value) { if(value is IEnumerable e) foreach(var x in e) yield return x; }
    static float Number(object v) { try { return v==null?0:Convert.ToSingle(v); } catch { return 0; } }
    public static AvatarMenuData Capture(GameObject avatar,Dictionary<Material,Material> materials,List<string> warnings) {
        var descriptor=avatar.GetComponentsInChildren<Component>(true).FirstOrDefault(c=>c!=null && c.GetType().Name=="VRCAvatarDescriptor");
        object root=Read(descriptor,"expressionsMenu");
        if(root==null) return null;
        var data=avatar.AddComponent<AvatarMenuData>();
        var pages=new List<AvatarMenuData.Page>(); var indexes=new Dictionary<object,int>();
        var icons=new Dictionary<Texture2D,Texture2D>();
        Func<Texture2D,Texture2D> copyIcon=icon=>{
            if(icon==null) return null;
            if(icons.TryGetValue(icon,out var existing)) return existing;
            var clone=Object.Instantiate(icon); AssetDatabase.CreateAsset(clone,Folder+"/menu-icon-"+icons.Count+".asset"); icons[icon]=clone; return clone;
        };
        Func<object,int> visit=null;
        visit=menu=>{
            if(menu==null) return -1;
            if(indexes.TryGetValue(menu,out int existing)) return existing;
            int id=pages.Count; indexes[menu]=id;
            var page=new AvatarMenuData.Page { name=(menu as Object)?.name??"菜单" }; pages.Add(page);
            page.controls=Items(Read(menu,"controls")).Select(c=>new AvatarMenuData.Control {
                name=Read(c,"name") as string??"选项",type=Read(c,"type")?.ToString()??"Unknown",
                parameter=Read(Read(c,"parameter"),"name") as string,value=Number(Read(c,"value")),
                submenu=visit(Read(c,"subMenu")),icon=copyIcon(Read(c,"icon") as Texture2D),
                subParameters=Items(Read(c,"subParameters")).Select(p=>Read(p,"name") as string??"").ToArray()
            }).ToArray();
            return id;
        };
        visit(root); data.pages=pages.ToArray();
        data.parameters=Items(Read(Read(descriptor,"expressionParameters"),"parameters")).Select(p=>new AvatarMenuData.Parameter {
            name=Read(p,"name") as string,type=Read(p,"valueType")?.ToString(),value=Number(Read(p,"defaultValue")),saved=Read(p,"saved") is bool saved && saved
        }).Where(p=>!string.IsNullOrEmpty(p.name)).ToArray();
        AnimatorController controller=null;
        foreach(var layer in Items(Read(descriptor,"baseAnimationLayers"))) if(Read(layer,"type")?.ToString()=="FX") controller=Read(layer,"animatorController") as AnimatorController;
        if(controller!=null) {
            string destination=Folder+"/portable-fx.controller";
            if(AssetDatabase.LoadAssetAtPath<Object>(destination)!=null) AssetDatabase.DeleteAsset(destination);
            controller=CopyControllerGraph(controller,destination);
            var motions=new Dictionary<Motion,Motion>(); int removed=0,clips=0,drivers=0;
            var references=new Dictionary<Object,Object>();
            Func<Object,Object> cleanReference=value=>{
                if(value==null) return null;
                if(references.TryGetValue(value,out var existing)) return existing;
                if(value is Material material) {
                    if(materials.TryGetValue(material,out var portable)) return portable;
                    portable=AvatarMaterialExporter.Convert(material);
                    AssetDatabase.CreateAsset(portable,Folder+"/fx-material-"+materials.Count+".mat"); materials[material]=portable; return portable;
                }
                if(value is Texture2D || value is Mesh) {
                    var clone=Object.Instantiate(value); AssetDatabase.CreateAsset(clone,Folder+"/fx-reference-"+references.Count+".asset"); references[value]=clone; return clone;
                }
                return null;
            };
            Func<Motion,Motion> cleanMotion=null;
            cleanMotion=motion=>{
                if(motion==null) return null;
                if(motions.TryGetValue(motion,out var found)) return found;
                if(motion is AnimationClip original) {
                    var clip=new AnimationClip { name=original.name+"_Portable",frameRate=original.frameRate,wrapMode=original.wrapMode };
                    AnimationUtility.SetAnimationClipSettings(clip,AnimationUtility.GetAnimationClipSettings(original));
                    motions[motion]=clip; motions[clip]=clip;
                    foreach(var b in AnimationUtility.GetCurveBindings(original)) {
                        var mapped=b; AvatarConstraintExporter.Remap(ref mapped);
                        if(Supported(mapped.type)) AnimationUtility.SetEditorCurve(clip,mapped,AnimationUtility.GetEditorCurve(original,b));
                    }
                    foreach(var b in AnimationUtility.GetObjectReferenceCurveBindings(original)) {
                        if(!Supported(b.type)) continue;
                        var frames=AnimationUtility.GetObjectReferenceCurve(original,b);
                        for(int i=0;i<frames.Length;i++) frames[i].value=cleanReference(frames[i].value);
                        AnimationUtility.SetObjectReferenceCurve(clip,b,frames);
                    }
                    AnimationUtility.SetAnimationEvents(clip,new AnimationEvent[0]);
                    AssetDatabase.CreateAsset(clip,Folder+"/fx-clip-"+(clips++)+".anim"); return clip;
                }
                if(motion is BlendTree originalTree) {
                    var tree=originalTree; motions[motion]=tree;
                    var children=tree.children; for(int i=0;i<children.Length;i++) children[i].motion=cleanMotion(children[i].motion); tree.children=children;
                    return tree;
                }
                return null;
            };
            Action<AnimatorStateMachine> cleanStates=null;
            cleanStates=machine=>{
                if(AssetDatabase.GetAssetPath(machine)!=destination) throw new Exception("FX copy references a source state machine; original assets were not changed");
                foreach(var behaviour in machine.behaviours) { Object.DestroyImmediate(behaviour,true); removed++; }
                foreach(var child in machine.states) {
                    if(AssetDatabase.GetAssetPath(child.state)!=destination) throw new Exception("FX copy references a source state");
                    foreach(var behaviour in child.state.behaviours) {
                        AvatarFxDriver driver=null;
                        if(behaviour!=null && behaviour.GetType().Name=="VRCAvatarParameterDriver") {
                            driver=child.state.AddStateMachineBehaviour<AvatarFxDriver>();
                            driver.changes=Items(Read(behaviour,"parameters")).Select(p=>new AvatarFxDriver.Change {
                                name=Read(p,"name") as string,source=Read(p,"source") as string,type=Read(p,"type")?.ToString(),value=Number(Read(p,"value")),
                                min=Number(Read(p,"valueMin")),max=Number(Read(p,"valueMax")),chance=Number(Read(p,"chance")),
                                sourceMin=Number(Read(p,"sourceMin")),sourceMax=Number(Read(p,"sourceMax")),destMin=Number(Read(p,"destMin")),destMax=Number(Read(p,"destMax")),convertRange=Read(p,"convertRange") is bool convert && convert
                            }).ToArray();
                        } else if(behaviour!=null && behaviour.GetType().Name=="VRCAnimatorLayerControl" && Read(behaviour,"playable")?.ToString()=="FX") {
                            driver=child.state.AddStateMachineBehaviour<AvatarFxDriver>(); driver.layer=(int)Number(Read(behaviour,"layer")); driver.layerWeight=Number(Read(behaviour,"goalWeight"));
                        }
                        if(driver!=null) drivers++; else removed++;
                        Object.DestroyImmediate(behaviour,true);
                    }
                    child.state.motion=cleanMotion(child.state.motion);
                }
                foreach(var child in machine.stateMachines) cleanStates(child.stateMachine);
            };
            foreach(var layer in controller.layers) cleanStates(layer.stateMachine);
            var masks=new Dictionary<AvatarMask,AvatarMask>();
            foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(destination)) {
                if(asset==null) continue;
                if(asset is StateMachineBehaviour && !(asset is AvatarFxDriver)) { Object.DestroyImmediate(asset,true); continue; }
                var serialized=new SerializedObject(asset); var property=serialized.GetIterator();
                while(property.Next(true)) {
                    if(property.propertyType!=SerializedPropertyType.ObjectReference) continue;
                    if(property.objectReferenceValue is AnimationClip clip) property.objectReferenceValue=cleanMotion(clip);
                    else if(property.objectReferenceValue is AvatarMask mask) {
                        if(!masks.TryGetValue(mask,out var clean)) { clean=Object.Instantiate(mask); AssetDatabase.AddObjectToAsset(clean,controller); masks[mask]=clean; }
                        property.objectReferenceValue=clean;
                    }
                }
                serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset);
            }
            EditorUtility.SetDirty(controller);
            int defaultStates=AvatarFxDefaults.Bake(avatar,controller);
            warnings.Add("Explicit default animation states: "+defaultStates);
            // Attach after the exporter's script stripping pass.
            data.compatibility="FX 动画已转换；平台脚本行为不执行（"+removed+" 个）。";
            warnings.Add("Expression menus: "+pages.Count+" pages; sanitized FX clips: "+clips+"; converted parameter/layer drivers: "+drivers+"; removed platform state behaviours: "+removed+". Contacts and unsupported platform behaviours are not executed. Layer-weight changes apply immediately.");
            var animator=avatar.GetComponent<Animator>();
            if(animator==null) animator=avatar.AddComponent<Animator>();
            var generic=AvatarBuilder.BuildGenericAvatar(avatar,""); generic.name="Portable model FX binding"; AssetDatabase.CreateAsset(generic,Folder+"/portable-fx-avatar.asset");
            animator.avatar=generic; animator.runtimeAnimatorController=controller; animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            data.effects=animator;
        } else { data.compatibility="没有可转换的 FX AnimatorController；菜单参数可能没有视觉效果。"; warnings.Add(data.compatibility); }
        Debug.Log("PICO_MENU_EXPORTED pages="+pages.Count+" controls="+pages.Sum(p=>p.controls.Length)+" fx="+(controller!=null));
        return data;
    }
    static bool Supported(Type type) => type==typeof(GameObject) || type==typeof(Transform) || type==typeof(SkinnedMeshRenderer) || type==typeof(MeshRenderer) || type==typeof(Renderer) || type==typeof(Animator) || typeof(AvatarConstraint).IsAssignableFrom(type);
    public static void AuditDependencies() {
        var paths=AssetDatabase.GetDependencies(Folder+"/avatar.prefab",true);
        var audio=paths.Where(p=>p.EndsWith(".wav",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".mp3",StringComparison.OrdinalIgnoreCase)).ToArray();
        var result=new List<string>();
        foreach(var path in paths) foreach(var dep in AssetDatabase.GetDependencies(path,false)) if(audio.Contains(dep) && path!=dep) result.Add(path+" -> "+dep);
        System.IO.File.WriteAllLines(System.IO.Path.GetFullPath("../menu-dependency-audit.txt"),result);
    }
    public static void FinalizeExport() {
        string path=Folder+"/avatar.prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try {
            foreach(var audio in root.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(audio);
            PrefabUtility.SaveAsPrefabAsset(root,path);
        } finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); AuditDependencies();
        var dependencies=AssetDatabase.GetDependencies(path,true);
        string target=System.IO.Path.GetFullPath("../App");
        foreach(var dependency in dependencies) {
            if(dependency.StartsWith("Assets/AvatarViewerExport/Runtime/")) continue;
            if(!dependency.StartsWith("Assets/")) throw new Exception("Unsupported dependency "+dependency);
            var extension=System.IO.Path.GetExtension(dependency).ToLowerInvariant();
            if(extension==".cs" || extension==".dll" || extension==".asmdef") throw new Exception("Unexpected script "+dependency);
            string destination=System.IO.Path.Combine(target,dependency); System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
            System.IO.File.Copy(dependency,destination,true); if(System.IO.File.Exists(dependency+".meta")) System.IO.File.Copy(dependency+".meta",destination+".meta",true);
        }
        System.IO.File.WriteAllLines(System.IO.Path.GetFullPath("../Exports/dependencies.txt"),dependencies);
        PicoAvatarUploader.ExportPlugin();
        Debug.Log("PICO_FINAL_EXPORT_CLEAN");
    }
    static AnimatorController CopyControllerGraph(AnimatorController source,string destination) {
        // NDMF may store the controller and its state machines in different temporary
        // assets. Copy the referenced graph and rewrite every internal object reference.
        var copies=new Dictionary<Object,Object>(); AnimatorController result=null;
        Action<Object> copy=null;
        copy=obj=>{
            if(obj==null || copies.ContainsKey(obj)) return;
            bool graph=obj is AnimatorController || obj is AnimatorStateMachine || obj is AnimatorState || obj is AnimatorTransitionBase || obj is BlendTree || obj is StateMachineBehaviour;
            if(!graph) return;
            var clone=Object.Instantiate(obj); clone.name=obj.name; clone.hideFlags=HideFlags.None; copies[obj]=clone;
            if(obj==source) { result=(AnimatorController)clone; AssetDatabase.CreateAsset(clone,destination); }
            else AssetDatabase.AddObjectToAsset(clone,result);
            var serialized=new SerializedObject(obj); var property=serialized.GetIterator();
            while(property.Next(true)) if(property.propertyType==SerializedPropertyType.ObjectReference) copy(property.objectReferenceValue);
        };
        copy(source);
        foreach(var item in copies) {
            var serialized=new SerializedObject(item.Value); var property=serialized.GetIterator();
            while(property.Next(true)) if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceValue!=null && copies.TryGetValue(property.objectReferenceValue,out var mapped)) property.objectReferenceValue=mapped;
            serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(item.Value);
        }
        return result;
    }
}
