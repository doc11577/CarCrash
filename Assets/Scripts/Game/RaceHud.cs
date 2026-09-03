using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Position, lap, the countdown and the result. The race's half of the on-screen furniture.
/// </summary>
/// <remarks>
/// Separate from <see cref="ScoreHud"/> rather than added to it, and that is a deliberate call
/// about which risk is worse. ScoreHud is 500 lines that work, drawn on every run of the game;
/// threading a mode through it means every destruction run is one branch away from a HUD bug.
/// This draws only when a <see cref="RaceDirector"/> exists, so a map with no race cannot be
/// affected by it at all.
///
/// ONE CANVAS, and a static one. Position changes perhaps twenty times in a race and the lap
/// three times, so a uGUI batch rebuild costs nothing here — unlike the popups, which is why
/// ScoreHud has a second canvas for those. The countdown does animate, but for three seconds
/// before anything else is happening.
///
/// Built in code, like every other screen in this game. The reason is unchanged: a hand-built
/// Canvas is a dozen GameObjects whose anchors and pivots can all be silently wrong in a scene
/// file and cannot be reviewed in a diff.
/// </remarks>
[DisallowMultipleComponent]
public class RaceHud : MonoBehaviour
{
    [Tooltip("Size of the P1/8 position readout. The biggest thing on screen after the " +
             "countdown, because it is the one number a racer looks for.")]
    public float positionSize = 62f;

    [Tooltip("Size of the lap line under it.")]
    public float lapSize = 26f;

    [Tooltip("Size of the 3-2-1-GO countdown.")]
    public float countdownSize = 150f;

    [Tooltip("Size of the finish banner and the results list.")]
    public float resultSize = 34f;

    [Tooltip("Seconds the GO! stays up after the green light.")]
    public float goHold = 1.1f;

    RaceDirector race;

    TextMeshProUGUI position;
    TextMeshProUGUI lap;
    TextMeshProUGUI countdown;
    TextMeshProUGUI result;

    readonly StringBuilder builder = new StringBuilder(256);
    float greenAt = -1f;
    int shownPosition = -1;
    int shownLap = -1;

    void Start()
    {
        race = RaceDirector.Instance;

        // No race on this map. The component can sit on the shared GameManager prefab and cost
        // nothing on a destruction map, which is what lets both modes share one scene.
        if (race == null)
        {
            enabled = false;
            return;
        }

        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogError(
                "RaceHud: TextMeshPro has no default font asset, so nothing will draw. Run " +
                "Window > TextMeshPro > Import TMP Essential Resources.", this);
            enabled = false;
            return;
        }

        Build();
    }

    void Build()
    {
        RectTransform screen = UiKit.Screen(transform, "RaceHud", order: 90);

        // TOP LEFT. ScoreHud's GEARS counter is anchored top-right and is on screen during a race
        // as well, so the two overlapped — the position sat straight on top of the gear count.
        // The two HUDs are separate components by design, which means neither can see the other's
        // layout and the corners have to be divided up by hand.
        position = Label(screen, "Position", positionSize, UiKit.Ink,
                         new Vector2(0f, 1f), new Vector2(40f, -34f), TextAlignmentOptions.TopLeft);
        position.fontStyle = FontStyles.Bold;

        lap = Label(screen, "Lap", lapSize, UiKit.Muted,
                    new Vector2(0f, 1f), new Vector2(40f, -34f - positionSize),
                    TextAlignmentOptions.TopLeft);

        countdown = Label(screen, "Countdown", countdownSize, UiKit.Accent,
                          new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), TextAlignmentOptions.Center);
        countdown.fontStyle = FontStyles.Bold;

        result = Label(screen, "Result", resultSize, UiKit.Ink,
                       new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), TextAlignmentOptions.Center);
        result.fontStyle = FontStyles.Bold;
        result.gameObject.SetActive(false);
    }

    static TextMeshProUGUI Label(RectTransform parent, string name, float size, Color colour,
                                 Vector2 anchor, Vector2 offset, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = offset;
        rect.sizeDelta = new Vector2(720f, 400f);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = colour;
        text.alignment = align;

        // Nothing on this canvas is clickable, and a raycast target costs a hit test per pointer
        // event for nothing. Same rule ScoreHud follows.
        text.raycastTarget = false;
        text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;

        return text;
    }

    void Update()
    {
        if (race == null) return;

        switch (race.Phase)
        {
            case RaceDirector.State.Countdown:
                DrawCountdown();
                break;

            case RaceDirector.State.Racing:
                DrawGo();
                DrawStandings();
                break;

            case RaceDirector.State.Finished:
                countdown.text = "";
                DrawResult();
                break;
        }
    }

    void DrawCountdown()
    {
        // Ceil, so the last second reads "1" rather than "0" — a countdown that shows zero
        // before it starts reads as a bug, and every racing game shows 3-2-1 then GO.
        int seconds = Mathf.CeilToInt(race.CountdownLeft);
        countdown.text = seconds > 0 ? seconds.ToString() : "";

        // Swells as each number lands, so the count reads as a beat rather than as text
        // changing. Unscaled would be wrong here: a paused game should pause the countdown.
        float within = race.CountdownLeft - Mathf.Floor(race.CountdownLeft);
        countdown.fontSize = countdownSize * Mathf.Lerp(1.25f, 0.95f, 1f - within);

        greenAt = -1f;
    }

    void DrawGo()
    {
        if (greenAt < 0f) greenAt = Time.time;

        float since = Time.time - greenAt;
        if (since > goHold)
        {
            if (countdown.text.Length > 0) countdown.text = "";
            return;
        }

        countdown.text = "GO!";
        countdown.fontSize = countdownSize * Mathf.Lerp(1.4f, 0.9f, since / goHold);
    }

    void DrawStandings()
    {
        if (race.Player == null) return;

        // Only pushed when the INTEGER changes. Assigning a string every frame allocates and
        // rebuilds the canvas batch on a canvas that is otherwise completely static.
        int place = race.Player.position;
        if (place != shownPosition)
        {
            shownPosition = place;
            position.SetText("P{0}/{1}", place, race.Standings.Count);
        }

        int onLap = race.LapOf(race.Player);
        if (onLap == shownLap) return;

        shownLap = onLap;
        lap.SetText("LAP {0}/{1}", onLap, race.Laps);
    }

    void DrawResult()
    {
        if (result.gameObject.activeSelf) return;

        result.gameObject.SetActive(true);

        builder.Clear();
        builder.Append(race.Player != null ? Ordinal(race.Player.position) : "RACE OVER");
        builder.Append('\n');

        int shown = 0;
        foreach (RaceDirector.Racer racer in race.Standings)
        {
            if (shown++ >= 8) break;

            builder.Append('\n');
            builder.Append(racer.position);
            builder.Append("  ");
            builder.Append(racer.name);
        }

        builder.Append("\n\nTAB for the menu");
        result.text = builder.ToString();
    }

    static string Ordinal(int place) => place switch
    {
        1 => "1ST — WINNER",
        2 => "2ND PLACE",
        3 => "3RD PLACE",
        _ => place + "TH PLACE",
    };
}
