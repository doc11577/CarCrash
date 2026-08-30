using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the gear counter, the combo multiplier and the floating score popups.
///
/// The whole HUD is built in code at Awake. That is deliberate: a hand-built Canvas is a
/// dozen GameObjects with anchors, pivots and font sizes that all have to be right, and
/// every one of them is a thing that can be silently wrong in a scene file. Building it
/// here makes the Editor wiring a single component add, and the layout reviewable as code.
/// </summary>
/// <remarks>
/// Cost, because it is a permanent on-screen element on a Chromebook budget:
///
/// TWO canvases, not one. A uGUI canvas rebuilds its whole batch when any element on it
/// changes, so mixing a counter that updates a few times a second with popups that move
/// every frame would rebuild the lot every frame. The static canvas holds the label and the
/// counter; the dynamic canvas holds the multiplier, the combo bar and the popups.
///
/// No GraphicRaycaster on either — nothing here is clickable, and a raycaster costs a
/// hit test per pointer event for nothing.
///
/// Every text uses the one default TMP font asset, so both canvases collapse to roughly one
/// draw call each. Popups are pooled to a fixed array and never allocate after Awake.
/// The counter only pushes a new string when its INTEGER changes, not every frame.
///
/// Popups carry no outline. TMP outlines are a material property, so switching one on would
/// instantiate a material per popup and turn one draw call into eight.
/// </remarks>
public class ScoreHud : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Leave empty to use RunScore.Instance.")]
    public RunScore score;

    [Header("Style")]
    [Tooltip("Colour of the gear counter. Matches the loading bar so the game reads as one thing.")]
    public Color accent = new Color(1f, 0.78f, 0.15f, 1f);

    [Tooltip("Colour of the small GEARS label above the counter.")]
    public Color muted = new Color(1f, 1f, 1f, 0.55f);

    [Tooltip("Font size of the counter, at a 1920x1080 reference resolution.")]
    public float counterSize = 84f;

    [Header("Counter feel")]
    [Tooltip("The displayed number chases the real one instead of snapping, so a big hit " +
             "spins the counter up. Minimum gears per second while catching up.")]
    public float countUpFloor = 40f;

    [Tooltip("Extra catch-up speed per gear of gap. Makes a large jump resolve quickly and a " +
             "small one tick.")]
    public float countUpGain = 6f;

    [Header("Popups")]
    [Tooltip("How many popups can be on screen at once. They are pooled to this count at " +
             "Awake and reused round-robin, so a pile-up recycles the oldest rather than " +
             "instantiating.")]
    public int popupCount = 8;

    [Tooltip("Seconds a popup lives.")]
    public float popupLife = 1.1f;

    [Tooltip("Metres per second a popup drifts upward in WORLD space, so it stays attached to " +
             "the place the hit happened as the camera moves.")]
    public float popupRise = 1.4f;

    [Tooltip("Font size of a popup, at a 1920x1080 reference resolution.")]
    public float popupSize = 34f;

    struct Floater
    {
        public TextMeshProUGUI text;
        public RectTransform rect;
        public Vector3 world;
        public float bornAt;
        public bool live;

        /// <summary>Mirrors text.enabled, so the per-frame loop only touches it on a change.</summary>
        public bool shown;
    }

    /// <summary>Width of the combo bar at the reference resolution.</summary>
    const float ComboBarWidth = 220f;

    RectTransform dynamicRect;
    TextMeshProUGUI counter;
    TextMeshProUGUI multiplier;
    RectTransform comboFill;
    Image comboBar;
    Image comboTrack;

    Floater[] floaters;
    int nextFloater;

    Camera view;
    float displayed;
    int lastShown = -1;
    int lastMultiplierTenths = -1;
    bool comboVisible;

    void Awake()
    {
        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogError(
                "ScoreHud: TextMeshPro has no default font asset, so nothing will draw. " +
                "Run Window > TextMeshPro > Import TMP Essential Resources once, then press " +
                "play again.", this);
        }

        RectTransform staticRect = BuildCanvas("HUD Static", 0);
        dynamicRect = BuildCanvas("HUD Dynamic", 1);

        BuildCounter(staticRect);
        BuildCombo(dynamicRect);
        BuildFloaters(dynamicRect);
    }

    void Start()
    {
        if (score == null) score = RunScore.Instance;
        if (score == null)
        {
            Debug.LogWarning("ScoreHud found no RunScore — the counter will sit at 0.", this);
            return;
        }

        score.Scored += OnScored;
    }

    void OnDestroy()
    {
        if (score != null) score.Scored -= OnScored;
    }

    // ---- construction -------------------------------------------------------------------

    RectTransform BuildCanvas(string name, int order)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);

        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;

        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

        // 0.5 splits the difference between matching width and height, which keeps the HUD a
        // sane size across the range of Chromebook aspect ratios rather than blowing up on
        // one of them.
        scaler.matchWidthOrHeight = 0.5f;

        return (RectTransform)go.transform;
    }

    void BuildCounter(RectTransform parent)
    {
        TextMeshProUGUI label = MakeText(parent, "GearsLabel", 26f, muted);
        Anchor(label.rectTransform, new Vector2(-32f, -24f), new Vector2(340f, 30f));
        label.alignment = TextAlignmentOptions.TopRight;
        label.text = "GEARS";
        label.characterSpacing = 8f;

        counter = MakeText(parent, "GearsCounter", counterSize, accent);
        Anchor(counter.rectTransform, new Vector2(-32f, -50f), new Vector2(340f, 96f));
        counter.alignment = TextAlignmentOptions.TopRight;
        counter.fontStyle = FontStyles.Bold;
        counter.text = "0";
    }

    void BuildCombo(RectTransform parent)
    {
        multiplier = MakeText(parent, "Multiplier", 40f, accent);
        Anchor(multiplier.rectTransform, new Vector2(-32f, -148f), new Vector2(340f, 46f));
        multiplier.alignment = TextAlignmentOptions.TopRight;
        multiplier.fontStyle = FontStyles.Bold;
        multiplier.enabled = false;

        // A bare Image with no sprite draws a plain tinted quad, so the bar needs no art.
        comboTrack = MakeBar(parent, "ComboTrack", new Color(1f, 1f, 1f, 0.18f));
        Anchor(comboTrack.rectTransform, new Vector2(-32f, -198f), new Vector2(ComboBarWidth, 7f));
        comboTrack.enabled = false;

        comboBar = MakeBar(parent, "ComboFill", accent);
        comboFill = comboBar.rectTransform;
        comboFill.SetParent(comboTrack.rectTransform, false);

        // Disabling the track Image does NOT hide the fill: it is a child, and a disabled
        // Graphic stops drawing itself only. Without this the bar sits on screen at full width
        // from the first frame, before any combo exists.
        comboBar.enabled = false;

        // Anchored to the RIGHT edge of the track and shrunk by width, so it drains toward the
        // corner it sits in rather than away from it.
        comboFill.anchorMin = new Vector2(1f, 0f);
        comboFill.anchorMax = new Vector2(1f, 1f);
        comboFill.pivot = new Vector2(1f, 0.5f);
        comboFill.anchoredPosition = Vector2.zero;
        comboFill.sizeDelta = new Vector2(ComboBarWidth, 0f);
    }

    void BuildFloaters(RectTransform parent)
    {
        floaters = new Floater[Mathf.Max(1, popupCount)];

        for (int i = 0; i < floaters.Length; i++)
        {
            TextMeshProUGUI text = MakeText(parent, "Popup" + i, popupSize, Color.white);
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.enabled = false;

            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(360f, 48f);

            floaters[i].text = text;
            floaters[i].rect = rect;
        }
    }

    static TextMeshProUGUI MakeText(RectTransform parent, string name, float size, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = colour;
        text.raycastTarget = false;
        text.richText = false;

        // The counter can spin to four digits and a popup can be a long part name. Neither
        // should ever reflow onto a second line mid-crash.
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;

        return text;
    }

    static Image MakeBar(RectTransform parent, string name, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>Pin a rect to the top-right corner at a reference-resolution offset.</summary>
    static void Anchor(RectTransform rect, Vector2 offset, Vector2 size)
    {
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
    }

    // ---- per frame ----------------------------------------------------------------------

    void Update()
    {
        if (score == null) return;

        UpdateCounter();
        UpdateCombo();
        UpdateFloaters();
    }

    void UpdateCounter()
    {
        int target = score.Gears;

        // Chase rather than snap, at a speed proportional to the gap, so a big payout spins
        // the counter and a small one ticks.
        float speed = Mathf.Max(countUpFloor, Mathf.Abs(target - displayed) * countUpGain);
        displayed = Mathf.MoveTowards(displayed, target, speed * Time.unscaledDeltaTime);

        int shown = Mathf.FloorToInt(displayed);
        if (shown == lastShown) return;

        lastShown = shown;

        // SetText with a format argument does not allocate a string, unlike assigning
        // shown.ToString(). This runs on most frames of a crash, so it is worth the overload.
        counter.SetText("{0}", shown);
    }

    void UpdateCombo()
    {
        float m = score.Multiplier;
        bool show = m > 1.01f;

        if (show != comboVisible)
        {
            comboVisible = show;
            multiplier.enabled = show;
            comboTrack.enabled = show;
            comboBar.enabled = show;
        }

        if (!show) return;

        int tenths = Mathf.RoundToInt(m * 10f);
        if (tenths != lastMultiplierTenths)
        {
            lastMultiplierTenths = tenths;
            multiplier.SetText("x{0:1}", m);
        }

        comboFill.sizeDelta = new Vector2(ComboBarWidth * score.ComboFraction, 0f);
    }

    void UpdateFloaters()
    {
        if (view == null)
        {
            view = Camera.main;
            if (view == null) return;
        }

        float now = Time.unscaledTime;

        for (int i = 0; i < floaters.Length; i++)
        {
            if (!floaters[i].live) continue;

            float age = now - floaters[i].bornAt;
            if (age >= popupLife)
            {
                floaters[i].live = false;
                Show(i, false);
                continue;
            }

            float t = age / popupLife;

            // Rise in WORLD space, so the popup stays pinned to the spot on the road where
            // the hit happened while the camera keeps moving past it.
            Vector3 world = floaters[i].world + Vector3.up * (popupRise * age);
            Vector3 screen = view.WorldToScreenPoint(world);

            // Behind the camera. WorldToScreenPoint mirrors the point when z is negative, so
            // without this a hit you have already driven past reappears on the wrong side.
            if (screen.z <= 0f)
            {
                Show(i, false);
                continue;
            }

            Show(i, true);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                dynamicRect, screen, null, out Vector2 local);
            floaters[i].rect.anchoredPosition = local;

            // Hold full opacity for the first part of the life, then fade. Fading from the
            // first frame makes a popup unreadable for most of the time it is on screen.
            floaters[i].text.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, t));
        }
    }

    /// <summary>
    /// Toggle a popup, skipping the call when it is already in that state. Behaviour.enabled
    /// is a native property set, and this runs for every live popup every frame.
    /// </summary>
    void Show(int index, bool visible)
    {
        if (floaters[index].shown == visible) return;

        floaters[index].shown = visible;
        floaters[index].text.enabled = visible;
    }

    void OnScored(string text, Vector3 world)
    {
        if (floaters == null || floaters.Length == 0) return;

        // Round-robin: at the cap the oldest popup is recycled rather than a ninth created.
        int slot = nextFloater;
        nextFloater = (nextFloater + 1) % floaters.Length;

        floaters[slot].world = world;
        floaters[slot].bornAt = Time.unscaledTime;
        floaters[slot].live = true;
        floaters[slot].text.text = text;
        floaters[slot].text.alpha = 1f;
        Show(slot, true);
    }
}
