using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

// Keeps the world-space controls and controller ray in the same floor tracking space.
public sealed class HeadTracking : MonoBehaviour
{
    static readonly List<XRInputSubsystem> floorInputs=new List<XRInputSubsystem>();
    public static bool FloorOriginReady {
        get {SubsystemManager.GetInstances(floorInputs);foreach(var system in floorInputs)if(system.running && system.GetTrackingOriginMode()==TrackingOriginModeFlags.Floor)return true;return false;}
    }
    readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
    void Start() {
        SubsystemManager.GetInstances(inputs);
        foreach(var input in inputs) input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
    }
}
