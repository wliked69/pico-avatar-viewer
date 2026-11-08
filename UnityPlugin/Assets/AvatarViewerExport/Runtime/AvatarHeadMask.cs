using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarViewer
{
    // Executed by the desktop converter. Player meshes keep their original GPU buffer layout.
    public static class AvatarHeadMask
    {
        public static int Bake(GameObject avatar) {
            #if UNITY_EDITOR
            if(avatar.GetComponent<AvatarPackageInfo>()?.originalMaterials==true) return AvatarMeshVariants.Bake(avatar);
            #endif
            int count=0;
            var info=avatar.GetComponent<AvatarPackageInfo>(); Transform head=null;
            if(info!=null && info.humanoidBoneNames!=null && info.humanoidBones!=null)
                for(int i=0;i<Math.Min(info.humanoidBoneNames.Length,info.humanoidBones.Length);i++) if(info.humanoidBoneNames[i]=="Head") head=info.humanoidBones[i];
            foreach(var skin in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                if(skin.sharedMesh==null || !skin.sharedMesh.isReadable) continue;
                var flags=HeadBones(skin.bones,head);
                var mesh=skin.sharedMesh; var weights=mesh.boneWeights;
                var markers=new List<Vector2>(mesh.vertexCount);
                // UV3 may already contain authored UV coordinates. Always overwrite
                // the complete mask channel, including non-head props, with zero.
                // Otherwise the first-person shader interprets their UVs as head weights.
                for(int i=0;i<mesh.vertexCount;i++) {
                    float weight=0;
                    if(weights.Length==mesh.vertexCount) { var w=weights[i]; weight=Weight(flags,w.boneIndex0,w.weight0)+Weight(flags,w.boneIndex1,w.weight1)+Weight(flags,w.boneIndex2,w.weight2)+Weight(flags,w.boneIndex3,w.weight3); }
                    markers.Add(new Vector2(weight,0));
                }
                mesh.SetUVs(2,markers); count++;
            }
            foreach(var filter in avatar.GetComponentsInChildren<MeshFilter>(true)) {
                if(filter.sharedMesh==null || !filter.sharedMesh.isReadable) continue;
                filter.sharedMesh.SetUVs(2,new List<Vector2>(new Vector2[filter.sharedMesh.vertexCount]));
            }
            if(info!=null) info.headMaskBaked=count>0;
            return count;
        }
        static bool ActiveWithin(Transform root,Transform node) {
            for(var t=node;t!=null;t=t.parent) { if(!t.gameObject.activeSelf) return false; if(t==root) return true; }
            return false;
        }
        public static bool[] HeadBones(Transform[] bones,Transform mappedHead=null) {
            var flags=new bool[bones.Length];
            for(int i=0;i<bones.Length;i++) for(var t=bones[i];t!=null;t=t.parent) {
                string name=t.name; int colon=name.LastIndexOf(':'); if(colon>=0) name=name.Substring(colon+1);
                if(t==mappedHead || string.Equals(name,"Head",StringComparison.OrdinalIgnoreCase)) { flags[i]=true; break; }
            }
            return flags;
        }
        public static float Weight(bool[] heads,int index,float weight) => index>=0 && index<heads.Length && heads[index]?weight:0;
    }
}
