using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The whole front end: main menu, map select, car select, go.
///
/// Three panels on one canvas, built in code at Awake and shown one at a time. Panels rather
/// than scenes because switching them is instant and costs nothing — a scene load per menu step
/// would put a loading bar between every click.
/// </summary>
/// <remarks>
/// Lives in its own scene, so the 100k-triangle course, the player car and the traffic are NOT in
/// memory while the player sits in a menu. On a 512 MB WASM heap that matters more than the
/// convenience of keeping everything in one scene.
///
/// Maps and cars are Inspector lists rather than hard-coded, so adding one is data. Selection is
/// stored by string ID through <see cref="GameSelection"/>, never by list index.
/// </remarks>
[DisallowMultipleComponent]
public class MenuUI : MonoBehaviour
{
    [Serializable]
    public class MapChoice
    {
        public string id = "quarry01";
        public string displayName = "Quarry Descent";

        [Tooltip("Scene to load. Must be in File > Build Profiles > Scene List or the button " +
                 "does nothing and logs an error.")]
        public string sceneName = "Quarry";

        [Tooltip("One line under the name. Facts, not adjectives.")]
        public string blurb = "1,800 m  ·  270 m drop  ·  about 90 seconds";
    }

    /// <summary>
    /// Release notes, shown on the main screen. **Edit these two here, in code, at each release.**
    /// </summary>
    /// <remarks>
    /// Deliberately a const rather than an Inspector field. A public string would be serialized
    /// into MainMenu.unity the moment the component was added, and from then on editing it here
    /// would change nothing on screen — the same trap that left CarInteriorProps poking through
    /// the bonnet with the bug "fixed". Release notes are edited once per release by whoever is
    /// making the release, which is the same person editing the code, so the Inspector buys
    /// nothing and costs a silent failure.
    ///
    /// Keep it short. There is room for about eight lines between the gears line and the tagline
    /// before it collides with them, and nothing here measures that for you.
    /// </remarks>
    const string PatchTitle = "UPDATE 2";

    const string PatchNotes =
        "·  New map — Bullseye\n" +
        "·  New map — The Dam, a canyon run\n" +
        "·  New car — Lamborghini Aventador\n" +
        "·  Falling boulders down the sides of Quarry\n" +
        "·  Airtime — Earn gears for airtime\n" +
        "·  Quality of Life Updates\n" +
        "·  New garage, a speedometer, and save codes";

    [Header("Content")]
    public string title = "CAR CRASH";
    public MapChoice[] maps = new MapChoice[] { new MapChoice() };

    [Tooltip("The shared roster asset. Same one PlayerCarSpawner uses, so the car you buy is " +
             "the car that spawns.")]
    public CarRoster roster;

    [Header("Loading")]
    [Tooltip("Seconds the loading bar is held on screen at minimum, so it does not flash.")]
    public float minimumLoadTime = 0.4f;

    enum Page { Main, Maps, Cars, Options }

    RectTransform root;
    readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
    readonly List<UnityEngine.UI.Button> carButtons = new List<UnityEngine.UI.Button>();

    TextMeshProUGUI carBlurb;
    TextMeshProUGUI carStatus;
    TextMeshProUGUI gearsLine;
    TextMeshProUGUI actionLabel;
    UnityEngine.UI.Button actionButton;
    TextMeshProUGUI carCredit;
    TextMeshProUGUI carName;
    CarPodium podium;
    UnityEngine.UI.Image pageBackdrop;
    GameObject shownPrefab;
    TMP_InputField devField;
    TextMeshProUGUI devStatus;
    TextMeshProUGUI mainStatus;
    UnityEngine.UI.Button resetButton;
    TextMeshProUGUI resetLabel;
    TextMeshProUGUI resetNote;
    TextMeshProUGUI saveHealthLine;
    TextMeshProUGUI saveMessage;
    TMP_InputField saveCodeField;
    TMP_InputField saveLoadField;
    bool resetArmed;
    int carIndex;

    // ---- paint shop -------------------------------------------------------------------------
    GameObject paintPanel;
    UnityEngine.UI.Button paintButton;
    TextMeshProUGUI paintTitle;
    TextMeshProUGUI paintStatus;
    UnityEngine.UI.Button paintAction;
    TextMeshProUGUI paintActionLabel;
    readonly List<UnityEngine.UI.Button> swatches = new List<UnityEngine.UI.Button>();
    readonly List<TextMeshProUGUI> swatchTicks = new List<TextMeshProUGUI>();
    bool paintOpen;
    int paintIndex;

    /// <summary>Metres the podium slides left while the paint shop is open.</summary>
    /// <remarks>
    /// Negative is screen-left — see <see cref="CarPodium.StageOffset"/>. Sized so the car clears
    /// the swatch grid on a 16:9 view without falling off the left edge on a narrow one.
    /// </remarks>
    const float PaintShift = -2.6f;

    void Awake()
    {
        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogError(
                "MenuUI: TextMeshPro has no default font asset, so the menu will be invisible. " +
                "Run Window > TextMeshPro > Import TMP Essential Resources once.", this);
        }

        UiKit.EnsureEventSystem();

        // Optional: without one the garage still works, it just has no car on show.
        podium = FindFirstObjectByType<CarPodium>();

        // Before anything reads the wallet. It counts this launch, and a counter that came back
        // from a previous session is the only real proof that this browser saves at all.
        SaveHealth.Check();

        root = UiKit.Screen(transform, "Menu", 0);

        // Kept as a field so the garage can switch it OFF. It is an opaque full-screen image, so
        // with it on the podium and its backdrop are drawn and then covered up by it — the car
        // would be rendering perfectly and invisibly behind a black rectangle.
        // The flat fallback background. Switched off entirely when a CarPodium is present, since
        // its animated backdrop is drawn in the world BEHIND this and would be covered up.
        pageBackdrop = UiKit.Backdrop(root, UiKit.Ground);
        if (podium != null) pageBackdrop.enabled = false;

        pages[Page.Main] = BuildMain();
        pages[Page.Maps] = BuildMaps();
        pages[Page.Cars] = BuildCars();
        pages[Page.Options] = BuildOptions();

        RestoreSelection();
        RefreshDev();
        Show(Page.Main);
    }

    // ---- pages ----------------------------------------------------------------------------

    GameObject NewPage(string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(root, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return go;
    }

    GameObject BuildMain()
    {
        GameObject page = NewPage("Main");

        UiKit.Text(page.transform, title, 110f, UiKit.Accent,
                   TextAlignmentOptions.Center, new Vector2(0f, 275f), new Vector2(1200f, 140f))
             .fontStyle = FontStyles.Bold;

        UiKit.Button(page.transform, "PLAY", new Vector2(0f, 105f), new Vector2(420f, 74f),
                     () => Show(Page.Maps), accent: true);

        UiKit.Button(page.transform, "OPTIONS", new Vector2(0f, 18f), new Vector2(420f, 66f),
                     () => Show(Page.Options));

        // Banked gears, so the currency is visible from the first screen rather than only
        // appearing once a run has been scored. Kept as a field because the reset button can
        // change it while the menu is already built — this used to be a build-time snapshot,
        // which was invisible only because returning from a run reloads the scene.
        mainStatus = UiKit.Text(page.transform, "", 28f, UiKit.Muted,
                                TextAlignmentOptions.Center,
                                new Vector2(0f, -60f), new Vector2(900f, 40f));

        // Patch notes sit on the screen rather than behind a button. Nobody clicks a
        // "what's new" link, and the whole point is that a returning player sees what changed
        // without going looking for it.
        UiKit.Text(page.transform, PatchTitle, 26f, UiKit.Accent,
                   TextAlignmentOptions.Center, new Vector2(0f, -125f), new Vector2(900f, 32f))
             .fontStyle = FontStyles.Bold;

        UiKit.Text(page.transform, PatchNotes, 20f, UiKit.Muted,
                   TextAlignmentOptions.Top, new Vector2(0f, -250f), new Vector2(1000f, 200f));

        UiKit.Text(page.transform, "Drive downhill. Destroy the car. Earn gears.",
                   26f, UiKit.Muted, TextAlignmentOptions.Center,
                   new Vector2(0f, -395f), new Vector2(1100f, 40f));

        return page;
    }

    GameObject BuildMaps()
    {
        GameObject page = NewPage("Maps");

        UiKit.Text(page.transform, "SELECT MAP", 64f, UiKit.Ink,
                   TextAlignmentOptions.Center, new Vector2(0f, 320f), new Vector2(1200f, 90f))
             .fontStyle = FontStyles.Bold;

        // Same band treatment as the garage. Map select has not clipped yet because there are
        // only two maps, but it is the identical fixed-step layout and would run its fourth
        // map's blurb into the BACK button. Fixed here rather than waiting for it to happen.
        UiKit.ListBand band = UiKit.Band(top: 201f, bottom: -300f, count: maps.Length,
                                         maxSlot: 130f, padding: 48f, maxHeight: 82f);

        for (int i = 0; i < maps.Length; i++)
        {
            MapChoice map = maps[i];
            if (map == null) continue;

            float y = band.Centre(i);

            UiKit.Button(page.transform, map.displayName, new Vector2(0f, y),
                         new Vector2(660f, band.height), () => ChooseMap(map),
                         fontSize: band.fontSize);

            UiKit.Text(page.transform, map.blurb, 24f, UiKit.Muted,
                       TextAlignmentOptions.Center,
                       new Vector2(0f, y - band.height * 0.5f - 18f),
                       new Vector2(660f, 32f));
        }

        UiKit.Button(page.transform, "BACK", new Vector2(0f, -380f), new Vector2(280f, 62f),
                     () => Show(Page.Main));

        return page;
    }

    /// <summary>
    /// The paint shop: a grid of swatches on the right, with the car slid out from under them.
    /// </summary>
    /// <remarks>
    /// Built once and hidden, like every other page here, rather than created on demand — the
    /// swatch count is fixed by the palette and rebuilding nine buttons per open would be work
    /// for nothing.
    ///
    /// **The grid is laid out from the palette length, not from nine hand-placed positions.**
    /// This project has now hit that bug twice (the garage at three cars, the dev tuner at twelve
    /// rows), so a tenth colour must not need a coordinate edit.
    /// </remarks>
    void BuildPaintPanel(Transform parent)
    {
        paintPanel = new GameObject("PaintPanel", typeof(RectTransform));
        paintPanel.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)paintPanel.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        const float x = 430f;

        paintTitle = UiKit.Text(paintPanel.transform, "PAINT", 44f, UiKit.Accent,
                                TextAlignmentOptions.Center, new Vector2(x, 250f),
                                new Vector2(620f, 60f));
        paintTitle.fontStyle = FontStyles.Bold;

        swatches.Clear();
        swatchTicks.Clear();

        const int columns = 3;
        const float cellW = 170f, cellH = 92f, gapX = 16f, gapY = 14f;

        for (int i = 0; i < CarColours.Palette.Length; i++)
        {
            int index = i;
            int col = i % columns;
            int row = i / columns;

            float px = x + (col - (columns - 1) * 0.5f) * (cellW + gapX);
            float py = 150f - row * (cellH + gapY);

            swatches.Add(UiKit.Swatch(paintPanel.transform, new Vector2(px, py),
                                      new Vector2(cellW, cellH),
                                      CarColours.Palette[i].colour,
                                      () => PickPaint(index)));

            // Drawn OVER the swatch: a tick for the paint in use, a padlock for one not owned.
            // On the swatch rather than beside it, because a caption column would double the
            // width of the grid to say something the swatch can carry itself.
            swatchTicks.Add(UiKit.Text(paintPanel.transform, "", 34f, UiKit.Ink,
                                       TextAlignmentOptions.Center, new Vector2(px, py),
                                       new Vector2(cellW, cellH)));
        }

        float gridBottom = 150f - ((CarColours.Palette.Length - 1) / columns) * (cellH + gapY)
                           - cellH * 0.5f;

        paintStatus = UiKit.Text(paintPanel.transform, "", 28f, UiKit.Ink,
                                 TextAlignmentOptions.Center,
                                 new Vector2(x, gridBottom - 40f), new Vector2(620f, 40f));

        paintAction = UiKit.Button(paintPanel.transform, "SELECT",
                                   new Vector2(x, gridBottom - 100f), new Vector2(340f, 66f),
                                   PaintAct, accent: true);
        paintActionLabel = paintAction.GetComponentInChildren<TextMeshProUGUI>();

        UiKit.Button(paintPanel.transform, "DONE", new Vector2(x, gridBottom - 176f),
                     new Vector2(240f, 58f), () => SetPaintOpen(false));

        paintPanel.SetActive(false);
    }

    void TogglePaint() => SetPaintOpen(!paintOpen);

    /// <summary>Open or close the paint shop, sliding the podium out of the way.</summary>
    void SetPaintOpen(bool open)
    {
        paintOpen = open;

        if (paintPanel != null) paintPanel.SetActive(open);
        if (podium != null) podium.StageOffset = open ? PaintShift : 0f;

        // The car's own controls have no meaning while the paint shop is up, and leaving them
        // live invites buying a car while looking at swatches.
        if (actionButton != null) actionButton.gameObject.SetActive(!open);
        if (paintButton != null) paintButton.gameObject.SetActive(!open);

        if (open)
        {
            CarRoster.Entry car = Selected();
            paintIndex = IndexOfPaint(car != null ? CarColours.For(car.id).id
                                                  : CarColours.DefaultId);
            RefreshPaint();
        }
    }

    static int IndexOfPaint(string paintId)
    {
        for (int i = 0; i < CarColours.Palette.Length; i++)
            if (CarColours.Palette[i].id == paintId) return i;
        return 0;
    }

    /// <summary>Highlight a swatch. Selecting or buying is the action button's job.</summary>
    /// <remarks>
    /// Deliberately NOT "click a swatch to buy it". Phantom black is 500,000 gears — roughly a
    /// whole progression — and a single misclick spending that would be indefensible. Owned
    /// paints could safely apply on click, but having one rule for all nine is easier to trust
    /// than a rule that changes depending on what you can afford.
    /// </remarks>
    void PickPaint(int index)
    {
        paintIndex = Mathf.Clamp(index, 0, CarColours.Palette.Length - 1);
        RefreshPaint();
    }

    /// <summary>Select the highlighted paint, or buy it if it is not owned yet.</summary>
    void PaintAct()
    {
        CarRoster.Entry car = Selected();
        if (car == null) return;

        CarColours.Paint paint = CarColours.Palette[paintIndex];

        if (!paint.Owned)
        {
            if (!CarColours.Buy(paint.id))
            {
                paintStatus.color = UiKit.Accent;
                paintStatus.text = $"{paint.price - PlayerWallet.Gears:N0} gears short";
                return;
            }
        }

        CarColours.Choose(car.id, paint.id);
        RefreshCars();
        RefreshPaint();
    }

    /// <summary>Repaint the swatch grid, the status line and the action button.</summary>
    void RefreshPaint()
    {
        if (paintPanel == null || !paintOpen) return;

        CarRoster.Entry car = Selected();
        if (car == null) return;

        string worn = CarColours.For(car.id).id;

        for (int i = 0; i < swatches.Count && i < CarColours.Palette.Length; i++)
        {
            CarColours.Paint p = CarColours.Palette[i];

            // A tick for the paint in use, a padlock for one not bought, and the highlighted
            // one gets brackets — three states told apart without colour, which matters on a
            // grid where colour is the content.
            string mark = !p.Owned ? "●" : p.id == worn ? "✓" : "";
            if (i == paintIndex) mark = mark.Length > 0 ? "[" + mark + "]" : "[  ]";

            swatchTicks[i].text = mark;

            // Readable against the swatch it sits on. Luminance rather than a per-colour flag,
            // so a tenth paint needs no extra decision.
            float lum = p.colour.r * 0.299f + p.colour.g * 0.587f + p.colour.b * 0.114f;
            swatchTicks[i].color = lum > 0.5f ? new Color(0.08f, 0.07f, 0.09f) : UiKit.Ink;
        }

        CarColours.Paint chosen = CarColours.Palette[paintIndex];

        if (!chosen.Owned)
        {
            paintStatus.color = UiKit.Accent;
            paintStatus.text = $"{chosen.displayName}  ·  {chosen.price:N0} gears";
            paintActionLabel.text = "BUY";
        }
        else if (chosen.id == worn)
        {
            paintStatus.color = UiKit.Muted;
            paintStatus.text = $"{chosen.displayName}  ·  in use";
            paintActionLabel.text = "IN USE";
        }
        else
        {
            paintStatus.color = UiKit.Ink;
            paintStatus.text = $"{chosen.displayName}  ·  owned";
            paintActionLabel.text = "SELECT";
        }

        // Preview on the actual car, which is the only way to judge a paint: these colours
        // MULTIPLY a near-white body texture, so a swatch is always brighter than the car.
        if (podium != null) podium.Preview(chosen.colour);

        if (gearsLine != null) gearsLine.text = $"{PlayerWallet.Gears:N0} gears";
    }

    GameObject BuildCars()
    {
        GameObject page = NewPage("Cars");

        UiKit.Text(page.transform, "GARAGE", 64f, UiKit.Ink,
                   TextAlignmentOptions.Center, new Vector2(0f, 330f), new Vector2(1200f, 90f))
             .fontStyle = FontStyles.Bold;

        gearsLine = UiKit.Text(page.transform, "", 28f, UiKit.Accent,
                               TextAlignmentOptions.Center, new Vector2(0f, 268f),
                               new Vector2(900f, 40f));

        carButtons.Clear();

        // ONE car at a time on a podium, cycled with arrows, rather than a list of every car.
        // The list was fine at two cars and was already compressing itself at three; a carousel
        // does not care how big the roster gets, and it is what the reference game does.
        //
        // No backdrop panel behind any of this: the podium draws the background in the WORLD,
        // and a Screen Space Overlay canvas is composited on top of it for free.
        carName = UiKit.Text(page.transform, "", 62f, UiKit.Ink,
                             TextAlignmentOptions.Center, new Vector2(0f, -120f),
                             new Vector2(1100f, 80f));
        carName.fontStyle = FontStyles.Bold;

        UiKit.Button(page.transform, "<", new Vector2(-470f, 30f), new Vector2(110f, 110f),
                     () => Cycle(-1), fontSize: 54f);

        UiKit.Button(page.transform, ">", new Vector2(470f, 30f), new Vector2(110f, 110f),
                     () => Cycle(1), fontSize: 54f);

        carBlurb = UiKit.Text(page.transform, "", 26f, UiKit.Muted,
                              TextAlignmentOptions.Center, new Vector2(0f, -192f),
                              new Vector2(900f, 36f));

        carStatus = UiKit.Text(page.transform, "", 30f, UiKit.Ink,
                               TextAlignmentOptions.Center, new Vector2(0f, -238f),
                               new Vector2(900f, 40f));

        // Attribution lives here because this is the screen the car is chosen on, and CC-BY
        // requires the credit to be visible in the product, not only in CREDITS.md.
        // Three cars now carry a credit line, so this is load-bearing rather than decorative.
        carCredit = UiKit.Text(page.transform, "", 21f, UiKit.Muted,
                               TextAlignmentOptions.Center, new Vector2(0f, -372f),
                               new Vector2(1100f, 30f));

        // ONE action button that changes meaning, rather than a BUY and a GO sitting side by
        // side with one of them always dead. What you can do with the selected car is never
        // both at once.
        //
        // PAINT sits beside it rather than replacing it, because painting is not an alternative
        // to going — it is something you do and then go. It only appears for a car you own,
        // since there is nothing to paint otherwise.
        actionButton = UiKit.Button(page.transform, "GO", new Vector2(-120f, -310f),
                                    new Vector2(400f, 78f), Act, accent: true);
        actionLabel = actionButton.GetComponentInChildren<TextMeshProUGUI>();

        paintButton = UiKit.Button(page.transform, "PAINT", new Vector2(190f, -310f),
                                   new Vector2(220f, 78f), TogglePaint);

        BuildPaintPanel(page.transform);

        // Lower than every other page's BACK: the garage is the only screen with a name, a
        // blurb, a status line, an action button AND a licence credit stacked under the podium.
        UiKit.Button(page.transform, "BACK", new Vector2(0f, -440f), new Vector2(280f, 62f),
                     () => Show(Page.Maps));

        return page;
    }

    GameObject BuildOptions()
    {
        GameObject page = NewPage("Options");

        UiKit.Text(page.transform, "OPTIONS", 64f, UiKit.Ink,
                   TextAlignmentOptions.Center, new Vector2(0f, 455f), new Vector2(1200f, 90f))
             .fontStyle = FontStyles.Bold;

        // ---- saving ----------------------------------------------------------------------
        // The game already saves to IndexedDB through PlayerPrefs. This block exists because
        // that can fail SILENTLY inside a sandboxed Google Sites iframe, and because a code is
        // the only way to carry progress from the school Chromebook to another machine.
        UiKit.Text(page.transform, "SAVING", 26f, UiKit.Accent,
                   TextAlignmentOptions.Center, new Vector2(0f, 370f), new Vector2(900f, 30f))
             .fontStyle = FontStyles.Bold;

        saveHealthLine = UiKit.Text(page.transform, "", 24f, UiKit.Muted,
                                    TextAlignmentOptions.Center, new Vector2(0f, 332f),
                                    new Vector2(1100f, 28f));

        UiKit.Text(page.transform, "Your save code — select it and press Ctrl+C to keep it",
                   22f, UiKit.Muted, TextAlignmentOptions.Center,
                   new Vector2(0f, 296f), new Vector2(1100f, 26f));

        // A read-only field rather than a label, because a field can be selected and copied.
        // Clipboard writes from script are unreliable in a sandboxed cross-origin iframe, so
        // Ctrl+C on a real selection is the path that always works; COPY is a convenience that
        // is allowed to fail.
        saveCodeField = UiKit.Field(page.transform, "", new Vector2(-70f, 250f),
                                    new Vector2(620f, 50f), 22f);
        saveCodeField.readOnly = true;

        UiKit.Button(page.transform, "COPY", new Vector2(300f, 250f), new Vector2(160f, 50f),
                     CopySaveCode, fontSize: 24f);

        saveLoadField = UiKit.Field(page.transform, "paste a save code here",
                                    new Vector2(-70f, 190f), new Vector2(620f, 50f), 22f);
        saveLoadField.onSubmit.AddListener(_ => LoadSaveCode());

        UiKit.Button(page.transform, "LOAD", new Vector2(300f, 190f), new Vector2(160f, 50f),
                     LoadSaveCode, fontSize: 24f);

        saveMessage = UiKit.Text(page.transform, "", 22f, UiKit.Muted,
                                 TextAlignmentOptions.Center, new Vector2(0f, 145f),
                                 new Vector2(1100f, 26f));

        // ---- dev mode --------------------------------------------------------------------
        UiKit.Text(page.transform, "Dev mode", 30f, UiKit.Ink,
                   TextAlignmentOptions.Right, new Vector2(-330f, 60f), new Vector2(240f, 46f));

        devField = UiKit.Field(page.transform, "code", new Vector2(-30f, 60f),
                               new Vector2(340f, 56f));
        devField.contentType = TMP_InputField.ContentType.Password;
        devField.onSubmit.AddListener(_ => SubmitDevCode());

        UiKit.Button(page.transform, "SUBMIT", new Vector2(240f, 60f), new Vector2(200f, 56f),
                     SubmitDevCode);

        devStatus = UiKit.Text(page.transform, "", 26f, UiKit.Muted,
                               TextAlignmentOptions.Center, new Vector2(0f, 0f),
                               new Vector2(1000f, 40f));

        UiKit.Button(page.transform, "TURN DEV MODE OFF", new Vector2(0f, -70f),
                     new Vector2(440f, 60f), () =>
                     {
                         DevMode.Disable();
                         RefreshDev();
                     });

        // Erasing everything is one of the few genuinely irreversible things in the game, so it
        // takes two presses. The button ARMS on the first and fires on the second, rather than
        // opening a confirm dialog — a dialog is a second canvas, a modal state and somewhere
        // else for input focus to get lost, for a control used about once a year.
        // It disarms whenever the Options page is left, so a stray press cannot sit armed
        // waiting for an unrelated click later.
        resetButton = UiKit.Button(page.transform, "", new Vector2(0f, -235f),
                                   new Vector2(440f, 60f), ResetPressed);
        resetLabel = resetButton.GetComponentInChildren<TextMeshProUGUI>();

        resetNote = UiKit.Text(page.transform, "", 22f, UiKit.Muted,
                               TextAlignmentOptions.Center, new Vector2(0f, -288f),
                               new Vector2(1000f, 28f));

        // No fullscreen button. Removed 2026-09-02 after it was confirmed dead on the live
        // site: the game sits two iframes deep in Google Sites and we do not control the
        // outer one, so if it was not granted fullscreen the request is refused however the
        // game asks. A control that cannot work reads as a bug — the Chromebook fullscreen
        // key does the job and nothing can block it. See Fullscreen.cs, kept but unused.

        UiKit.Button(page.transform, "BACK", new Vector2(0f, -380f), new Vector2(280f, 62f),
                     () => Show(Page.Main));

        return page;
    }

    void SubmitDevCode()
    {
        if (devField == null) return;

        bool ok = DevMode.TryUnlock(devField.text);
        devField.text = "";

        if (!ok)
        {
            devStatus.color = new Color(0.95f, 0.45f, 0.35f);
            devStatus.text = "Not a valid code.";
            return;
        }

        RefreshDev();
        RefreshCars();
    }

    void RefreshDev()
    {
        if (devStatus == null) return;

        devStatus.color = DevMode.Enabled ? UiKit.Accent : UiKit.Muted;
        devStatus.text = DevMode.Enabled
            ? $"DEV MODE ON  ·  {PlayerWallet.Gears:N0} gears  ·  car tuning on the pause screen"
            : "Off. Enter the code to unlock car tuning and gears.";
    }

    /// <summary>The roster entry currently on the podium, or null if the roster is empty.</summary>
    CarRoster.Entry Selected()
    {
        CarRoster.Entry[] entries = Entries();
        if (entries.Length == 0) return null;
        return entries[Mathf.Clamp(carIndex, 0, entries.Length - 1)];
    }

    CarRoster.Entry[] Entries()
    {
        return roster != null && roster.cars != null
            ? System.Array.FindAll(roster.cars, e => e != null)
            : new CarRoster.Entry[0];
    }

    /// <summary>Buy it if it is not owned, drive it if it is.</summary>
    void Act()
    {
        CarRoster.Entry[] entries = Entries();
        if (entries.Length == 0) return;

        CarRoster.Entry car = entries[Mathf.Clamp(carIndex, 0, entries.Length - 1)];

        if (car.Owned)
        {
            StartRun();
            return;
        }

        if (PlayerWallet.Buy(car.id, car.price))
        {
            GameSelection.CarId = car.id;
            RefreshCars();
            return;
        }

        // Not enough gears. Say so rather than doing nothing, or a dead button reads as a bug.
        int short_ = car.price - PlayerWallet.Gears;
        carStatus.text = $"{short_:N0} gears short";
        carStatus.color = new Color(0.95f, 0.45f, 0.35f);
    }

    // ---- flow -----------------------------------------------------------------------------

    void Show(Page page)
    {
        foreach (KeyValuePair<Page, GameObject> entry in pages)
            entry.Value.SetActive(entry.Key == page);

        // The animated backdrop is the background for the WHOLE front end, so only the plinth
        // and the car are hidden off the garage page — never the podium component itself, which
        // owns the backdrop, the lights and the pointer tracking.
        //
        // BEFORE RefreshCars, not after: RefreshCars builds the car into the mount, and doing
        // that while the mount is still switched off from the last visit means the whole car is
        // assembled inside an inactive hierarchy.
        if (podium != null) podium.SetShowcase(page == Page.Cars);

        // The paint shop is a mode WITHIN the garage, so leaving the garage must leave it too —
        // otherwise the podium stays shifted left on a page that has no swatches to justify it.
        if (page != Page.Cars && paintOpen) SetPaintOpen(false);

        // Repaint on arrival rather than only at build time, because a reset can change the
        // wallet, ownership and dev mode while every page already exists.
        if (page == Page.Main) RefreshMain();
        if (page == Page.Cars) RefreshCars();
        if (page == Page.Options) RefreshSave();
        if (page != Page.Options) DisarmReset();
    }

    void RefreshMain()
    {
        if (mainStatus == null) return;

        mainStatus.text =
            $"{PlayerWallet.Gears:N0} gears banked   ·   best run {PlayerWallet.BestRun:N0}"
            + (DevMode.Enabled ? "   ·   DEV" : "");
    }

    /// <summary>
    /// Repaint the saving block. The code has to be regenerated on every visit, because gears,
    /// best run and ownership all change between one look at this screen and the next — a code
    /// captured at build time would quietly hand the player a stale save.
    /// </summary>
    void RefreshSave()
    {
        if (saveHealthLine != null)
        {
            saveHealthLine.color = SaveHealth.ShouldWarn ? UiKit.Accent : UiKit.Muted;
            saveHealthLine.text = SaveHealth.Line;
        }

        if (saveCodeField != null) saveCodeField.text = SaveCode.Export();
    }

    void CopySaveCode()
    {
        if (saveCodeField == null) return;

        // Fails silently in some sandboxed iframes, which is why the field is selectable and the
        // label above it tells the player to use Ctrl+C. Never claim the copy worked.
        GUIUtility.systemCopyBuffer = saveCodeField.text;

        if (saveMessage != null)
        {
            saveMessage.color = UiKit.Muted;
            saveMessage.text = "Copied — if nothing was copied, click the code and press Ctrl+C.";
        }
    }

    void LoadSaveCode()
    {
        if (saveLoadField == null) return;

        bool ok = SaveCode.TryImport(saveLoadField.text, out string message);

        if (saveMessage != null)
        {
            saveMessage.color = ok ? UiKit.Muted : UiKit.Accent;
            saveMessage.text = message;
        }

        if (!ok) return;

        saveLoadField.text = "";
        carIndex = 0;
        RefreshSave();
        RefreshCars();
        RefreshMain();
    }

    /// <summary>First press arms, second press erases. See BuildOptions for why.</summary>
    void ResetPressed()
    {
        if (!resetArmed)
        {
            resetArmed = true;
            UiKit.Tint(resetButton, true);
            if (resetLabel != null) resetLabel.text = "PRESS AGAIN TO ERASE";
            if (resetNote != null)
            {
                resetNote.color = UiKit.Accent;
                resetNote.text = "This cannot be undone. Leave this screen to cancel.";
            }
            return;
        }

        PlayerWallet.ResetAll();

        // Dev mode goes too, or "reset progress" leaves the game in a state no real player can
        // be in. It also makes the reset useful for its main purpose — checking what a new
        // player actually sees. The gear grant only happens at unlock, so leaving dev mode on
        // after a wipe would give a dev-mode player zero gears and no way to get them back
        // short of re-entering the code anyway.
        DevMode.Disable();

        // Tuned cars are progress too — and a tuned car makes "what does a new player see"
        // impossible to answer, which is the main reason this button exists.
        CarTuning.ResetAll();
        CarColours.ResetAll();

        carIndex = 0;
        DisarmReset();
        RefreshDev();
        RefreshCars();
        RefreshMain();
        RefreshSave();

        if (resetNote != null)
        {
            resetNote.color = UiKit.Muted;
            resetNote.text = "Progress erased. Gears, best run, cars and dev mode are back to new.";
        }
    }

    void DisarmReset()
    {
        resetArmed = false;
        UiKit.Tint(resetButton, false);
        if (resetLabel != null) resetLabel.text = "RESET PROGRESS";
        if (resetNote != null)
        {
            resetNote.color = UiKit.Muted;
            resetNote.text = "";
        }
    }

    void RestoreSelection()
    {
        CarRoster.Entry[] entries = Entries();

        carIndex = 0;
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].id == GameSelection.CarId) carIndex = i;

        ChooseCar(carIndex);
    }

    void ChooseMap(MapChoice map)
    {
        GameSelection.MapId = map.id;
        Show(Page.Cars);
    }

    /// <summary>
    /// Step to the next or previous car and sweep the backdrop the way the player clicked.
    /// </summary>
    /// <remarks>
    /// Wraps, so the roster is a loop with no dead arrow at either end. A disabled arrow reads
    /// as a bug, the same reason the action button changes meaning instead of greying out.
    /// </remarks>
    void Cycle(int step)
    {
        CarRoster.Entry[] entries = Entries();
        if (entries.Length == 0) return;

        if (podium != null) podium.Sweep(step);

        // Close the paint shop on a car change. It shows what THIS car is wearing and what is
        // selected for it, so leaving it open across a swap would show one car's paint over
        // another car's body.
        if (paintOpen) SetPaintOpen(false);

        ChooseCar((carIndex + step + entries.Length) % entries.Length);
    }

    void ChooseCar(int index)
    {
        CarRoster.Entry[] entries = Entries();
        if (entries.Length == 0) return;

        carIndex = Mathf.Clamp(index, 0, entries.Length - 1);

        // Only remember a car the player actually owns. Selecting one in the garage to read its
        // price must not silently change what spawns on the next run.
        if (entries[carIndex].Owned) GameSelection.CarId = entries[carIndex].id;

        RefreshCars();
    }

    /// <summary>Repaint the garage from the wallet. Called after a purchase too.</summary>
    void RefreshCars()
    {
        CarRoster.Entry[] entries = Entries();
        if (entries.Length == 0) return;

        CarRoster.Entry car = entries[Mathf.Clamp(carIndex, 0, entries.Length - 1)];

        if (gearsLine != null) gearsLine.text = $"{PlayerWallet.Gears:N0} gears";
        if (carName != null) carName.text = car.displayName;
        if (carBlurb != null) carBlurb.text = car.blurb;
        if (carCredit != null) carCredit.text = car.credit;

        // Only rebuild the model when the car actually changed. RefreshCars runs on every visit
        // to the page and after every purchase, and respawning an 11,000-triangle prefab for a
        // changed price label would be a visible hitch for nothing.
        //
        // **`|| !podium.HasCar` is the important half.** Caching on the prefab alone means that
        // if the car goes missing for any reason, this says "already showing that one" forever
        // and the podium stays empty for the rest of the session. Asking whether a car is
        // actually there costs nothing and makes an empty podium recover on the next arrow
        // press, page visit or purchase.
        if (podium != null && (car.prefab != shownPrefab || !podium.HasCar))
        {
            shownPrefab = car.prefab;
            podium.Show(car.prefab);
        }

        // Wear the paint that was chosen for THIS car. Applied on every refresh rather than only
        // on a rebuild, because the car is not respawned when the paint changes — the whole point
        // of the preview is that the same instance changes colour.
        if (podium != null) podium.Preview(CarColours.For(car.id).colour);

        // Nothing to paint on a car you do not own, and offering it would raise the question of
        // whether buying paint buys the car.
        if (paintButton != null && !paintOpen)
            paintButton.gameObject.SetActive(car.Owned);

        if (carStatus != null)
        {
            carStatus.color = car.Owned ? UiKit.Muted : UiKit.Accent;
            carStatus.text = car.Owned ? "OWNED" : $"{car.price:N0} gears";
        }

        if (actionLabel != null)
            actionLabel.text = car.Owned ? "GO" : $"BUY  ·  {car.price:N0}";

        for (int i = 0; i < carButtons.Count && i < entries.Length; i++)
            UiKit.Tint(carButtons[i], entries[i].id == GameSelection.CarId);
    }

    void StartRun()
    {
        MapChoice map = FindMap(GameSelection.MapId) ?? (maps.Length > 0 ? maps[0] : null);
        if (map == null || string.IsNullOrWhiteSpace(map.sceneName))
        {
            Debug.LogError("MenuUI: no map to load. Check the Maps list has a Scene Name.", this);
            return;
        }

        // Belt and braces: a run started from the menu must never inherit a paused clock.
        Time.timeScale = 1f;
        RestartOverlay.Begin(map.sceneName, minimumLoadTime);
    }

    MapChoice FindMap(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || maps == null) return null;

        foreach (MapChoice map in maps)
            if (map != null && map.id == id) return map;

        return null;
    }
}
