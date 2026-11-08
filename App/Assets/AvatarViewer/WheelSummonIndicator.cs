using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.UI;

// Non-interactive, world-space feedback independent of the hidden menu.
public sealed class WheelSummonIndicator : MonoBehaviour {
    Camera eyes;
    Canvas canvas;
    RadialSectorGraphic fill;
    public bool Visible=>canvas!=null && canvas.enabled;
    public float Fill {get;private set;}
    public static WheelSummonIndicator Create(Transform parent,Camera view) {
        var go=new GameObject("Wheel summon progress",typeof(RectTransform),typeof(Canvas),typeof(WheelSummonIndicator));
        go.transform.SetParent(parent,false);
        var result=go.GetComponent<WheelSummonIndicator>();result.Initialize(view);return result;
    }
    void Initialize(Camera view) {
        eyes=view;canvas=GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=eyes;
        ((RectTransform)transform).sizeDelta=new Vector2(30,30);transform.localScale=Vector3.one*.001f;
        Ring("Track",7,13,new Color(.35f,.42f,.49f,1));
        fill=Ring("Progress",7,13,new Color(.05f,.65f,1,1));
        Hide();
    }
    RadialSectorGraphic Ring(string name,float inner,float outer,Color color) {
        var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(RadialSectorGraphic));go.transform.SetParent(transform,false);
        var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(100,100);
        var graphic=go.GetComponent<RadialSectorGraphic>();graphic.inner=inner;graphic.outer=outer;graphic.start=0;graphic.sweep=360;graphic.color=color;graphic.raycastTarget=false;return graphic;
    }
    public void Supply(WheelSummonGesture gesture,HandJointLocations left,HandJointLocations right,Transform tracking,bool focused) {
        float progress=gesture.Progress;
        var hand=gesture.ProgressHand==0?left:right;
        if(!focused || !gesture.ContactActive || progress<=0 || progress>=1
            || !AvatarEmbodiment.Joint(hand,1,tracking,out _)
            || !AvatarEmbodiment.Joint(hand,(int)HandJoint.JointThumbTip,tracking,out var thumb)
            || !AvatarEmbodiment.Joint(hand,(int)HandJoint.JointLittleTip,tracking,out var little)
            || (hand.jointLocations[1].locationStatus & HandLocationStatus.PositionTracked)==0) {Hide();return;}
        var contact=(thumb+little)*.5f;
        var towardEyes=(eyes.transform.position-contact).normalized;
        var position=contact+towardEyes*.012f;
        var direction=position-eyes.transform.position;
        transform.SetPositionAndRotation(position,direction.sqrMagnitude>.0001f?Quaternion.LookRotation(direction,Vector3.up):eyes.transform.rotation);
        Fill=progress;fill.sweep=progress*360;fill.SetVerticesDirty();canvas.enabled=true;
    }
    public void Hide() {if(canvas!=null)canvas.enabled=false;Fill=0;}
}
