#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarViewer
{
    public static class AvatarFxDefaults
    {
        // An empty Write Defaults state is not sufficient to restore object visibility
        // in the standalone animator. Give it an explicit clip of the model's defaults.
        public static int Bake(GameObject avatar,AnimatorController controller) {
            int count=0;
            foreach(var layer in controller.layers) {
                var states=States(layer.stateMachine).ToArray();
                var empty=states.Where(s=>s.writeDefaultValues && Empty(s.motion)).ToArray();
                if(empty.Length==0) continue;
                var clips=states.SelectMany(s=>Clips(s.motion)).Distinct().ToArray();
                var defaults=new AnimationClip { name="Portable defaults: "+layer.name };
                foreach(var binding in clips.SelectMany(AnimationUtility.GetCurveBindings).Distinct()) {
                    if(binding.type==typeof(Animator)) continue;
                    if(AnimationUtility.GetFloatValue(avatar,binding,out float value)) AnimationUtility.SetEditorCurve(defaults,binding,AnimationCurve.Constant(0,1f/60,value));
                }
                foreach(var binding in clips.SelectMany(AnimationUtility.GetObjectReferenceCurveBindings).Distinct())
                    if(AnimationUtility.GetObjectReferenceValue(avatar,binding,out var value)) AnimationUtility.SetObjectReferenceCurve(defaults,binding,new[]{new ObjectReferenceKeyframe { time=0,value=value }});
                if(Empty(defaults)) { Object.DestroyImmediate(defaults); continue; }
                AssetDatabase.AddObjectToAsset(defaults,controller);
                foreach(var state in empty) { state.motion=defaults; EditorUtility.SetDirty(state); count++; }
            }
            EditorUtility.SetDirty(controller); return count;
        }
        static bool Empty(Motion motion) => motion==null || (motion is AnimationClip clip && AnimationUtility.GetCurveBindings(clip).Length==0 && AnimationUtility.GetObjectReferenceCurveBindings(clip).Length==0);
        static IEnumerable<AnimatorState> States(AnimatorStateMachine machine) { foreach(var s in machine.states) yield return s.state; foreach(var child in machine.stateMachines) foreach(var s in States(child.stateMachine)) yield return s; }
        static IEnumerable<AnimationClip> Clips(Motion motion) { if(motion is AnimationClip clip) yield return clip; else if(motion is BlendTree tree) foreach(var child in tree.children) foreach(var c in Clips(child.motion)) yield return c; }
    }
}
#endif
