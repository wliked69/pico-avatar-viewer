using System;
using UnityEngine.Events;

public sealed class ViewerWheelOptions {
    public sealed class ModelFile {public string path,label;}
    public Func<bool> Embodied,Bones,Expressions;
    public UnityAction ToggleEmbodied,ToggleBones,ToggleExpressions,Import,Scan,Quit;
    public Func<string> ModelTitle,Status,Tracking;
    public Func<ModelFile[]> Models;
    public Action<string> LoadModel;
    public WheelSummonGesture Gesture;
}
