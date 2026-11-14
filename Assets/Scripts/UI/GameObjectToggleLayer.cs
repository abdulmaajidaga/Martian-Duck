using UnityEngine;

/// <summary>
/// Generic data layer that shows/hides a set of scene objects. Useful for anything already built as GameObjects
/// (e.g. the rover drive-path overlay): add this component, fill in Targets, and it appears in the layer panel.
/// </summary>
public class GameObjectToggleLayer : MarsDataLayer
{
    [SerializeField] GameObject[] targets;

    protected override void Start()
    {
        // Start from the layer's own on/off state rather than whatever the scene objects happen to be.
        if (!IsOn) ApplyState(false);
        base.Start();
    }

    protected override void ApplyState(bool on)
    {
        if (targets == null) return;
        foreach (GameObject t in targets)
            if (t != null) t.SetActive(on);
    }
}
