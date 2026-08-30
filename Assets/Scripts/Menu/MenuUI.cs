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
/// Lives in its own scene, so the 71k-triangle course and the car are NOT in memory while the
/// player is sitting in a menu. On a 512 MB WASM heap that matters more than the convenience of
/// keeping everything in one scene.
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
        public string sceneName = "SampleScene";

        [Tooltip("One line under the name. Facts, not adjectives.")]
        public string blurb = "1,800 m  ·  270 m drop  ·  about 90 seconds";
    }

    [Serializable]
    public class CarChoice
    {
        public string id = "e30";
        public string displayName = "BMW E30";
        public string blurb = "1,200 kg  ·  rear wheel drive";

        [Tooltip("Attribution shown on this screen. REQUIRED for anything CC-BY — the E30 " +
                 "licence obliges us to credit ROH3D visibly, not just in CREDITS.md.")]
        public string credit = "BMW E30 1985 by ROH3D  ·  CC BY 4.0";
    }

    [Header("Content")]
    public string title = "CAR CRASH";
    public MapChoice[] maps = new MapChoice[] { new MapChoice() };
    public CarChoice[] cars = new CarChoice[] { new CarChoice() };

    [Header("Loading")]
    [Tooltip("Seconds the loading bar is held on screen at minimum, so it does not flash.")]
    public float minimumLoadTime = 0.4f;

    enum Page { Main, Maps, Cars }

    RectTransform root;
    readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
    readonly List<UnityEngine.UI.Button> carButtons = new List<UnityEngine.UI.Button>();

    TextMeshProUGUI carBlurb;
    TextMeshProUGUI carCredit;
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

        RestoreSelection();
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
                   TextAlignmentOptions.Center, new Vector2(0f, 250f), new Vector2(1200f, 140f))
             .fontStyle = FontStyles.Bold;

        UiKit.Button(page.transform, "PLAY", new Vector2(0f, 40f), new Vector2(420f, 74f),
                     () => Show(Page.Maps), accent: true);

        // Banked gears, so the currency is visible from the first screen rather than only
        // appearing once a run has been scored.
        UiKit.Text(page.transform,
                   $"{PlayerWallet.Gears} gears banked   ·   best run {PlayerWallet.BestRun}",
                   28f, UiKit.Muted, TextAlignmentOptions.Center,
                   new Vector2(0f, -70f), new Vector2(900f, 40f));

        UiKit.Text(page.transform, "Drive downhill. Destroy the car. Earn gears.",
                   26f, UiKit.Muted, TextAlignmentOptions.Center,
                   new Vector2(0f, -370f), new Vector2(1100f, 40f));

        return page;
    }

    GameObject BuildMaps()
    {
        GameObject page = NewPage("Maps");

        UiKit.Text(page.transform, "SELECT MAP", 64f, UiKit.Ink,
                   TextAlignmentOptions.Center, new Vector2(0f, 320f), new Vector2(1200f, 90f))
             .fontStyle = FontStyles.Bold;

        for (int i = 0; i < maps.Length; i++)
        {
            MapChoice map = maps[i];
            if (map == null) continue;

            float y = 160f - i * 130f;

            UiKit.Button(page.transform, map.displayName, new Vector2(0f, y),
                         new Vector2(660f, 82f), () => ChooseMap(map));

            UiKit.Text(page.transform, map.blurb, 24f, UiKit.Muted,
                       TextAlignmentOptions.Center, new Vector2(0f, y - 58f),
                       new Vector2(660f, 32f));
        }

        UiKit.Button(page.transform, "BACK", new Vector2(0f, -380f), new Vector2(280f, 62f),
                     () => Show(Page.Main));

        return page;
    }

    GameObject BuildCars()
    {
        GameObject page = NewPage("Cars");

        UiKit.Text(page.transform, "SELECT CAR", 64f, UiKit.Ink,
                   TextAlignmentOptions.Center, new Vector2(0f, 320f), new Vector2(1200f, 90f))
             .fontStyle = FontStyles.Bold;

        carButtons.Clear();
        for (int i = 0; i < cars.Length; i++)
        {
            int index = i;
            CarChoice car = cars[i];
            if (car == null) continue;

            carButtons.Add(UiKit.Button(page.transform, car.displayName,
                                        new Vector2(0f, 170f - i * 100f),
                                        new Vector2(660f, 82f), () => ChooseCar(index)));
        }

        carBlurb = UiKit.Text(page.transform, "", 26f, UiKit.Muted,
                              TextAlignmentOptions.Center, new Vector2(0f, -40f),
                              new Vector2(900f, 36f));

        // Attribution lives here because this is the screen the car is chosen on, and CC-BY
        // requires the credit to be visible in the product, not only in CREDITS.md.
        carCredit = UiKit.Text(page.transform, "", 22f, UiKit.Muted,
                               TextAlignmentOptions.Center, new Vector2(0f, -100f),
                               new Vector2(1100f, 32f));

        UiKit.Button(page.transform, "GO", new Vector2(0f, -220f), new Vector2(420f, 78f),
                     StartRun, accent: true);

        UiKit.Button(page.transform, "BACK", new Vector2(0f, -380f), new Vector2(280f, 62f),
                     () => Show(Page.Maps));

        return page;
    }

    // ---- flow -----------------------------------------------------------------------------

    void Show(Page page)
    {
        foreach (KeyValuePair<Page, GameObject> entry in pages)
            entry.Value.SetActive(entry.Key == page);
    }

    void RestoreSelection()
    {
        carIndex = 0;
        for (int i = 0; i < cars.Length; i++)
            if (cars[i] != null && cars[i].id == GameSelection.CarId) carIndex = i;

        ChooseCar(carIndex);
    }

    void ChooseMap(MapChoice map)
    {
        GameSelection.MapId = map.id;
        Show(Page.Cars);
    }

    void ChooseCar(int index)
    {
        if (cars == null || cars.Length == 0) return;

        carIndex = Mathf.Clamp(index, 0, cars.Length - 1);
        CarChoice car = cars[carIndex];
        if (car == null) return;

        GameSelection.CarId = car.id;
        if (carBlurb != null) carBlurb.text = car.blurb;
        if (carCredit != null) carCredit.text = car.credit;

        for (int i = 0; i < carButtons.Count; i++)
            UiKit.Tint(carButtons[i], i == carIndex);
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
