#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace AvatarViewer {
    public static class AvatarMeshVariants {
        public static int Bake(GameObject avatar) {
            var info=avatar.GetComponent<AvatarPackageInfo>(); if(info==null) return 0;
            Transform head=null; for(int i=0;i<info.humanoidBoneNames.Length;i++) if(info.humanoidBoneNames[i]=="Head") head=info.humanoidBones[i];
            var variants=new List<AvatarPackageInfo.MeshVariant>();
            foreach(var renderer in avatar.GetComponentsInChildren<Renderer>(true)) {
                var skin=renderer as SkinnedMeshRenderer; var filter=renderer.GetComponent<MeshFilter>(); var original=skin!=null?skin.sharedMesh:filter!=null?filter.sharedMesh:null;
                if(original==null || !original.isReadable) continue;
                bool whole=head!=null && (renderer.transform==head || renderer.transform.IsChildOf(head));
                var flags=skin!=null?AvatarHeadMask.HeadBones(skin.bones,head):new bool[0];
                var weights=skin!=null?original.boneWeights:new BoneWeight[0]; var hidden=new bool[original.vertexCount]; bool any=whole;
                for(int v=0;v<hidden.Length;v++) {
                    float weight=0; if(v<weights.Length) {var w=weights[v]; weight=AvatarHeadMask.Weight(flags,w.boneIndex0,w.weight0)+AvatarHeadMask.Weight(flags,w.boneIndex1,w.weight1)+AvatarHeadMask.Weight(flags,w.boneIndex2,w.weight2)+AvatarHeadMask.Weight(flags,w.boneIndex3,w.weight3);}
                    hidden[v]=whole || weight>.75f; any|=hidden[v];
                }
                if(!any) continue;
                string directory=Path.GetDirectoryName(AssetDatabase.GetAssetPath(original)).Replace('\\','/');
                string path=directory+"/firstperson-"+original.GetInstanceID().ToString("x")+"-"+renderer.GetInstanceID().ToString("x")+".asset";
                // Reuse saved variants during the viewer build; source conversion creates them once.
                var old=System.Array.Find(info.meshVariants,v=>v.renderer==renderer && v.original==original);
                if(old!=null && old.firstPerson!=null) { variants.Add(old); continue; }
                var mesh=Object.Instantiate(original); mesh.name=original.name+" first person";
                for(int s=0;s<mesh.subMeshCount;s++) { var triangles=original.GetTriangles(s); var kept=new List<int>(triangles.Length); for(int t=0;t<triangles.Length;t+=3) if(!hidden[triangles[t]] && !hidden[triangles[t+1]] && !hidden[triangles[t+2]]) { kept.Add(triangles[t]); kept.Add(triangles[t+1]); kept.Add(triangles[t+2]); } mesh.SetTriangles(kept,s,false); }
                AssetDatabase.CreateAsset(mesh,path); variants.Add(new AvatarPackageInfo.MeshVariant { renderer=renderer,original=original,firstPerson=mesh });
            }
            info.meshVariants=variants.ToArray(); info.headMaskBaked=variants.Count>0; EditorUtility.SetDirty(info); return variants.Count;
        }
    }
}
#endif
