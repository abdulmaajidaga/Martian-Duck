using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Keyboard shortcuts used by the astronaut interface. Works with the Input System package (active in this
/// project) and falls back to the legacy Input Manager if that is ever selected instead.
/// </summary>
public static class MarsInput
{
#if ENABLE_INPUT_SYSTEM
    static bool Pressed(Key key)
    {
        Keyboard kb = Keyboard.current;
        return kb != null && kb[key].wasPressedThisFrame;
    }

    public static bool MapToggle => Pressed(Key.M);
    public static bool LayersToggle => Pressed(Key.L);
    public static bool HelpToggle => Pressed(Key.H);
    /// <summary>Toggle explore (creative) mode: very fast movement.</summary>
    public static bool ExploreToggle => Pressed(Key.X);
    /// <summary>Open / close the photo viewer at a point of interest.</summary>
    public static bool PoiViewerToggle => Pressed(Key.E);
    public static bool PhotoNext => Pressed(Key.RightArrow) || Pressed(Key.P);
    public static bool PhotoPrev => Pressed(Key.LeftArrow);
    public static bool Close => Pressed(Key.Escape);

    // ---- map navigation ----
    /// <summary>Scroll this frame in wheel notches (positive = away from the user / zoom in). Trackpads give fractions.</summary>
    public static float MapScroll
    {
        get
        {
            Mouse m = Mouse.current;
            if (m == null) return 0f;
            float y = m.scroll.ReadValue().y;
            return Mathf.Clamp(y, -1f, 1f);   // some platforms report 120 per notch, others 1
        }
    }
    /// <summary>+1 while a zoom-in key is held, -1 while a zoom-out key is held.</summary>
    public static float MapZoomKeys
    {
        get
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return 0f;
            float z = 0f;
            if (kb[Key.Equals].isPressed || kb[Key.NumpadPlus].isPressed) z += 1f;
            if (kb[Key.Minus].isPressed || kb[Key.NumpadMinus].isPressed) z -= 1f;
            return z;
        }
    }
    public static bool MapResetView => Pressed(Key.R);
    /// <summary>Mouse position in screen pixels (origin bottom-left).</summary>
    public static Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    public static bool DragPressed => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    public static bool DragHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;

    /// <summary>1..9 if a number key (top row or keypad) was pressed this frame, otherwise 0.</summary>
    public static int NumberPressed
    {
        get
        {
            for (int i = 1; i <= 9; i++)
                if (Pressed(Key.Digit1 + (i - 1)) || Pressed(Key.Numpad1 + (i - 1)))
                    return i;
            return 0;
        }
    }
#else
    public static bool MapToggle => Input.GetKeyDown(KeyCode.M);
    public static bool LayersToggle => Input.GetKeyDown(KeyCode.L);
    public static bool HelpToggle => Input.GetKeyDown(KeyCode.H);
    public static bool ExploreToggle => Input.GetKeyDown(KeyCode.X);
    public static bool PoiViewerToggle => Input.GetKeyDown(KeyCode.E);
    public static bool PhotoNext => Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.P);
    public static bool PhotoPrev => Input.GetKeyDown(KeyCode.LeftArrow);
    public static bool Close => Input.GetKeyDown(KeyCode.Escape);

    public static float MapScroll => Mathf.Clamp(Input.mouseScrollDelta.y, -1f, 1f);
    public static float MapZoomKeys =>
        ((Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus)) ? 1f : 0f) -
        ((Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus)) ? 1f : 0f);
    public static bool MapResetView => Input.GetKeyDown(KeyCode.R);
    public static Vector2 PointerPosition => Input.mousePosition;
    public static bool DragPressed => Input.GetMouseButtonDown(0);
    public static bool DragHeld => Input.GetMouseButton(0);

    public static int NumberPressed
    {
        get
        {
            for (int i = 1; i <= 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                    return i;
            return 0;
        }
    }
#endif
}
