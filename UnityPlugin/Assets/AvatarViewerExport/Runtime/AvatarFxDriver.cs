using System;
using UnityEngine;

namespace AvatarViewer
{
    public sealed class AvatarFxDriver : StateMachineBehaviour
    {
        [Serializable] public sealed class Change {
            public string name,source,type;
            public float value,min,max,chance,sourceMin,sourceMax,destMin,destMax;
            public bool convertRange;
        }
        public Change[] changes=new Change[0];
        public int layer=-1;
        public float layerWeight=1;
        public override void OnStateEnter(Animator animator,AnimatorStateInfo state,int layerIndex) {
            var menu=animator.GetComponent<AvatarMenuData>();
            if(menu==null) return;
            foreach(var c in changes) {
                float value=c.value;
                if(c.type=="Add") value=menu.Get(c.name)+c.value;
                else if(c.type=="Copy") { value=menu.Get(c.source); if(c.convertRange) value=Mathf.Lerp(c.destMin,c.destMax,Mathf.InverseLerp(c.sourceMin,c.sourceMax,value)); }
                else if(c.type=="Random") {
                    var type=AnimatorControllerParameterType.Float;
                    foreach(var p in animator.parameters) if(p.name==c.name) type=p.type;
                    value=type==AnimatorControllerParameterType.Bool?(UnityEngine.Random.value<c.chance?1:0):type==AnimatorControllerParameterType.Int?UnityEngine.Random.Range(Mathf.RoundToInt(c.min),Mathf.RoundToInt(c.max)+1):UnityEngine.Random.Range(c.min,c.max);
                }
                menu.Set(c.name,value);
            }
            if(layer>=0 && layer<animator.layerCount) animator.SetLayerWeight(layer,layerWeight);
        }
    }
}
