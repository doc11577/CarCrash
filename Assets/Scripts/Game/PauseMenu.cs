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
    TextMeshProUGUI fullscreenLabel;
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

        fullscreenLabel = UiKit.Button(panel.transform, Fullscreen.Label,
                                       new Vector2(0f, -142f), new Vector2(420f, 58f),
                                       () =>
                                       {
                                           Fullscreen.Toggle();
                                           if (fullscreenLabel != null)
                                               fullscreenLabel.text = Fullscreen.Label;
                                       })
                                  .GetComponentInChildren<TextMeshProUGUI>();

        UiKit.Text(panel.transform, "TAB to resume  ·  R to restart the run",
                   24f, UiKit.Muted, TextAlignmentOptions.Center,
                   new Vector2(0f, -228f), new Vector2(900f, 34f));

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
    // The table lives in CarTuning, because PlayerCarSpawner needs it too — it has to put
    // saved values back on a freshly spawned car. Two copies would drift silently.
    static CarTuning.Tunable[] Tunables => CarTuning.Tunables;

    /// <summary>
    /// The editable value box for each tunable. See <see cref="BuildTuner"/>.
    /// </summary>
    readonly List<TMP_InputField> tunerFields = new List<TMP_InputField>();

    /// <summary>True while RefreshTuner is writing, so its writes are not read back as edits.</summary>
    bool refreshingTuner;

    void BuildTuner()
    {
        UiKit.Text(panel.transform, "DEV — CAR TUNING", 26f, UiKit.Accent,
                   TextAlignmentOptions.Center, new Vector2(560f, 300f), new Vector2(560f, 34f))
             .fontStyle = FontStyles.Bold;

        tunerFields.Clear();

        for (int i = 0; i < Tunables.Length; i++)
        {
            int index = i;
            float y = 240f - i * 56f;

            UiKit.Text(panel.transform, Tunables[i].label, 24f, UiKit.Ink,
                       TextAlignmentOptions.Right, new Vector2(370f, y), new Vector2(240f, 34f));

            UiKit.Button(panel.transform, "-", new Vector2(530f, y), new Vector2(46f, 44f),
                         () => Nudge(index, -1f), fontSize: 26f);

            // A TYPED field, not a label. Nudging a spring rate from 9000 to 22500 at 500 a
            // click is 27 clicks, and the numbers this project actually needs — a truck's 2.5x
            // spring scaling, an exact downforce — are worked out on paper and then entered.
            // The +/- buttons stay for feeling out a value by ear, which is the other half of
            // what this screen is for.
            TMP_InputField field = UiKit.Field(panel.transform, "", new Vector2(650f, y),
                                               new Vector2(150f, 44f), 24f);
            field.contentType = TMP_InputField.ContentType.DecimalNumber;

            // Committed on Enter or on clicking away, NOT per keystroke. onValueChanged would
            // apply "9" and then "90" while "9000" is still being typed, which briefly gives the
            // car a spring rate of 9 — and at timeScale 0 that is invisible until you resume
            // into a car sitting on its bump stops.
            field.onEndEdit.AddListener(text => Commit(index, text));

            tunerFields.Add(field);

            UiKit.Button(panel.transform, "+", new Vector2(770f, y), new Vector2(46f, 44f),
                         () => Nudge(index, 1f), fontSize: 26f);
        }

        UiKit.Text(panel.transform, "Type a value and press Enter, or nudge with -/+.",
                   20f, UiKit.Muted, TextAlignmentOptions.Center,
                   new Vector2(560f, -240f), new Vector2(560f, 30f));

        UiKit.Text(panel.transform, "Saved per car. RESET PROGRESS clears it.",
                   20f, UiKit.Muted, TextAlignmentOptions.Center,
                   new Vector2(560f, -268f), new Vector2(560f, 30f));
    }

    /// <summary>Apply a typed value, or put the real one back if it was not a number.</summary>
    void Commit(int index, string text)
    {
        if (refreshingTuner) return;

        CarController target = PlayerCar.Current != null ? PlayerCar.Current.Controller : null;
        if (target == null) return;

        // InvariantCulture: TMP's DecimalNumber content type accepts whatever separator the
        // keyboard produces, and a machine set to a comma decimal would otherwise parse "0,85"
        // as 85 — a grip value of 85 rather than 0.85.
        if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out float value))
        {
            Tunables[index].set(target, value);
            Persist(target);
        }

        // Always refresh, so a rejected entry snaps back to the truth rather than sitting there
        // looking as though it took, and so a clamped one shows what it was clamped to.
        RefreshTuner();
    }

    void Nudge(int index, float direction)
    {
        CarController target = PlayerCar.Current != null ? PlayerCar.Current.Controller : null;
        if (target == null) return;

        var t = Tunables[index];
        t.set(target, t.get(target) + t.step * direction);
        Persist(target);
        RefreshTuner();
    }

    /// <summary>Save the whole set, so a restart keeps whatever was being tried.</summary>
    void Persist(CarController target)
    {
        CarTuning.Save(target, GameSelection.CarId);
    }

    void RefreshTuner()
    {
        if (tunerFields.Count == 0) return;

        CarController target = PlayerCar.Current != null ? PlayerCar.Current.Controller : null;
        if (target == null) return;

        // Guarded, because writing .text fires onEndEdit on a focused field — which would call
        // Commit, which calls RefreshTuner, which writes .text again.
        refreshingTuner = true;

        for (int i = 0; i < tunerFields.Count && i < Tunables.Length; i++)
        {
            // Leave the box alone while it is being typed into, or the value snaps back
            // mid-entry the moment anything else refreshes.
            if (tunerFields[i].isFocused) continue;

            tunerFields[i].SetTextWithoutNotify(
                Tunables[i].get(target).ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        refreshingTuner = false;
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

        // TAB moves between fields while typing in the tuner; resuming on it would eject you
        // from the menu mid-edit.
        if (UiKit.Typing()) return;

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
