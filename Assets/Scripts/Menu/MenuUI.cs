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
    const string PatchTitle = "UPDATE 1";

    const string PatchNotes =
        "·  Improved lighting, with skies themed per map\n" +
        "·  New map — Everest, a 70° bombing run down the mountain\n" +
        "·  New car — LCT 3000 box truck, 12 parts to lose\n" +
        "·  Smarter AI — better cornering, obstacle dodging and recovery\n" +
        "·  More AI cars, now drawn from the whole roster\n" +
        "·  Feats — bonus gears for stunts like losing every wheel\n" +
        "·  Reset your progress from the Options screen";

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
    TMP_InputField devField;
    TextMeshProUGUI devStatus;
    TextMeshProUGUI fullscreenLabel;
    TextMeshProUGUI mainStatus;
    UnityEngine.UI.Button resetButton;
    TextMeshProUGUI resetLabel;
    TextMeshProUGUI resetNote;
    bool resetArmed;
    int carIndex;

    void Awake()
    {
        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogError(
                "MenuUI: TextMeshPro has no default font asset, so the menu will be invisible. " +
                "Run Window > TextMeshPro > Import TMP Essential Resources once.", this);
        }

        UiKit.EnsureEventSystem();

        root = UiKit.Screen(transform, "Menu", 0);
        UiKit.Backdrop(root, UiKit.Ground);

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
        CarRoster.Entry[] entries = Entries();

        // The roster GROWS, so the list gets a band rather than a fixed step. At two cars this
        // is identical to the old fixed layout; the third car is what made the blurb, the OWNED
        // line and the CC-BY credit draw straight through the bottom button.
        UiKit.ListBand band = UiKit.Band(top: 209f, bottom: -58f, count: entries.Length,
                                         maxSlot: 96f, padding: 11f, maxHeight: 78f);

        for (int i = 0; i < entries.Length; i++)
        {
            int index = i;
            carButtons.Add(UiKit.Button(page.transform, entries[i].displayName,
                                        new Vector2(0f, band.Centre(i)),
                                        new Vector2(660f, band.height), () => ChooseCar(index),
                                        fontSize: band.fontSize));
        }

        carBlurb = UiKit.Text(page.transform, "", 26f, UiKit.Muted,
                              TextAlignmentOptions.Center, new Vector2(0f, -86f),
                              new Vector2(900f, 36f));

        carStatus = UiKit.Text(page.transform, "", 30f, UiKit.Ink,
                               TextAlignmentOptions.Center, new Vector2(0f, -134f),
                               new Vector2(900f, 40f));

        // Attribution lives here because this is the screen the car is chosen on, and CC-BY
        // requires the credit to be visible in the product, not only in CREDITS.md.
        // Three cars now carry a credit line, so this is load-bearing rather than decorative.
        carCredit = UiKit.Text(page.transform, "", 21f, UiKit.Muted,
                               TextAlignmentOptions.Center, new Vector2(0f, -178f),
                               new Vector2(1100f, 30f));

        // ONE action button that changes meaning, rather than a BUY and a GO sitting side by
        // side with one of them always dead. What you can do with the selected car is never
        // both at once.
        actionButton = UiKit.Button(page.transform, "GO", new Vector2(0f, -258f),
                                    new Vector2(440f, 78f), Act, accent: true);
        actionLabel = actionButton.GetComponentInChildren<TextMeshProUGUI>();

        UiKit.Button(page.transform, "BACK", new Vector2(0f, -380f), new Vector2(280f, 62f),
                     () => Show(Page.Maps));

        return page;
    }

    GameObject BuildOptions()
    {
        GameObject page = NewPage("Options");

        UiKit.Text(page.transform, "OPTIONS", 64f, UiKit.Ink,
                   TextAlignmentOptions.Center, new Vector2(0f, 300f), new Vector2(1200f, 90f))
             .fontStyle = FontStyles.Bold;

        UiKit.Text(page.transform, "Dev mode", 30f, UiKit.Ink,
                   TextAlignmentOptions.Right, new Vector2(-330f, 90f), new Vector2(240f, 46f));

        devField = UiKit.Field(page.transform, "code", new Vector2(-30f, 90f),
                               new Vector2(340f, 56f));
        devField.contentType = TMP_InputField.ContentType.Password;
        devField.onSubmit.AddListener(_ => SubmitDevCode());

        UiKit.Button(page.transform, "SUBMIT", new Vector2(240f, 90f), new Vector2(200f, 56f),
                     SubmitDevCode);

        devStatus = UiKit.Text(page.transform, "", 26f, UiKit.Muted,
                               TextAlignmentOptions.Center, new Vector2(0f, 10f),
                               new Vector2(1000f, 40f));

        UiKit.Button(page.transform, "TURN DEV MODE OFF", new Vector2(0f, -80f),
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
        resetButton = UiKit.Button(page.transform, "", new Vector2(0f, -265f),
                                   new Vector2(440f, 60f), ResetPressed);
        resetLabel = resetButton.GetComponentInChildren<TextMeshProUGUI>();

        resetNote = UiKit.Text(page.transform, "", 22f, UiKit.Muted,
                               TextAlignmentOptions.Center, new Vector2(0f, -322f),
                               new Vector2(1000f, 28f));

        fullscreenLabel = UiKit.Button(page.transform, Fullscreen.Label,
                                       new Vector2(0f, -180f), new Vector2(440f, 60f),
                                       () =>
                                       {
                                           Fullscreen.Toggle();
                                           // Relabelled on the NEXT frame would be
                                           // cleaner, but the browser applies the
                                           // change synchronously enough that reading
                                           // it straight back is right in practice.
                                           RefreshFullscreenLabel();
                                       })
                                  .GetComponentInChildren<TextMeshProUGUI>();

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

    void RefreshFullscreenLabel()
    {
        if (fullscreenLabel != null) fullscreenLabel.text = Fullscreen.Label;
    }

    void RefreshDev()
    {
        if (devStatus == null) return;

        devStatus.color = DevMode.Enabled ? UiKit.Accent : UiKit.Muted;
        devStatus.text = DevMode.Enabled
            ? $"DEV MODE ON  ·  {PlayerWallet.Gears:N0} gears  ·  car tuning on the pause screen"
            : "Off. Enter the code to unlock car tuning and gears.";
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

        // Repaint on arrival rather than only at build time, because a reset can change the
        // wallet, ownership and dev mode while every page already exists.
        if (page == Page.Main) RefreshMain();
        if (page == Page.Cars) RefreshCars();
        if (page != Page.Options) DisarmReset();
    }

    void RefreshMain()
    {
        if (mainStatus == null) return;

        mainStatus.text =
            $"{PlayerWallet.Gears:N0} gears banked   ·   best run {PlayerWallet.BestRun:N0}"
            + (DevMode.Enabled ? "   ·   DEV" : "");
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

        carIndex = 0;
        DisarmReset();
        RefreshDev();
        RefreshCars();
        RefreshMain();

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
        if (carBlurb != null) carBlurb.text = car.blurb;
        if (carCredit != null) carCredit.text = car.credit;

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
