using AvatarViewer;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Reflect avatar geometry directly; no auxiliary camera or XR render target.
[DefaultExecutionOrder(100)]
public sealed class AvatarMirror : MonoBehaviour
{
    AvatarEmbodiment rig;
    Camera viewer;
    GameObject reflection,clone,label,boundAvatar;
    Transform[] sourceBones,mirrorBones;
    SkinnedMeshRenderer[] sourceSkins,mirrorSkins;
    Renderer[] sourceRenderers,mirrorRenderers;
    readonly List<Material> sourceMaterials=new List<Material>(),mirrorMaterials=new List<Material>();
    MaterialPropertyBlock materialProperties;
    Vector3 plane;
    Quaternion basis;
    float nextFrame;
    float[][] shapeWeights;
    Vector3[] positions,scales;
    public double CopyMilliseconds { get; private set; }
    public bool Visible { get; private set; }
    public bool EarVisible => clone!=null && clone.transform.Find("Other_ear")!=null && clone.transform.Find("Other_ear").gameObject.activeInHierarchy;
    public void Initialize(AvatarEmbodiment embodiment,Camera eyes,Font font) {
        rig=embodiment; viewer=eyes;
        materialProperties=new MaterialPropertyBlock();
        reflection=new GameObject("Avatar reflection");
        label=new GameObject("Mirror label",typeof(RectTransform),typeof(Canvas),typeof(Text));
        var canvas=label.GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace; canvas.worldCamera=eyes;
        var rect=label.GetComponent<RectTransform>(); rect.sizeDelta=new Vector2(900,65); rect.localScale=Vector3.one*.001f;
        var text=label.GetComponent<Text>(); text.font=font; text.fontSize=30; text.text="虚拟镜像 · 眼动 / 表情"; text.color=Color.white; text.alignment=TextAnchor.MiddleCenter; text.raycastTarget=false;
        Reposition();
        SetVisible(false);
    }
    public void SetVisible(bool value) { Visible=value; if(reflection!=null) reflection.SetActive(value); if(label!=null) label.SetActive(value); }
    public void Reposition() {
        if(reflection==null || viewer==null) return;
        basis=Quaternion.Euler(0,viewer.transform.eulerAngles.y,0);
        plane=viewer.transform.position+basis*Vector3.forward*1.1f;
        reflection.transform.SetPositionAndRotation(plane,basis); reflection.transform.localScale=new Vector3(1,1,-1);
        label.transform.SetPositionAndRotation(viewer.transform.position+basis*Vector3.forward*2.2f+Vector3.up*.3f,basis);
    }
    void Bind() {
        if(clone!=null) Destroy(clone);
        boundAvatar=rig.Avatar; clone=Instantiate(boundAvatar,reflection.transform); clone.name="Mirrored avatar";
        var info=clone.GetComponent<AvatarPackageInfo>(); if(info!=null && info.originalMaterials) foreach(var variant in info.meshVariants) {
            if(variant.renderer is SkinnedMeshRenderer s) s.sharedMesh=variant.original;
            else if(variant.renderer!=null) {var f=variant.renderer.GetComponent<MeshFilter>(); if(f!=null) f.sharedMesh=variant.original;}
        }
        foreach(var spring in clone.GetComponentsInChildren<SpringChain>(true)) spring.enabled=false;
        foreach(var animator in clone.GetComponentsInChildren<Animator>(true)) animator.enabled=false;
        foreach(var solver in clone.GetComponentsInChildren<AvatarConstraintSolver>(true)) solver.enabled=false;
        sourceBones=boundAvatar.GetComponentsInChildren<Transform>(true); mirrorBones=clone.GetComponentsInChildren<Transform>(true);
        sourceSkins=boundAvatar.GetComponentsInChildren<SkinnedMeshRenderer>(true); mirrorSkins=clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        sourceRenderers=boundAvatar.GetComponentsInChildren<Renderer>(true); mirrorRenderers=clone.GetComponentsInChildren<Renderer>(true);
        positions=new Vector3[sourceBones.Length]; scales=new Vector3[sourceBones.Length];
        shapeWeights=new float[sourceSkins.Length][];
        for(int s=0;s<sourceSkins.Length;s++) {
            shapeWeights[s]=new float[sourceSkins[s].sharedMesh!=null?sourceSkins[s].sharedMesh.blendShapeCount:0];
            for(int i=0;i<shapeWeights[s].Length;i++) shapeWeights[s][i]=float.NaN;
        }
        foreach(var renderer in clone.GetComponentsInChildren<Renderer>(true)) {
            renderer.gameObject.layer=29;
            var block=new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); block.SetFloat("_AvatarMirror",1); renderer.SetPropertyBlock(block);
        }
    }
    void LateUpdate() {
        CopyMilliseconds=0;
        if(!Visible || rig==null || !rig.Active || !rig.Calibrated || rig.Avatar==null || Time.unscaledTime<nextFrame) return;
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        if(boundAvatar!=rig.Avatar) Bind();
        nextFrame=Time.unscaledTime+1f/45;
        var source=boundAvatar.transform;
        clone.transform.localPosition=Quaternion.Inverse(basis)*(source.position-plane);
        clone.transform.localRotation=Quaternion.Inverse(basis)*source.rotation;
        clone.transform.localScale=source.lossyScale;
        for(int i=1;i<sourceBones.Length && i<mirrorBones.Length;i++) {
            if(mirrorBones[i].gameObject.activeSelf!=sourceBones[i].gameObject.activeSelf) mirrorBones[i].gameObject.SetActive(sourceBones[i].gameObject.activeSelf);
            var p=sourceBones[i].localPosition; if(positions[i]!=p) { mirrorBones[i].localPosition=p; positions[i]=p; }
            mirrorBones[i].localRotation=sourceBones[i].localRotation;
            var scale=sourceBones[i].localScale; if(scales[i]!=scale) { mirrorBones[i].localScale=scale; scales[i]=scale; }
        }
        for(int s=0;s<sourceSkins.Length && s<mirrorSkins.Length;s++) {
            var skin=sourceSkins[s]; mirrorSkins[s].enabled=skin.enabled; if(skin.sharedMesh==null || !skin.enabled || !skin.gameObject.activeInHierarchy) continue;
            for(int i=0;i<shapeWeights[s].Length;i++) {
                float value=skin.GetBlendShapeWeight(i);
                if(shapeWeights[s][i]!=value) { mirrorSkins[s].SetBlendShapeWeight(i,value); shapeWeights[s][i]=value; }
            }
        }
        for(int r=0;r<sourceRenderers.Length && r<mirrorRenderers.Length;r++) {
            var sourceRenderer=sourceRenderers[r]; var mirrorRenderer=mirrorRenderers[r];
            mirrorRenderer.enabled=sourceRenderer.enabled;
            sourceRenderer.GetSharedMaterials(sourceMaterials); mirrorRenderer.GetSharedMaterials(mirrorMaterials);
            bool changed=sourceMaterials.Count!=mirrorMaterials.Count;
            for(int m=0;!changed && m<sourceMaterials.Count;m++) changed=sourceMaterials[m]!=mirrorMaterials[m];
            if(changed) mirrorRenderer.SetSharedMaterials(sourceMaterials);
            sourceRenderer.GetPropertyBlock(materialProperties); materialProperties.SetFloat("_AvatarMirror",1); mirrorRenderer.SetPropertyBlock(materialProperties);
            for(int m=0;m<sourceMaterials.Count;m++) {
                sourceRenderer.GetPropertyBlock(materialProperties,m);
                if(!materialProperties.isEmpty) materialProperties.SetFloat("_AvatarMirror",1);
                mirrorRenderer.SetPropertyBlock(materialProperties,m);
            }
        }
        CopyMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
    }
    void OnDestroy() { if(reflection!=null) Destroy(reflection); if(label!=null) Destroy(label); }
}
