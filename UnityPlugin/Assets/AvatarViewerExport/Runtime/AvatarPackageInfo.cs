using UnityEngine;
namespace AvatarViewer
{
    public sealed class AvatarPackageInfo : MonoBehaviour
    {
        public int formatVersion = 1;
        public string displayName;
        public string unityVersion;
        public string buildPlatform;
        public int convertedChains, convertedColliders;
        public string[] humanoidBoneNames;
        public Transform[] humanoidBones;
        public bool headMaskBaked;
        public bool originalMaterials;
        [System.Serializable] public sealed class MeshVariant { public Renderer renderer; public Mesh original,firstPerson; }
        public MeshVariant[] meshVariants=new MeshVariant[0];
    }
}
