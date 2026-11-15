using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class for a switchable data layer (slope hazard, mineral map, rover traverse, ...).
/// To add a layer: create a component deriving from this (implement ApplyState), add it to a GameObject in the
/// scene, and it registers itself; the layer panel lists it automatically. No HUD code changes are needed.
/// </summary>
public abstract class MarsDataLayer : MonoBehaviour
{
    /// <summary>Section of the layer panel: data drawn on the terrain, or map content (markers, routes).</summary>
    public enum LayerGroup { Data, MapOverlay }

    [SerializeField] string displayName = "Data layer";
    [TextArea(1, 3)] [SerializeField] string description;
    [SerializeField] bool startEnabled;
    [Tooltip("Position in the layer list (lower first).")]
    [SerializeField] int sortOrder;
    [Tooltip("Panel section: Data (drawn on the terrain) or Map Overlay (markers, routes and other map content).")]
    [SerializeField] LayerGroup group = LayerGroup.Data;

    public string DisplayName => displayName;
    public string Description => description;
    public int SortOrder => sortOrder;
    public virtual LayerGroup Group => group;
    public bool IsOn { get; private set; }

    /// <summary>Optional one-line legend shown under the layer name (e.g. value range).</summary>
    public virtual string Legend => null;

    public static readonly List<MarsDataLayer> Registered = new List<MarsDataLayer>();
    public static event Action RegistryChanged;
    public static event Action<MarsDataLayer> LayerToggled;

    protected virtual void OnEnable()
    {
        if (!Registered.Contains(this)) Registered.Add(this);
        Registered.Sort((a, b) =>
            a.Group != b.Group ? a.Group.CompareTo(b.Group)
            : a.sortOrder != b.sortOrder ? a.sortOrder.CompareTo(b.sortOrder)
            : string.CompareOrdinal(a.displayName, b.displayName));
        RegistryChanged?.Invoke();
    }

    protected virtual void Start()
    {
        if (startEnabled) SetOn(true);
    }

    protected virtual void OnDisable()
    {
        IsOn = false;   // layers clean up their own content (changing other scene objects here is unsafe while the scene unloads)
        Registered.Remove(this);
        RegistryChanged?.Invoke();
    }

    public void SetOn(bool on)
    {
        if (on == IsOn) return;
        IsOn = on;
        ApplyState(on);
        LayerToggled?.Invoke(this);
    }

    public void Toggle() => SetOn(!IsOn);

    /// <summary>Show or hide the layer's content.</summary>
    protected abstract void ApplyState(bool on);
}
