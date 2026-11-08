using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class RadialSectorGraphic : MaskableGraphic
{
    protected RadialSectorGraphic() { useLegacyMeshGeneration=false; }
    public float inner,outer,start,sweep;
    protected override void OnPopulateMesh(VertexHelper helper) {
        helper.Clear(); int segments=Mathf.Max(2,Mathf.CeilToInt(sweep/5));
        for(int i=0;i<=segments;i++) {
            float angle=(start+sweep*i/segments)*Mathf.Deg2Rad; var direction=new Vector2(Mathf.Sin(angle),Mathf.Cos(angle));
            helper.AddVert(direction*inner,color,Vector2.zero); helper.AddVert(direction*outer,color,Vector2.zero);
            if(i>0) { int k=i*2; helper.AddTriangle(k-2,k-1,k); helper.AddTriangle(k,k-1,k+1); }
        }
    }
}

