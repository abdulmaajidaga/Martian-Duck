using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds the ordered route of waypoints. The HUD and map read from it. Reaching the current waypoint
/// (within its arrive radius) advances to the next one when Auto Advance is on.
/// </summary>
[DisallowMultipleComponent]
public class RouteManager : MonoBehaviour
{
    [SerializeField] List<Waypoint> waypoints = new List<Waypoint>();
    [SerializeField] bool autoAdvance = true;
    [Tooltip("Who reaches waypoints. If empty, the MarsPlayerController in the scene is used.")]
    [SerializeField] Transform traveller;

    public IReadOnlyList<Waypoint> Waypoints => waypoints;
    public int CurrentIndex { get; private set; }

    public event Action<Waypoint> WaypointReached;
    public event Action RouteChanged;

    static RouteManager current;
    public static RouteManager Current
    {
        get
        {
            if (current == null) current = FindAnyObjectByType<RouteManager>();
            return current;
        }
    }

    void OnEnable() => current = this;

    /// <summary>True when the route has at least one waypoint.</summary>
    public bool HasRoute
    {
        get { foreach (Waypoint w in waypoints) if (w != null) return true; return false; }
    }

    public bool IsComplete => HasRoute && Next == null;

    /// <summary>The waypoint to head for, or null if there is none / the route is complete.</summary>
    public Waypoint Next
    {
        get
        {
            for (int i = CurrentIndex; i < waypoints.Count; i++)
                if (waypoints[i] != null) return waypoints[i];
            return null;
        }
    }

    public void SetRoute(IEnumerable<Waypoint> route)
    {
        waypoints = new List<Waypoint>(route);
        CurrentIndex = 0;
        RouteChanged?.Invoke();
    }

    public void Add(Waypoint w) { waypoints.Add(w); RouteChanged?.Invoke(); }
    public void Clear() { waypoints.Clear(); CurrentIndex = 0; RouteChanged?.Invoke(); }
    public void Restart() { CurrentIndex = 0; RouteChanged?.Invoke(); }

    public void Advance()
    {
        Waypoint reached = Next;
        if (reached == null) return;
        CurrentIndex = waypoints.IndexOf(reached) + 1;
        WaypointReached?.Invoke(reached);
        RouteChanged?.Invoke();
    }

    void Update()
    {
        if (!autoAdvance) return;
        if (traveller == null)
        {
            var pc = FindAnyObjectByType<MarsPlayerController>();
            if (pc != null) traveller = pc.transform;
        }
        Waypoint next = Next;
        if (next == null || traveller == null) return;
        Vector3 d = next.Position - traveller.position; d.y = 0f;
        if (d.magnitude <= next.arriveRadius) Advance();
    }
}
