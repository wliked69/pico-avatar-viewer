using System;
using System.Collections.Generic;
using UnityEngine;

// Feeds the avatar's existing local Unified Expressions inputs. This does not load
// desktop VRCFT modules, emulate OSC, or write FTF's generated/smoothed outputs.
public sealed class VrcFaceTrackingBridge
{
    public sealed class Frame
    {
        public float[] weights;
        public bool enabled,upper,lower,gaze,leftOpenValid,rightOpenValid;
        public float gazeX,gazeY,leftOpen=1,rightOpen=1;
        float W(int index) => weights!=null && index<weights.Length ? Safe(weights[index]) : 0;
        static float Safe(float value) => float.IsNaN(value)||float.IsInfinity(value)?0:Mathf.Clamp01(value);
        // Facial blink coefficients are continuous; some legacy openness streams
        // report only open/closed. Use openness only if facial data is unavailable.
        float Lid(bool left) => upper ? (1-W(left?5:12))*.75f+W(left?11:18)*.25f
            : Safe(left?leftOpen:rightOpen)*.75f;
        public bool TryRead(string key,out float value,out bool valid)
        {
            value=0; valid=false;
            bool isEye=key.StartsWith("Eye",StringComparison.Ordinal);
            bool isBrow=key.StartsWith("Brow",StringComparison.Ordinal);
            valid=enabled && (isEye||isBrow?upper:lower);
            switch(key) {
                case "EyeLeftX": value=gaze?gazeX:W(7)-W(8); valid=enabled&&(gaze||upper); break;
                case "EyeRightX": value=gaze?gazeX:W(15)-W(14); valid=enabled&&(gaze||upper); break;
                case "EyeLeftY": value=gaze?gazeY:W(9)-W(6); valid=enabled&&(gaze||upper); break;
                case "EyeRightY": value=gaze?gazeY:W(16)-W(13); valid=enabled&&(gaze||upper); break;
                case "EyeX": value=gaze?gazeX:(W(7)-W(8)+W(15)-W(14))*.5f; valid=enabled&&(gaze||upper); break;
                case "EyeY": value=gaze?gazeY:(W(9)-W(6)+W(16)-W(13))*.5f; valid=enabled&&(gaze||upper); break;
                case "EyeLidLeft": value=Lid(true); valid=enabled&&(upper||leftOpenValid); break;
                case "EyeLidRight": value=Lid(false); valid=enabled&&(upper||rightOpenValid); break;
                case "EyeLid": value=(Lid(true)+Lid(false))*.5f; valid=enabled&&(upper||(leftOpenValid&&rightOpenValid)); break;
                case "EyeSquintLeft": value=W(10); break;
                case "EyeSquintRight": value=W(17); break;
                case "EyeSquint": value=(W(10)+W(17))*.5f; break;
                case "EyeWideLeft": value=W(11); break;
                case "EyeWideRight": value=W(18); break;
                case "BrowInnerUpLeft": case "BrowInnerUpRight": case "BrowInnerUp": value=W(2); break;
                case "BrowOuterUpLeft": value=W(3); break;
                case "BrowOuterUpRight": value=W(4); break;
                case "BrowOuterUp": value=(W(3)+W(4))*.5f; break;
                case "BrowUp": value=(W(2)+(W(3)+W(4))*.5f)*.5f; break;
                case "BrowDownLeft": case "BrowLowererLeft": case "BrowPinchLeft": value=W(0); break;
                case "BrowDownRight": case "BrowLowererRight": case "BrowPinchRight": value=W(1); break;
                case "BrowLowerer": case "BrowDown": value=(W(0)+W(1))*.5f; break;
                case "BrowExpressionLeft": value=(W(2)+W(3))*.5f-W(0); break;
                case "BrowExpressionRight": value=(W(2)+W(4))*.5f-W(1); break;
                case "BrowExpression": value=(W(2)+(W(3)+W(4))*.5f)*.5f-(W(0)+W(1))*.5f; break;
                case "JawOpen": value=W(50); break;
                case "JawForward": case "JawZ": value=W(47); break;
                case "JawX": value=W(49)-W(48); break;
                case "JawLeft": value=W(48); break;
                case "JawRight": value=W(49); break;
                case "MouthClosed": value=W(24); break;
                case "MouthX": case "MouthUpperX": case "MouthLowerX": value=W(28)-W(27); break;
                case "MouthUpperUpLeft": value=W(45); break;
                case "MouthUpperUpRight": value=W(46); break;
                case "MouthUpperUp": value=(W(45)+W(46))*.5f; break;
                case "MouthLowerDownLeft": value=W(43); break;
                case "MouthLowerDownRight": value=W(44); break;
                case "MouthLowerDown": value=(W(43)+W(44))*.5f; break;
                case "MouthOpen": value=(W(45)+W(46)+W(43)+W(44))*.25f; break;
                case "MouthSmileLeft": case "MouthCornerPullLeft": case "MouthCornerSlantLeft": value=W(29); break;
                case "MouthSmileRight": case "MouthCornerPullRight": case "MouthCornerSlantRight": value=W(30); break;
                case "MouthSmile": value=(W(29)+W(30))*.5f; break;
                case "MouthFrownLeft": value=W(31); break;
                case "MouthFrownRight": value=W(32); break;
                case "MouthSadLeft": value=(W(31)+W(35))*.5f; break;
                case "MouthSadRight": value=(W(32)+W(36))*.5f; break;
                case "SmileFrownLeft": value=W(29)-W(31); break;
                case "SmileFrownRight": value=W(30)-W(32); break;
                case "SmileFrown": value=(W(29)+W(30)-W(31)-W(32))*.5f; break;
                case "SmileSadLeft": value=W(29)-(W(31)+W(35))*.5f; break;
                case "SmileSadRight": value=W(30)-(W(32)+W(36))*.5f; break;
                case "SmileSad": value=(W(29)+W(30))*.5f-(W(31)+W(32)+W(35)+W(36))*.25f; break;
                case "MouthDimpleLeft": value=W(33); break;
                case "MouthDimpleRight": value=W(34); break;
                case "MouthDimple": value=(W(33)+W(34))*.5f; break;
                case "MouthStretchLeft": value=W(35); break;
                case "MouthStretchRight": value=W(36); break;
                case "MouthStretch": value=(W(35)+W(36))*.5f; break;
                case "MouthRaiserLower": value=W(39); break;
                case "MouthRaiserUpper": value=W(40); break;
                case "MouthPressLeft": value=W(41); break;
                case "MouthPressRight": value=W(42); break;
                case "LipSuckUpper": case "LipSuckUpperLeft": case "LipSuckUpperRight": value=W(38); break;
                case "LipSuckLower": case "LipSuckLowerLeft": case "LipSuckLowerRight": value=W(37); break;
                case "LipSuck": value=(W(37)+W(38))*.5f; break;
                case "LipFunnel": case "LipFunnelUpper": case "LipFunnelLower":
                case "LipFunnelUpperLeft": case "LipFunnelUpperRight": case "LipFunnelLowerLeft": case "LipFunnelLowerRight": value=W(25); break;
                case "LipPucker": case "LipPuckerUpper": case "LipPuckerLower":
                case "LipPuckerUpperLeft": case "LipPuckerUpperRight": case "LipPuckerLowerLeft": case "LipPuckerLowerRight": value=W(26); break;
                case "CheekPuffSuck": case "CheekPuffSuckLeft": case "CheekPuffSuckRight": value=W(21); break;
                case "CheekSquintLeft": value=W(22); break;
                case "CheekSquintRight": value=W(23); break;
                case "CheekSquint": value=(W(22)+W(23))*.5f; break;
                case "NoseSneerLeft": value=W(19); break;
                case "NoseSneerRight": value=W(20); break;
                case "NoseSneer": value=(W(19)+W(20))*.5f; break;
                case "TongueOut": value=W(51); break;
                default: valid=false; return false; // No synthetic pupil/tongue-direction data.
            }
            value=float.IsNaN(value)||float.IsInfinity(value)?0:Mathf.Clamp(value,-1,1);
            return true;
        }
    }
    struct Input { public string key; public int hash; public float original; public AnimatorControllerParameterType type; }
    readonly List<Input> inputs=new List<Input>(),statuses=new List<Input>();
    Animator animator;
    public int Count => inputs.Count;
    public bool OwnsUpper { get; private set; }
    public bool OwnsLower { get; private set; }
    public bool OwnsGaze { get; private set; }
    public void Bind(Animator target)
    {
        Restore(); inputs.Clear(); statuses.Clear(); animator=target;
        OwnsUpper=OwnsLower=OwnsGaze=false;
        if(animator==null || animator.runtimeAnimatorController==null) return;
        var probe=new Frame();
        float probeValue; bool probeValid;
        foreach(var parameter in animator.parameters) {
            if(parameter.name=="EyeTrackingActive" || parameter.name=="ExpressionTrackingActive" || parameter.name=="LipTrackingActive") {
                if(parameter.type!=AnimatorControllerParameterType.Trigger) statuses.Add(Make(parameter,parameter.name));
                continue;
            }
            string key=InputKey(parameter.name);
            if(key==null || parameter.type!=AnimatorControllerParameterType.Float || !probe.TryRead(key,out probeValue,out probeValid)) continue;
            inputs.Add(Make(parameter,key));
            if(key.StartsWith("Eye",StringComparison.Ordinal)||key.StartsWith("Brow",StringComparison.Ordinal)) OwnsUpper=true; else OwnsLower=true;
            if(key=="EyeLeftX" || key=="EyeRightX" || key=="EyeLeftY" || key=="EyeRightY" || key=="EyeX" || key=="EyeY") OwnsGaze=true;
        }
    }
    static Input Make(AnimatorControllerParameter p,string key) => new Input { key=key,hash=p.nameHash,type=p.type,original=p.type==AnimatorControllerParameterType.Bool?(p.defaultBool?1:0):p.type==AnimatorControllerParameterType.Int?p.defaultInt:p.defaultFloat };
    public static string InputKey(string parameter)
    {
        if(string.IsNullOrEmpty(parameter) || parameter.StartsWith("Drive/",StringComparison.Ordinal) || parameter.StartsWith("Generated/",StringComparison.Ordinal)) return null;
        int marker=parameter.LastIndexOf("/v2/",StringComparison.Ordinal);
        if(marker>=0) return parameter.Substring(marker+4);
        return parameter.StartsWith("v2/",StringComparison.Ordinal)?parameter.Substring(3):null;
    }
    public void Apply(Frame frame)
    {
        if(animator==null || !animator.isActiveAndEnabled) return;
        foreach(var input in inputs) { float value; bool valid; frame.TryRead(input.key,out value,out valid); animator.SetFloat(input.hash,valid?value:input.original); }
        foreach(var status in statuses) Write(status,frame.enabled && (status.key=="EyeTrackingActive"?(frame.upper||frame.gaze):frame.lower)?1:0);
    }
    void Write(Input input,float value)
    {
        if(input.type==AnimatorControllerParameterType.Bool) animator.SetBool(input.hash,value>.5f);
        else if(input.type==AnimatorControllerParameterType.Int) animator.SetInteger(input.hash,Mathf.RoundToInt(value));
        else animator.SetFloat(input.hash,value);
    }
    public void Restore()
    {
        if(animator==null || animator.runtimeAnimatorController==null) return;
        foreach(var input in inputs) animator.SetFloat(input.hash,input.original);
        foreach(var status in statuses) Write(status,0);
    }
}
