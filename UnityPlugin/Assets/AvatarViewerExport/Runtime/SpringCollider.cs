using UnityEngine;

namespace AvatarViewer
{
    public sealed class SpringCollider : MonoBehaviour
    {
        public Vector3 center;
        public Quaternion rotation = Quaternion.identity;
        public float radius = .05f, height;
        public bool capsule, inside;
        int cachedFrame=-1;
        Vector3 worldCenter,worldAxis,worldRight;
        float worldScale,worldHalf;

        void Prepare() {
            if(Application.isPlaying && cachedFrame==Time.frameCount) return;
            cachedFrame=Time.frameCount;
            var scale=transform.lossyScale;
            worldScale=Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
            worldCenter=transform.TransformPoint(center); worldAxis=transform.rotation*rotation*Vector3.up; worldRight=transform.right;
            worldHalf=capsule?Mathf.Max(0,height*.5f-radius)*worldScale:0;
        }

        public Vector3 Resolve(Vector3 point, float particleRadius)
        {
            Prepare();
            var closest = worldCenter + worldAxis * Mathf.Clamp(Vector3.Dot(point - worldCenter, worldAxis), -worldHalf, worldHalf);
            var delta = point - closest;
            float distance = delta.magnitude;
            float limit = inside ? Mathf.Max(0, radius * worldScale - particleRadius) : radius * worldScale + particleRadius;
            if ((inside && distance > limit) || (!inside && distance < limit))
                return closest + (distance > .00001f ? delta / distance : worldRight) * limit;
            return point;
        }
    }
}
