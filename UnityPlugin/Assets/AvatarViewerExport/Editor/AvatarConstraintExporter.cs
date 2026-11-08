using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AvatarViewer;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

public static class AvatarConstraintExporter {
    static readonly Dictionary<string,AvatarConstraint> converted=new Dictionary<string,AvatarConstraint>();
    static object Read(object obj,string name) => obj==null?null:obj.GetType().GetField(name,BindingFlags.Public|BindingFlags.Instance)?.GetValue(obj)??obj.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance)?.GetValue(obj);
    static T Value<T>(object obj,string name,T fallback) { var value=Read(obj,name); return value is T v?v:fallback; }
    static IEnumerable<object> Items(object obj) { if(obj is IEnumerable list) foreach(var item in list) yield return item; }
    static Type PortableType(Type type) {
        switch(type.Name.Replace("VRC","")) {
            case "ParentConstraint":return typeof(PortableParentConstraint);
            case "PositionConstraint":return typeof(PortablePositionConstraint);
            case "RotationConstraint":return typeof(PortableRotationConstraint);
            case "ScaleConstraint":return typeof(PortableScaleConstraint);
            case "AimConstraint":return typeof(PortableAimConstraint);
            default:return null;
        }
    }
    static string Key(Type type,string path) => type.FullName+"|"+path;
    public static int Capture(GameObject avatar,List<string> warnings) {
        converted.Clear(); int count=0;
        foreach(var component in avatar.GetComponentsInChildren<Component>(true).Where(c=>c!=null && PortableType(c.GetType())!=null).ToArray()) {
            var portable=(AvatarConstraint)component.gameObject.AddComponent(PortableType(component.GetType()));
            portable.enabled=(component as Behaviour)?.enabled??true;
            if(component is IConstraint native) {
                portable.IsActive=native.constraintActive; portable.GlobalWeight=native.weight;
                portable.Sources=new AvatarConstraint.SourceList(Enumerable.Range(0,native.sourceCount).Select(i=>new AvatarConstraint.Source { SourceTransform=native.GetSource(i).sourceTransform,Weight=native.GetSource(i).weight }).ToArray());
                portable.PositionAtRest=Value(component,"translationAtRest",component.transform.localPosition); portable.RotationAtRest=Value(component,"rotationAtRest",component.transform.localEulerAngles); portable.ScaleAtRest=Value(component,"scaleAtRest",component.transform.localScale);
                portable.PositionOffset=Value(component,"translationOffset",Vector3.zero); portable.RotationOffset=Value(component,"rotationOffset",Vector3.zero); portable.ScaleOffset=Value(component,"scaleOffset",Vector3.one);
                var position=Value(component,"translationAxis",Axis.X|Axis.Y|Axis.Z); var rotation=Value(component,"rotationAxis",Axis.X|Axis.Y|Axis.Z); var scale=Value(component,"scalingAxis",Axis.X|Axis.Y|Axis.Z);
                portable.AffectsPositionX=(position&Axis.X)!=0; portable.AffectsPositionY=(position&Axis.Y)!=0; portable.AffectsPositionZ=(position&Axis.Z)!=0;
                portable.AffectsRotationX=(rotation&Axis.X)!=0; portable.AffectsRotationY=(rotation&Axis.Y)!=0; portable.AffectsRotationZ=(rotation&Axis.Z)!=0;
                portable.AffectsScaleX=(scale&Axis.X)!=0; portable.AffectsScaleY=(scale&Axis.Y)!=0; portable.AffectsScaleZ=(scale&Axis.Z)!=0;
                if(component is ParentConstraint parent) for(int i=0;i<portable.Sources.Length;i++) { var s=portable.Sources[i]; s.ParentPositionOffset=parent.GetTranslationOffset(i); s.ParentRotationOffset=parent.GetRotationOffset(i); portable.Sources[i]=s; }
                if(component is AimConstraint aim) { portable.AimAxis=aim.aimVector; portable.UpAxis=aim.upVector; portable.WorldUp=aim.worldUpType.ToString(); portable.WorldUpVector=aim.worldUpVector; portable.WorldUpTransform=aim.worldUpObject; }
            } else {
                // Read only the SDK's public constraint API, never native implementation details.
                foreach(var field in typeof(AvatarConstraint).GetFields(BindingFlags.Public|BindingFlags.Instance)) {
                    if(field.Name=="Sources" || field.Name=="WorldUp") continue;
                    var value=Read(component,field.Name); if(value!=null && field.FieldType.IsInstanceOfType(value)) field.SetValue(portable,value);
                }
                portable.WorldUp=Read(component,"WorldUp")?.ToString()??"SceneUp";
                portable.Sources=new AvatarConstraint.SourceList(Items(Read(component,"Sources")).Select(s=>new AvatarConstraint.Source { SourceTransform=Read(s,"SourceTransform") as Transform,Weight=Value(s,"Weight",0f),ParentPositionOffset=Value(s,"ParentPositionOffset",Vector3.zero),ParentRotationOffset=Value(s,"ParentRotationOffset",Vector3.zero) }).ToArray());
            }
            for(int i=0;i<portable.Sources.Length;i++) { var source=portable.Sources[i]; if(source.SourceTransform!=null && !source.SourceTransform.IsChildOf(avatar.transform)) { warnings.Add("External constraint source omitted: "+component.name); source.SourceTransform=null; portable.Sources[i]=source; } }
            converted[Key(component.GetType(),AnimationUtility.CalculateTransformPath(component.transform,avatar.transform))]=portable; count++;
        }
        if(count>0) avatar.AddComponent<AvatarConstraintSolver>();
        warnings.Add("Standalone constraints converted: "+count+"; parent/position/rotation/scale/aim, animated weights and world freeze are evaluated after tracked IK. Complex cyclic constraints may differ.");
        return count;
    }
    public static bool Remap(ref EditorCurveBinding binding) {
        if(!converted.TryGetValue(Key(binding.type,binding.path),out var component)) return false;
        string property=binding.propertyName;
        property=Regex.Replace(property,@"m_TranslationOffsets\.Array\.data\[(\d+)\]","Sources.source$1.ParentPositionOffset");
        property=Regex.Replace(property,@"m_RotationOffsets\.Array\.data\[(\d+)\]","Sources.source$1.ParentRotationOffset");
        property=property.Replace("m_Sources.","Sources.").Replace(".sourceTransform",".SourceTransform").Replace(".weight",".Weight");
        property=Regex.Replace(property,@"Sources\.Array\.data\[(\d+)\]\.","Sources.source$1.");
        foreach(var pair in new[]{new[]{"m_Weight","GlobalWeight"},new[]{"m_Active","IsActive"},new[]{"m_TranslationAtRest","PositionAtRest"},new[]{"m_RotationAtRest","RotationAtRest"},new[]{"m_ScaleAtRest","ScaleAtRest"},new[]{"m_TranslationOffset","PositionOffset"},new[]{"m_RotationOffset","RotationOffset"},new[]{"m_ScaleOffset","ScaleOffset"},new[]{"m_AffectTranslation","AffectsPosition"},new[]{"m_AffectRotation","AffectsRotation"},new[]{"m_AffectScaling","AffectsScale"}}) property=property.Replace(pair[0],pair[1]);
        string field=property.Split('.')[0];
        if(field!="m_Enabled" && typeof(AvatarConstraint).GetField(field)==null) return false;
        binding.type=component.GetType(); binding.propertyName=property; return true;
    }
}
