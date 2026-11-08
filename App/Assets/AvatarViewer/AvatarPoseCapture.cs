using System;
using System.IO;
using UnityEngine;

// USB-requested pose snapshot for reproducing a live rig problem in the editor.
[DefaultExecutionOrder(250)]
public sealed class AvatarPoseCapture : MonoBehaviour {
    public AvatarEmbodiment rig;
    float nextCheck;
    [Serializable] public sealed class Node { public string path; public Vector3 position,scale; public Quaternion rotation; public bool active; }
    [Serializable] public sealed class Skin { public string path; public float[] weights; public bool enabled; }
    [Serializable] public sealed class Snapshot { public string utc; public Vector3 position,scale; public Quaternion rotation; public Node[] nodes; public Skin[] skins; public BodyProportions.Settings body; public Vector3 leftWristTarget,rightWristTarget; public bool leftTracked,rightTracked; public float leftWristError,rightWristError; }
    public static string PathOf(Transform node,Transform root) { if(node==root) return ""; return node.parent==root?node.name:PathOf(node.parent,root)+"/"+node.name; }
    void LateUpdate() {
        if(Time.unscaledTime<nextCheck || rig==null || rig.Avatar==null || !rig.Calibrated || !Application.isFocused) return; nextCheck=Time.unscaledTime+1;
        string request=Path.Combine(Application.persistentDataPath,"capture-pose.request"); if(!File.Exists(request)) return;
        var root=rig.Avatar.transform; var transforms=root.GetComponentsInChildren<Transform>(true); var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var snapshot=new Snapshot { utc=DateTime.UtcNow.ToString("O"),position=root.position,rotation=root.rotation,scale=root.lossyScale,nodes=new Node[transforms.Length],skins=new Skin[skins.Length] };
        snapshot.body=rig.Proportions.Value;snapshot.leftWristTarget=rig.LeftWristTarget;snapshot.rightWristTarget=rig.RightWristTarget;
        snapshot.leftTracked=rig.LeftTracked;snapshot.rightTracked=rig.RightTracked;snapshot.leftWristError=rig.LeftWristError;snapshot.rightWristError=rig.RightWristError;
        for(int i=0;i<transforms.Length;i++) { var t=transforms[i]; snapshot.nodes[i]=new Node { path=PathOf(t,root),position=t.localPosition,rotation=t.localRotation,scale=t.localScale,active=t.gameObject.activeSelf }; }
        for(int i=0;i<skins.Length;i++) { var s=skins[i]; var weights=new float[s.sharedMesh!=null?s.sharedMesh.blendShapeCount:0]; for(int w=0;w<weights.Length;w++) weights[w]=s.GetBlendShapeWeight(w); snapshot.skins[i]=new Skin { path=PathOf(s.transform,root),enabled=s.enabled,weights=weights }; }
        File.WriteAllText(Path.Combine(Application.persistentDataPath,"pose-snapshot.json"),JsonUtility.ToJson(snapshot)); File.Delete(request);
    }
}
