using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// TAB pauses the run. Resume, or go back to the menu.
/// </summary>
/// <remarks>
/// Deliberately two buttons and nothing else for now. Restart already exists on R, and settings
/// have nowhere to go until there is something to set.
///
/// Pausing is `Time.timeScale = 0`, which stops FixedUpdate and therefore all physics, and stops
/// Time.time so a combo cannot expire while the game is paused. Everything in this component
/// runs on UNSCALED time for the same reason — a pause menu driven by scaled time would be
/// frozen the moment it opened, including the button that unpauses it.
/// </remarks>
[DisallowMultipleComponent]
public class PauseMenu : MonoBehaviour
{
    /// <summary>True while the run is paused. Read this rather than checking Time.timeScale.</summary>
    public static bool Paused { get; private set; }

    [Tooltip("Key that opens and closes the pause menu.")]
    public Key pauseKey = Key.Tab;

    [Tooltip("Scene to return to. Must be in File > Build Profiles > Scene List.")]
    public string menuScene = "MainMenu";

    [Tooltip("Ignore the pause key for this long after a scene loads, so a key held through a " +
             "load does not immediately pause the new run.")]
    public float inputLockout = 0.25f;

    RectTransform root;
    GameObject panel;
    CarInput car;
    float armedAt;

    void Awake()
    {
        UiKit.EnsureEventSystem();

        // Above the HUD, which sits at 0 and 1.
        root = UiKit.Screen(transform, "Pause", 10);
        UiKit.Backdrop(root, new Color(0.05f, 0.04f, 0.06f, 0.82f));

        panel = new GameObject("PausePanel", typeof(RectTransform));
        panel.transform.SetParent(root, false);
        RectTransform rect = (RectTransform)panel.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        UiKit.Text(panel.transform, "PAUSED", 84f, UiKit.Accent,
                   TextAlignmentOptions.Center, new Vector2(0f, 190f), new Vector2(900f, 110f))
             .fontStyle = FontStyles.Bold;

        UiKit.Button(panel.transform, "RESUME", new Vector2(0f, 40f), new Vector2(420f, 74f),
                     Resume, accent: true);

        UiKit.Button(panel.transform, "RETURN TO MENU", new Vector2(0f, -60f),
                     new Vector2(420f, 74f), ReturnToMenu);

        UiKit.Text(panel.transform, "TAB to resume  ·  R to restart the run",
                   24f, UiKit.Muted, TextAlignmentOptions.Center,
                   new Vector2(0f, -180f), new Vector2(900f, 34f));

        if (DevMode.Enabled) BuildTuner();

        // The backdrop is on the canvas, so hiding the panel alone would leave the screen
        // dimmed. Hide the whole canvas instead.
        root.gameObject.SetActive(false);
    }

    /// <summary>
    /// Live car tuning, shown only in dev mode.
    /// </summary>
    /// <remarks>
    /// Exists because the target device is a school Chromebook running a Web build: there is no
    /// Inspector, no console and no way to try a spring rate without going home. Every field
    /// here is one that has actually needed tuning by feel rather than by arithmetic.
    ///
    /// Values are read from the live CarController when the menu opens and written straight
    /// back, so a change takes effect the moment you resume. They are NOT persisted — a reload
    /// restores the prefab, which is what makes it safe to experiment.
    /// </remarks>
    static readonly (string label, System.Func<CarController, float> get,
                     System.Action<CarController, float> set, float step)[] Tunables =
    {
        ("Top speed",    c => c.topSpeed,          (c, v) => c.topSpeed = v,          2f),
        ("Engine power", c => c.enginePower,       (c, v) => c.enginePower = v,       200f),
        ("Grip front",   c => c.frontGrip,         (c, v) => c.frontGrip = Mathf.Clamp01(v),  0.05f),
        ("Grip rear",    c => c.rearGrip,          (c, v) => c.rearGrip = Mathf.Clamp01(v),   0.05f),
        ("Downforce",    c => c.downforce,         (c, v) => c.downforce = Mathf.Max(0f, v),  0.1f),
        ("Spring",       c => c.springStrength,    (c, v) => c.springStrength = v,    500f),
        ("Damper",       c => c.damperStrength,    (c, v) => c.damperStrength = v,    200f),
        ("Anti-roll",    c => c.antiRollStrength,  (c, v) => c.antiRollStrength = v,  500f),
        ("Steer angle",  c => c.maxSteerAngle,     (c, v) => c.maxSteerAngle = v,     2f),
    };

    readonly List<TextMeshProUGUI> tunerValues = new List<TextMeshProUGUI>();

    void BuildTuner()
    {
        UiKit.Text(panel.transform, "DEV — CAR TUNING", 26f, UiKit.Accent,
                   TextAlignmentOptions.Center, new Vector2(560f, 300f), new Vector2(560f, 34f))
             .fontStyle = FontStyles.Bold;

        tunerValues.Clear();

        for (int i = 0; i < Tunables.Length; i++)
        {
            int index = i;
            float y = 240f - i * 56f;

            UiKit.Text(panel.transform, Tunables[i].label, 24f, UiKit.Ink,
                       TextAlignmentOptions.Right, new Vector2(400f, y), new Vector2(240f, 34f));

            UiKit.Button(panel.transform, "-", new Vector2(560f, y), new Vector2(52f, 44f),
                         () => Nudge(index, -1f));

            tunerValues.Add(UiKit.Text(panel.transform, "", 24f, UiKit.Accent,
                                       TextAlignmentOptions.Center, new Vector2(650f, y),
                                       new Vector2(130f, 34f)));

            UiKit.Button(panel.transform, "+", new Vector2(740f, y), new Vector2(52f, 44f),
                         () => Nudge(index, 1f));
        }

        UiKit.Text(panel.transform, "Not saved. Restarting restores the prefab.",
                   20f, UiKit.Muted, TextAlignmentOptions.Center,
                   new Vector2(560f, -260f), new Vector2(560f, 30f));
    }

    void Nudge(int index, float direction)
    {
        CarController target = PlayerCar.Current != null ? PlayerCar.Current.Controller : null;
        if (target == null) return;

        var t = Tunables[index];
        t.set(target, t.get(target) + t.step * direction);
        RefreshTuner();
    }

    void RefreshTuner()
    {
        if (tunerValues.Count == 0) return;

        CarController target = PlayerCar.Current != null ? PlayerCar.Current.Controller : null;
        if (target == null) return;

        for (int i = 0; i < tunerValues.Count && i < Tunables.Length; i++)
            tunerValues[i].text = Tunables[i].get(target).ToString("0.##");
    }

    void OnEnable()
    {
        armedAt = Time.unscaledTime + inputLockout;
    }

    void Start()
    {
        if (PlayerCar.Current != null) car = PlayerCar.Current.GetComponent<CarInput>();
    }

    void Update()
    {
        if (RestartOverlay.InProgress) return;
        if (Time.unscaledTime < armedAt) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard[pauseKey].wasPressedThisFrame)
        {
            if (Paused) Resume();
            else Pause();
        }
    }

    void Pause()
    {
        Paused = true;
        Time.timeScale = 0f;
        root.gameObject.SetActive(true);
        RefreshTuner();
        SetCarInput(false);
    }

    public void Resume()
    {
        Paused = false;
        Time.timeScale = 1f;
        root.gameObject.SetActive(false);
        SetCarInput(true);
    }

    void ReturnToMenu()
    {
        // Resume first. Loading a scene while timeScale is 0 hands the next scene a frozen
        // clock, and the symptom -- a menu that renders but ignores every click -- looks
        // nothing like its cause.
        Resume();
        RestartOverlay.Begin(menuScene, 0.4f);
    }

    void SetCarInput(bool accept)
    {
        // Physics is already stopped by timeScale, so this is not what makes the car stop. It
        // stops steering and throttle being *sampled* while the menu is up, which otherwise
        // gets applied in a lump on the first frame after resuming.
        if (car == null && PlayerCar.Current != null)
            car = PlayerCar.Current.GetComponent<CarInput>();

        if (car != null) car.acceptInput = accept;
    }

    void OnDestroy()
    {
        // Never leave the game frozen because this object went away while paused.
        if (Paused)
        {
            Paused = false;
            Time.timeScale = 1f;
        }
    }
}
