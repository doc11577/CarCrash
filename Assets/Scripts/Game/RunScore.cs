using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns damage into gears. This is the run's score and the game's currency at once —
/// wreck the car, earn gears, spend them in the garage.
///
/// Two things pay out:
///
///   DAMAGE — every qualifying impact on a registered car, scaled by the live combo
///   multiplier. This is the steady drip that makes the counter move constantly.
///
///   PARTS — a lump sum when a panel or wheel actually comes off. This is the payout the
///   player can see happening, so it is deliberately the chunkier of the two.
///
/// The multiplier is what turns "crash a lot" into "keep crashing". It climbs with each
/// fresh impact and expires <see cref="comboWindow"/> seconds after the last one, so a run
/// that keeps hitting things is worth several times a run that hits one wall and stops.
/// </summary>
/// <remarks>
/// Costs nothing per frame beyond one Time.time comparison to expire the combo. All the real
/// work happens inside collision events that were already firing.
/// </remarks>
public class RunScore : MonoBehaviour
{
    /// <summary>The scorer for the current scene. Null between scene loads.</summary>
    public static RunScore Instance { get; private set; }

    [Header("Cars that score")]
    [Tooltip("OPTIONAL OVERRIDE — normally leave this empty. The player's car finds its own way " +
             "here: put a PlayerCar component on it and this resolves itself, including for a " +
             "car the garage spawns mid-session, which a scene reference could never point at. " +
             "Set this only to force scoring onto a specific car for a test.")]
    public CarDamage playerCar;

    [Header("Conversion")]
    [Tooltip("Gears per point of damage. MEASURED: a solid wall hit is about 700 damage, so " +
             "0.02 makes that hit worth roughly 14 gears at x1. The reference game shows " +
             "totals around 200 gears at the end of a long run, which this lands near.")]
    public float gearsPerDamage = 0.02f;

    [Tooltip("Gears per point of a lost part's STARTING health. Derived from health rather " +
             "than a per-part field so there is nothing extra to wire, and health is already " +
             "roughly proportional to how hard the part was to remove: a 160-health bumper " +
             "pays 40, a 60-health mirror pays 15.")]
    public float gearsPerPartHealth = 0.25f;

    [Header("Combo")]
    [Tooltip("Seconds after a fresh impact before the multiplier expires back to x1.")]
    public float comboWindow = 2.5f;

    [Tooltip("Added to the multiplier by each fresh impact.")]
    public float comboPerHit = 0.25f;

    [Tooltip("Ceiling on the multiplier. Uncapped, a long grind would make the last seconds " +
             "of a run worth more than all the rest of it put together.")]
    public float maxMultiplier = 5f;

    [Tooltip("Minimum seconds between multiplier increases. The car has THREE box colliders, " +
             "so hitting one wall can raise three OnCollisionEnter events in the same frame " +
             "and would triple-count the combo without this.")]
    public float comboRearmInterval = 0.2f;

    /// <summary>
    /// A named thing worth doing, that pays out once a run.
    /// </summary>
    /// <remarks>
    /// Matched on a part's GROUP rather than its name. Names differ per car — the E30's mirrors
    /// are `MIrrorL` and `MIrrorR`, the P72's will be something else — so a feat matched on
    /// names would fire on one car and silently do nothing on the next, which is the worst kind
    /// of failure because the feat simply never appears and nothing says why.
    /// </remarks>
    [Serializable]
    public class Feat
    {
        [Tooltip("Stable id. Only used to make sure a feat pays once per run.")]
        public string id = "boat";

        [Tooltip("Shown on screen. Short and shouted works best.")]
        public string title = "BOAT";

        [Tooltip("One line under the title, or leave empty.")]
        public string subtitle = "no wheels";

        [Tooltip("Part group this counts, e.g. \"wheel\" or \"mirror\". Leave EMPTY to count " +
                 "every part on the car, which is how you write a \"lose 8 parts\" feat.")]
        public string group = "wheel";

        [Tooltip("How many must be gone. 0 means ALL of them, which is usually what you want — " +
                 "it keeps working if a car has three mirrors or five wheels.")]
        public int required = 0;

        [Tooltip("Bonus gears. For scale: a solid wall hit is about 14, and a whole run is " +
                 "roughly 200.")]
        public int bonus = 300;

        public Color colour = new Color(0.35f, 0.85f, 1f);
    }

    [Header("Feats")]
    [Tooltip("Named bonuses for doing something particular. Each pays once per run.")]
    public Feat[] feats =
    {
        new Feat { id = "boat", title = "BOAT", subtitle = "no wheels",
                   group = "wheel", required = 0, bonus = 300,
                   colour = new Color(0.35f, 0.85f, 1f) },

        new Feat { id = "mirrors", title = "MIRROR MIRROR", subtitle = "both mirrors gone",
                   group = "mirror", required = 0, bonus = 120,
                   colour = new Color(0.80f, 0.55f, 1f) },
    };

    [Header("Feedback")]
    [Tooltip("Impacts worth fewer gears than this raise no popup. Stops kerbs and scrapes " +
             "filling the screen with +0.")]
    public int minPopupGears = 1;

    /// <summary>Gears earned this run, unrounded. <see cref="Gears"/> is what to display.</summary>
    public float Score { get; private set; }

    /// <summary>Gears earned this run.</summary>
    public int Gears => Mathf.FloorToInt(Score);

    /// <summary>Live combo multiplier. 1 when the combo has expired.</summary>
    public float Multiplier { get; private set; } = 1f;

    /// <summary>How much of the combo window is left, 0-1. For a draining bar on the HUD.</summary>
    public float ComboFraction =>
        comboWindow <= 0f ? 0f : Mathf.Clamp01((comboExpiresAt - Time.time) / comboWindow);

    /// <summary>Raised when something is worth showing on screen: text, and where it happened.</summary>
    public event Action<string, Vector3, Color, bool> Scored;

    [Header("Read-only — watch these in play mode")]
    [Tooltip("Cars currently being scored. 0 means NOTHING can score — the player's car has no " +
             "PlayerCar component on it. 1 is correct until traffic exists.")]
    [SerializeField] int carsScoring;

    [Tooltip("Damage events received. Still 0 after a crash means CarDamage is not raising " +
             "Damaged at all, so the problem is upstream of scoring — read CarDamage.lastImpulse.")]
    [SerializeField] int impactsCounted;

    [Tooltip("Gears the last impact was worth. Compare against the popup that appeared.")]
    [SerializeField] float lastGain;

    [Tooltip("Live score. If this climbs while the on-screen counter does not, the fault is in " +
             "ScoreHud, not here.")]
    [SerializeField] float scoreReadout;

    [Tooltip("Live combo multiplier.")]
    [SerializeField] float multiplierReadout;

    [Tooltip("Feats earned this run. Stuck at 0 after losing every wheel means the parts have " +
             "no matching Group, or the mirrors are not tagged.")]
    [SerializeField] int featsEarned;

    float comboExpiresAt;
    float comboRearmAt;
    bool banked;

    readonly List<CarDamage> scoring = new List<CarDamage>();
    readonly HashSet<string> earnedFeats = new HashSet<string>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Start, not Awake: a car wires up its own parts in Awake. Nothing here depends on
        // that today, but subscribing to a component that has not initialised yet is exactly
        // the ordering bug that will show up once traffic starts spawning mid-run.
        //
        // The player's car normally registers itself from PlayerCar.OnEnable. This picks up the
        // other ordering — a car that was already active before this component's Awake ran, so
        // RunScore.Instance was still null when it tried. Register is idempotent, so a car that
        // has already registered costs nothing here.
        if (playerCar == null && PlayerCar.Current != null) playerCar = PlayerCar.Current.Damage;

        if (playerCar != null) Register(playerCar);

        if (scoring.Count == 0)
        {
            Debug.LogWarning(
                "RunScore has no cars to score. Add a PlayerCar component to the player's car — " +
                "that is what announces it. Nothing will score until it does.", this);
        }
    }

    /// <summary>
    /// Start scoring a car's damage. Safe to call twice with the same car. Traffic should
    /// call this on spawn and <see cref="Unregister"/> before being destroyed or pooled.
    /// </summary>
    public void Register(CarDamage car)
    {
        if (car == null || scoring.Contains(car)) return;

        scoring.Add(car);
        car.Damaged += OnDamaged;
        car.PartLost += OnPartLost;
        carsScoring = scoring.Count;
    }

    /// <summary>Stop scoring a car. Must be called before the car is destroyed or pooled.</summary>
    public void Unregister(CarDamage car)
    {
        if (car == null || !scoring.Remove(car)) return;

        car.Damaged -= OnDamaged;
        car.PartLost -= OnPartLost;
        carsScoring = scoring.Count;
    }

    void Update()
    {
        if (Multiplier > 1f && Time.time >= comboExpiresAt) Multiplier = 1f;

        scoreReadout = Score;
        multiplierReadout = Multiplier;
    }

    void OnDamaged(float damage, Vector3 point, bool sustained)
    {
        // Scored at the multiplier in force WHEN THE HIT LANDED, and the popup shows this same
        // number. Working it out again after the combo has climbed would print a figure that
        // was never added to the score.
        float gained = damage * gearsPerDamage * Multiplier;
        Score += gained;

        impactsCounted++;
        lastGain = gained;

        // Only a FRESH impact builds the combo. Sustained contact — grinding along a wall,
        // sliding on the roof — fires every sustainedInterval (0.08 s), so letting it feed
        // the combo would reach the cap in under half a second of scraping and make the whole
        // mechanic free.
        if (sustained) return;

        // Every fresh impact refreshes the window, including one too soon to raise the
        // multiplier, so a fast sequence of small hits still keeps a combo alive.
        comboExpiresAt = Time.time + comboWindow;

        int worth = Mathf.RoundToInt(gained);
        if (worth >= minPopupGears) Scored?.Invoke("+" + worth, point, Color.white, false);

        if (Time.time < comboRearmAt) return;
        comboRearmAt = Time.time + comboRearmInterval;

        Multiplier = Mathf.Min(Multiplier + comboPerHit, maxMultiplier);
    }

    void OnPartLost(CarDamage.Part part)
    {
        if (part == null) return;

        int bonus = Mathf.RoundToInt(part.startingHealth * gearsPerPartHealth * Multiplier);
        if (bonus <= 0) return;

        Score += bonus;

        // The part is already detached and flying, so its transform is a live debris position
        // rather than a point on the car. That is the right place for the popup — it tracks
        // the thing the player is actually watching leave.
        Vector3 at = part.visual != null ? part.visual.position : transform.position;
        Scored?.Invoke(part.Label + "  +" + bonus, at, Color.white, false);

        CheckFeats(at);
    }

    /// <summary>
    /// Pay out any feat the player has just completed.
    /// </summary>
    /// <remarks>
    /// Evaluated against the PLAYER's car only, and always from scratch rather than from the
    /// part that was just lost. That is what makes it correct when traffic is registered for
    /// scoring too: a traffic car shedding its fourth wheel does not change the player's parts,
    /// so nothing fires, and this never has to work out which car an event came from.
    /// </remarks>
    void CheckFeats(Vector3 at)
    {
        if (feats == null || feats.Length == 0) return;
        if (PlayerCar.Current == null) return;

        CarDamage car = PlayerCar.Current.Damage;
        if (car == null || car.parts == null) return;

        foreach (Feat feat in feats)
        {
            if (feat == null || earnedFeats.Contains(feat.id)) continue;

            string group = (feat.group ?? "").Trim().ToLowerInvariant();
            int total = 0;
            int lost = 0;

            foreach (CarDamage.Part part in car.parts)
            {
                if (part == null) continue;
                if (group.Length > 0 && part.Group != group) continue;

                total++;
                if (part.detached) lost++;
            }

            // A feat whose group matches nothing on this car must never fire. Without this,
            // "all mirrors gone" is vacuously true on a car with no mirrors and pays out the
            // instant anything at all falls off.
            if (total == 0) continue;

            int needed = feat.required > 0 ? Mathf.Min(feat.required, total) : total;
            if (lost < needed) continue;

            earnedFeats.Add(feat.id);
            Score += feat.bonus;
            featsEarned = earnedFeats.Count;

            string text = string.IsNullOrWhiteSpace(feat.subtitle)
                ? $"{feat.title}   +{feat.bonus}"
                : $"{feat.title}   +{feat.bonus}\n{feat.subtitle}";

            Scored?.Invoke(text, at, feat.colour, true);
        }
    }

    /// <summary>
    /// Pay this run into the wallet. Called automatically when the scene unloads, so a
    /// restart, a quit and a return to the menu all bank correctly. Safe to call twice.
    /// </summary>
    public void Bank()
    {
        if (banked) return;
        banked = true;

        PlayerWallet.Deposit(Gears);
    }

    void OnDestroy()
    {
        // Unsubscribe before banking. A car destroyed in the same teardown could otherwise
        // fire one last event into a component that is already going away.
        for (int i = scoring.Count - 1; i >= 0; i--)
        {
            CarDamage car = scoring[i];
            if (car == null) continue;

            car.Damaged -= OnDamaged;
            car.PartLost -= OnPartLost;
        }
        scoring.Clear();

        Bank();

        if (Instance == this) Instance = null;
    }
}
