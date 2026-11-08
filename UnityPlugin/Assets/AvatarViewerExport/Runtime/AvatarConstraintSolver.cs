using System.Collections.Generic;
using UnityEngine;
namespace AvatarViewer {
    // Pose attachments after tracked IK and before spring motion and mirror copying.
    [DefaultExecutionOrder(-50)]
    public sealed class AvatarConstraintSolver : MonoBehaviour {
        AvatarConstraint[] constraints;
        public void Rebuild() {
            var all=GetComponentsInChildren<AvatarConstraint>(true);
            var ordered=new List<AvatarConstraint>(); var visited=new HashSet<AvatarConstraint>();
            void Visit(AvatarConstraint current) {
                if(!visited.Add(current)) return;
                foreach(var source in current.Sources) if(source.SourceTransform!=null) foreach(var other in all)
                    if(other!=current && (source.SourceTransform==other.Target || source.SourceTransform.IsChildOf(other.Target))) Visit(other);
                // Constrained ancestors must also settle before their children.
                foreach(var other in all) if(other!=current && current.Target.IsChildOf(other.Target)) Visit(other);
                ordered.Add(current);
            }
            foreach(var c in all) Visit(c); constraints=ordered.ToArray();
        }
        void LateUpdate() { Evaluate(); }
        public void Evaluate() { if(constraints==null) Rebuild(); foreach(var c in constraints) if(c!=null) c.Evaluate(); }
    }
}
