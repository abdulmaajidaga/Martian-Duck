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
    public static bool NextPhoto => Pressed(Key.P);

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
    public static bool NextPhoto => Input.GetKeyDown(KeyCode.P);

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
