using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class RadialMenuSymbol : MaskableGraphic
{
    protected RadialMenuSymbol() { useLegacyMeshGeneration=false; }
    public string kind; public bool selected;
    protected override void OnPopulateMesh(VertexHelper h) {
        h.Clear();
        if(kind=="SubMenu") {
            var p=new[]{new Vector2(-22,-15),new Vector2(22,-15),new Vector2(22,13),new Vector2(-2,13),new Vector2(-9,21),new Vector2(-22,21),new Vector2(-22,-15)};
            for(int i=1;i<p.Length;i++) Line(h,p[i-1],p[i]); return;
        }
        for(int i=0;i<32;i++) { float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16; Line(h,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*20,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*20); }
        if(kind=="Toggle") { if(selected) { Line(h,new Vector2(-11,0),new Vector2(-3,-8)); Line(h,new Vector2(-3,-8),new Vector2(12,10)); } }
        else if(kind!=null && kind.EndsWith("Puppet")) Line(h,Vector2.zero,new Vector2(12,12));
        else { Line(h,new Vector2(-5,-9),new Vector2(8,0)); Line(h,new Vector2(8,0),new Vector2(-5,9)); }
    }
    void Line(VertexHelper h,Vector2 a,Vector2 b) {
        Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*1.6f; int i=h.currentVertCount;
        h.AddVert(a+n,color,Vector2.zero); h.AddVert(a-n,color,Vector2.zero); h.AddVert(b-n,color,Vector2.zero); h.AddVert(b+n,color,Vector2.zero);
        h.AddTriangle(i,i+1,i+2); h.AddTriangle(i,i+2,i+3);
    }
}


