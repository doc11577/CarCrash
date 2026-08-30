using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Turns key presses into three numbers. Kept separate from <see cref="CarController"/>
/// so that split-screen (and later, AI traffic) can drive the same car by writing to the
/// same fields without any of them knowing about a keyboard.
/// </summary>
public class CarInput : MonoBehaviour, ICarDriver
{
    public enum Scheme
    {
        WasdAndArrows,  // single player: either set of keys works
        WasdOnly,       // split-screen player 1
        ArrowsOnly      // split-screen player 2
    }

    [Tooltip("Which keys this car listens to. Single player should use WasdAndArrows.")]
    public Scheme scheme = Scheme.WasdAndArrows;

    [Tooltip("Turn off to freeze input — used for countdowns and crash replays.")]
    public bool acceptInput = true;

    /// <summary>-1 (full reverse / brake) to 1 (full throttle).</summary>
    public float Throttle { get; set; }

    /// <summary>-1 (left) to 1 (right).</summary>
    public float Steer { get; set; }

    /// <summary>True while the handbrake is held.</summary>
    public bool Handbrake { get; set; }

    void Update()
    {
        if (!acceptInput)
        {
            Throttle = 0f;
            Steer = 0f;
            Handbrake = false;
            return;
        }

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        bool wasd   = scheme != Scheme.ArrowsOnly;
        bool arrows = scheme != Scheme.WasdOnly;

        float throttle = 0f;
        if ((wasd && kb.wKey.isPressed) || (arrows && kb.upArrowKey.isPressed))    throttle += 1f;
        if ((wasd && kb.sKey.isPressed) || (arrows && kb.downArrowKey.isPressed))  throttle -= 1f;

        float steer = 0f;
        if ((wasd && kb.dKey.isPressed) || (arrows && kb.rightArrowKey.isPressed)) steer += 1f;
        if ((wasd && kb.aKey.isPressed) || (arrows && kb.leftArrowKey.isPressed))  steer -= 1f;

        Throttle = throttle;
        Steer = steer;

        // Space for single player, right-shift for the arrow-keys player so the two
        // split-screen schemes never fight over the same key.
        Handbrake = scheme == Scheme.ArrowsOnly
            ? kb.rightShiftKey.isPressed
            : kb.spaceKey.isPressed || kb.leftShiftKey.isPressed;
    }
}
