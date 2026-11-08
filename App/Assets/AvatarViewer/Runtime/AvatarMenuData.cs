using System;
using System.IO;
using UnityEngine;

namespace AvatarViewer
{
    // Data exported from the model's expression menu. No VRChat runtime is required.
    public sealed class AvatarMenuData : MonoBehaviour
    {
        [Serializable] public sealed class Parameter { public string name,type; public float value; public bool saved; }
        [Serializable] public sealed class Control {
            public string name,type,parameter;
            public float value;
            public int submenu=-1;
            public Texture2D icon;
            public string[] subParameters=new string[0];
        }
        [Serializable] public sealed class Page { public string name; public Control[] controls=new Control[0]; }
        public Page[] pages=new Page[0];
        public Parameter[] parameters=new Parameter[0];
        public Animator effects;
        public string compatibility;
        string pendingControl,pendingParameter; float pendingValue,reportAt;
        public void RecordControl(string control,string parameter,float value) { pendingControl=control; pendingParameter=parameter; pendingValue=value; reportAt=Time.unscaledTime+.3f; }
        [Serializable] sealed class ActionReport { public string timeUtc,control,parameter; public float requested,actual,earParameter; public bool earActive,animatorEnabled; }
        void LateUpdate() {
            if(pendingControl==null || Time.unscaledTime<reportAt) return;
            var ear=transform.Find("Other_ear");
            var report=new ActionReport { timeUtc=DateTime.UtcNow.ToString("O"),control=pendingControl,parameter=pendingParameter,requested=pendingValue,actual=Get(pendingParameter),earParameter=Get("Ear"),earActive=ear!=null && ear.gameObject.activeSelf,animatorEnabled=effects!=null && effects.isActiveAndEnabled };
            File.WriteAllText(Path.Combine(Application.persistentDataPath,"menu-action.json"),JsonUtility.ToJson(report,true));
            Debug.Log("PICO_MENU_ACTION "+report.control+" "+report.parameter+"="+report.actual+" earActive="+report.earActive); pendingControl=null;
        }

        public void InitializeParameters() {
            foreach(var p in parameters) Set(p.name,p.value);
            Set("IsLocal",1); Set("VRMode",1); Set("TrackingType",3); Set("AvatarVersion",3);
        }
        public bool Has(string key) {
            if(effects==null || string.IsNullOrEmpty(key)) return false;
            foreach(var p in effects.parameters) if(p.name==key) return true;
            return false;
        }
        public float Get(string key) {
            if(effects==null || string.IsNullOrEmpty(key)) return 0;
            foreach(var p in effects.parameters) if(p.name==key) {
                if(p.type==AnimatorControllerParameterType.Bool) return effects.GetBool(key)?1:0;
                if(p.type==AnimatorControllerParameterType.Int) return effects.GetInteger(key);
                if(p.type==AnimatorControllerParameterType.Float) return effects.GetFloat(key);
            }
            return 0;
        }
        public bool Set(string key,float value) {
            if(effects==null || string.IsNullOrEmpty(key) || float.IsNaN(value) || float.IsInfinity(value)) return false;
            foreach(var p in effects.parameters) if(p.name==key) {
                if(p.type==AnimatorControllerParameterType.Bool) effects.SetBool(key,value>.5f);
                else if(p.type==AnimatorControllerParameterType.Int) effects.SetInteger(key,Mathf.RoundToInt(value));
                else if(p.type==AnimatorControllerParameterType.Float) effects.SetFloat(key,value);
                else if(value>.5f) effects.SetTrigger(key); else effects.ResetTrigger(key);
                return true;
            }
            return false;
        }
    }
}
