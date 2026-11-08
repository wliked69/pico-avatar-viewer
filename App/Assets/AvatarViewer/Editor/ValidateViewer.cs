using System;
using System.IO;
using AvatarViewer;
using UnityEngine;

public static class ValidateViewer
{
    public static void Run()
    {
        int checks=0;
        var colliderObject=new GameObject("Test collider");
        var collider=colliderObject.AddComponent<SpringCollider>(); collider.radius=.5f;
        var point=collider.Resolve(Vector3.right*.1f,.1f);
        Check(Mathf.Abs(point.magnitude-.6f)<.0001f,"Sphere surface separation"); checks++;
        collider.inside=true;
        Check(Mathf.Abs(collider.Resolve(Vector3.right,.1f).magnitude-.4f)<.0001f,"Inside sphere containment"); checks++;
        collider.inside=false; collider.capsule=true; collider.height=2;
        point=collider.Resolve(new Vector3(.1f,.5f,0),.1f);
        Check(Mathf.Abs(point.x-.6f)<.0001f && Mathf.Abs(point.y-.5f)<.0001f,"Capsule closest axis point"); checks++;
        colliderObject.transform.localScale=Vector3.one*2;
        point=collider.Resolve(Vector3.right*.1f,.1f);
        Check(Mathf.Abs(point.x-1.1f)<.0001f,"Scaled collider separation"); checks++;
        UnityEngine.Object.DestroyImmediate(colliderObject);
        var root=new GameObject("Chain root"); var child=new GameObject("Tail"); child.transform.SetParent(root.transform); child.transform.localPosition=Vector3.right;
        var spring=root.AddComponent<SpringChain>(); spring.root=root.transform; spring.gravity=.6f; spring.maxAngle=35; spring.Rebuild();
        for(int i=0;i<360;i++) spring.Advance(1f/90f);
        Check(child.transform.position.y < -.01f,"Gravity deforms the chain"); checks++;
        Check(Mathf.Abs(child.transform.localPosition.magnitude-1)<.0001f,"Bone length preserved"); checks++;
        Check(Quaternion.Angle(Quaternion.identity,root.transform.rotation)<=35.01f,"Cone limit respected"); checks++;
        spring.simulate=false; spring.Advance(1f/90f);
        Check(Quaternion.Angle(Quaternion.identity,root.transform.rotation)<.0001f,"Disabling restores rest pose"); checks++;
        spring.simulate=true; root.transform.position=Vector3.one*5; spring.Advance(1f/90f);
        Check(!float.IsNaN(root.transform.rotation.x),"Teleport resets finite state"); checks++;
        UnityEngine.Object.DestroyImmediate(root);
        var buttonObject=new GameObject("World UI test",typeof(RectTransform));
        var rectangle=buttonObject.GetComponent<RectTransform>(); rectangle.sizeDelta=new Vector2(200,60);
        rectangle.position=new Vector3(-.7f,1.2f,1.55f); rectangle.rotation=Quaternion.Euler(5,15,0); rectangle.localScale=Vector3.one*.00135f;
        Check(ViewerController.ContainsWorldPoint(rectangle,rectangle.TransformPoint(Vector3.zero)),"XR world-space button hit without screen coordinates"); checks++;
        Check(!ViewerController.ContainsWorldPoint(rectangle,rectangle.TransformPoint(new Vector3(150,0,0))),"XR button rejects outside points"); checks++;
        UnityEngine.Object.DestroyImmediate(buttonObject);
        Check(ViewerController.PinchFromDistance(true,.02f,false),"Pinch starts with touching fingertips"); checks++;
        Check(ViewerController.PinchFromDistance(true,.032f,true),"Pinch hysteresis ignores small tracking jitter"); checks++;
        Check(!ViewerController.PinchFromDistance(true,.05f,true),"Opening fingers releases pinch"); checks++;
        Check(!ViewerController.PinchFromDistance(false,0,true),"Tracking loss releases pinch"); checks++;
        checks+=ValidateHands();
        checks+=ValidateHandBones();
        File.WriteAllText(Path.GetFullPath("../validation-result.json"),"{\"passed\":true,\"checks\":"+checks+"}");
        Debug.Log("VIEWER_VALIDATION_PASS "+checks);
    }
    static int ValidateHands() {
        int checks=0;
        var grab=new ModelHandManipulation { DepthGain=3 };
        Vector3 position=new Vector3(0,0,2); float yaw=180,size=1;
        var left=new ModelHandManipulation.Hand { valid=true,rotation=Quaternion.identity,position=new Vector3(-.2f,1,.5f) };
        var right=new ModelHandManipulation.Hand { valid=true,pinched=true,rotation=Quaternion.identity,position=new Vector3(.2f,1,.5f) };
        grab.Step(left,right,false,ref position,ref yaw,ref size);
        Check(grab.Mode==0,"Pinching away from target does not grab"); checks++;
        grab.Step(left,right,true,ref position,ref yaw,ref size);
        Check(grab.Mode==2 && Vector3.Distance(position,new Vector3(0,0,2))<.0001f,"Pinch-first then look acquires without jumping"); checks++;
        right.position+=new Vector3(.1f,.2f,-.3f); right.rotation=Quaternion.Euler(0,90,0);
        for(int i=0;i<90;i++) grab.Step(left,right,false,ref position,ref yaw,ref size);
        Check(Vector3.Distance(position,new Vector3(.14f,.2f,1.1f))<.001f,"Thirty centimetres of pull moves distant model ninety centimetres"); checks++;
        Check(Mathf.Abs(yaw-180)<.001f,"Single-hand drag ignores wrist twisting"); checks++;
        var before=position;
        var missing=right; missing.valid=false; missing.position=Vector3.one*100;
        grab.Step(left,missing,false,ref position,ref yaw,ref size,.1f);
        Check(grab.Mode==2 && Vector3.Distance(before,position)<.001f,"Brief tracking occlusion retains grab without teleport"); checks++;
        grab.Step(left,right,false,ref position,ref yaw,ref size);
        left.pinched=true; grab.Step(left,right,false,ref position,ref yaw,ref size);
        Check(grab.Mode==3 && Vector3.Distance(before,position)<.001f && size==1,"Adding second hand rebases without jump"); checks++;
        var middle=(left.position+right.position)*.5f; var half=(right.position-left.position)*.5f;
        left.position=middle-half*2; right.position=middle+half*2;
        for(int i=0;i<90;i++) grab.Step(left,right,false,ref position,ref yaw,ref size);
        Check(Mathf.Abs(size-2)<.001f && Vector3.Distance(before,position)<.001f,"Two-hand stretch scales around stable model position"); checks++;
        half=Quaternion.Euler(0,90,0)*half;
        left.position=middle-half*2; right.position=middle+half*2;
        for(int i=0;i<90;i++) grab.Step(left,right,false,ref position,ref yaw,ref size);
        Check(Mathf.Abs(Mathf.Abs(Mathf.DeltaAngle(180,yaw))-90)<.001f,"Two-hand turning rotates model"); checks++;
        before=position; float beforeYaw=yaw; left.pinched=false;
        grab.Step(left,right,false,ref position,ref yaw,ref size,.1f);
        Check(grab.Mode==2 && Vector3.Distance(before,position)<.001f && Mathf.Abs(beforeYaw-yaw)<.001f && Mathf.Abs(size-2)<.001f,"Releasing one hand preserves transform"); checks++;
        right.valid=false; right.position=Vector3.one*100;
        grab.Step(left,right,false,ref position,ref yaw,ref size,.2f);
        Check(grab.Mode==0 && Vector3.Distance(before,position)<.001f,"Sustained tracking loss safely releases model"); checks++;
        left.pinched=true; grab.Step(left,right,true,ref position,ref yaw,ref size);
        Check(grab.Mode==1,"Left hand can independently acquire"); checks++;
        left.pinched=false; grab.Step(left,right,false,ref position,ref yaw,ref size,.1f);
        Check(grab.Mode==0,"Deliberate finger opening releases promptly"); checks++;
        // Freeze the last displayed position even while smoothing has not caught up.
        grab.Cancel(); right.valid=true; right.pinched=true;
        grab.Step(left,right,true,ref position,ref yaw,ref size);
        right.position.z-=.2f; grab.Step(left,right,false,ref position,ref yaw,ref size);
        before=position;
        right.pinched=false; right.position.z+=.4f;
        grab.Step(left,right,false,ref position,ref yaw,ref size,1f/90f);
        Check(grab.Mode==0 && Vector3.Distance(before,position)<.000001f,"Finger opening immediately freezes displayed model position"); checks++;
        for(int i=0;i<20;i++) grab.Step(left,right,false,ref position,ref yaw,ref size);
        Check(Vector3.Distance(before,position)<.000001f,"No smoothing tail or rebound after release"); checks++;
        right.pinched=true; grab.Step(left,right,true,ref position,ref yaw,ref size);
        right.position.z-=.2f; grab.Step(left,right,false,ref position,ref yaw,ref size);
        before=position; right.valid=false;
        grab.Step(left,right,false,ref position,ref yaw,ref size,.05f);
        Check(Vector3.Distance(before,position)<.000001f,"Tracking loss freezes in-flight smoothing immediately"); checks++;
        return checks;
    }
    static int ValidateHandBones() {
        int checks=0;
        var root=new GameObject("Touchable hair root"); var tip=new GameObject("Hair tip"); tip.transform.SetParent(root.transform); tip.transform.localPosition=Vector3.right;
        var chain=root.AddComponent<SpringChain>(); chain.root=root.transform; chain.pull=.8f; chain.gravity=0; chain.maxAngle=70; chain.Rebuild();
        var hand=new GameObject("Tracked hand collision test").AddComponent<SpringCollider>();
        hand.radius=.2f; hand.transform.position=new Vector3(.95f,-.1f,0); chain.interactionColliders=new[]{hand};
        for(int i=0;i<90;i++) chain.Advance(1f/90f);
        Check(chain.HandContactEvents>0 && tip.transform.position.y>.01f,"Tracked hand collision visibly deflects dynamic bone"); checks++;
        Check(Mathf.Abs(tip.transform.position.magnitude-1)<.0001f,"Hand collision preserves bone length"); checks++;
        chain.maxAngle=5; chain.ResetSimulation(); float largestDelta=0; Quaternion last=root.transform.rotation;
        for(int i=0;i<240;i++) { chain.Advance(1f/90); if(i>180) largestDelta=Mathf.Max(largestDelta,Quaternion.Angle(last,root.transform.rotation)); last=root.transform.rotation; }
        Check(Quaternion.Angle(Quaternion.identity,root.transform.rotation)<5.1f,"Hand collision cannot bypass the cloth cone limit"); checks++;
        Check(largestDelta<.1f,"Stationary contact settles instead of injecting collision correction as spring velocity"); checks++;
        chain.maxAngle=70;
        hand.enabled=false; chain.ResetSimulation(); chain.Advance(1f/90f);
        Check(Mathf.Abs(tip.transform.position.y)<.0001f,"Tracking-disabled hand collider does not affect bone"); checks++;
        Check(chain.FindHandGrab(Vector3.right,.065f,out var index,out var distance) && chain.BeginHandGrab(index,Vector3.right),"Pinch can acquire nearby dynamic bone endpoint"); checks++;
        chain.MoveHandGrab(new Vector3(.8f,.5f,0)); chain.Advance(1f/90f);
        Check(chain.HasHandGrab && tip.transform.position.y>.3f && Mathf.Abs(tip.transform.position.magnitude-1)<.0001f,"Pinched hair follows hand within bone-length constraint"); checks++;
        chain.ReleaseHandGrab();
        for(int i=0;i<180;i++) chain.Advance(1f/90f);
        Check(!chain.HasHandGrab && !float.IsNaN(tip.transform.position.x) && Mathf.Abs(tip.transform.position.y)<.01f,"Released hair returns to rest with finite state"); checks++;
        UnityEngine.Object.DestroyImmediate(hand.gameObject); UnityEngine.Object.DestroyImmediate(root);
        var interaction=new HandBoneInteraction();
        var locations=new ByteDance.PICO.XR.HandJointLocations { isActive=1,jointCount=26,jointLocations=new ByteDance.PICO.XR.HandJointLocation[26] };
        for(int i=0;i<26;i++) locations.jointLocations[i].locationStatus=ByteDance.PICO.XR.HandLocationStatus.PositionValid;
        interaction.Update(locations,locations,null,default,default,false);
        Check(interaction.ActiveColliders==0,"Untracked inferred hands cannot keep pushing clothing"); checks++;
        foreach(var c in interaction.Colliders) UnityEngine.Object.DestroyImmediate(c.gameObject); interaction.Release();
        return checks;
    }
    static void Check(bool test,string description) { if(!test) throw new Exception("Validation failed: "+description); Debug.Log("VALIDATED: "+description); }
}
