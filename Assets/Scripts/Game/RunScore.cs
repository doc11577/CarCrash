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

    [Tooltip("Gears per point of damage you do to SOMEONE ELSE'S car. Higher than your own, " +
             "because wrecking traffic is a deliberate act and taking damage yourself is mostly " +
             "what happens anyway. Only paid when YOU caused it — traffic wrecking itself on a " +
             "wall pays nothing, which is what makes this safe to switch on at all.")]
    public float gearsPerPvpDamage = 0.06f;

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

        TrackAirtime(Time.deltaTime);

        scoreReadout = Score;
        multiplierReadout = Multiplier;
    }

    /// <summary>
    /// Add gears from outside, with a popup. For scorers that belong to ONE map.
    /// </summary>
    /// <remarks>
    /// Exists so a map-specific rule — the dartboard, and whatever the next map wants — does not
    /// have to be built into `RunScore` itself. Damage, parts, airtime and feats are the rules
    /// that apply to every run and they live here; a target that only exists on Bullseye does
    /// not, and putting it here would mean every map paying to check a board it does not have.
    ///
    /// The combo multiplier is deliberately NOT applied. The caller decides what the award is
    /// worth, because a landing on the bull is worth what it is worth regardless of what the
    /// player happened to hit on the way down.
    /// </remarks>
    public void Award(int gears, string text, Vector3 at, Color colour, bool major)
    {
        if (gears > 0) Score += gears;
        if (!string.IsNullOrEmpty(text)) Scored?.Invoke(text, at, colour, major);
    }

    // ---- airtime ---------------------------------------------------------------------------

    /// <summary>
    /// Airborne time earns gears, but only once the car LANDS.
    /// </summary>
    /// <remarks>
    /// Paying on landing rather than continuously is the whole design. A jump that ends in the
    /// bottom of a ravine, or with the car falling out of the world, should pay nothing —
    /// otherwise the best way to farm gears is to drive off the map and wait. It also gives the
    /// counter somewhere to go: it climbs while the outcome is still in doubt and is banked at
    /// the moment the risk resolves, which is where the tension is.
    ///
    /// <see cref="minAirTime"/> is what separates a jump from a bump. `CarController.Grounded`
    /// goes false whenever all four SphereCasts miss, which happens over every crest and kerb on
    /// the course, so without a floor the counter would flicker constantly during normal driving.
    ///
    /// <see cref="maxAirTime"/> exists because "not grounded" is also true of a car wedged on a
    /// rock, upside down on a ledge, or falling out of the world. Without a cap those pay out
    /// unboundedly.
    /// </remarks>
    [Header("Airtime")]
    [Tooltip("Seconds off the ground before it counts as a jump at all, and before the counter " +
             "appears. Below this it is a bump over a crest — the wheels leave the ground " +
             "constantly on these courses, so a floor is what stops the counter flickering the " +
             "whole way down. Raised from 0.45 because at that value it still flickered on " +
             "rough ground; 0.8 is about the shortest thing that reads as a jump.")]
    public float minAirTime = 0.8f;

    [Tooltip("Gears earned per second airborne, before the combo multiplier.")]
    public float gearsPerAirSecond = 26f;

    [Tooltip("Gears at which the popup turns gold and grows. Roughly a 2.5 second jump.")]
    public int airGoldAt = 65;

    [Tooltip("Longest jump that can pay. A cap is needed because 'not grounded' is also true of " +
             "a car stuck on its roof or falling out of the world.")]
    public float maxAirTime = 9f;

    /// <summary>True while the car is off the ground, before the minimum is reached.</summary>
    public bool Airborne { get; private set; }

    /// <summary>Seconds off the ground so far this jump.</summary>
    public float AirTime { get; private set; }

    /// <summary>Gears this jump is currently worth. What the live popup shows.</summary>
    public int AirGears => Mathf.FloorToInt(AirTime * gearsPerAirSecond * Multiplier);

    /// <summary>Past the gold threshold — the HUD draws it larger and in the accent colour.</summary>
    public bool AirIsBig => AirGears >= airGoldAt;

    /// <summary>Whether the HUD should be drawing the airtime counter at all.</summary>
    public bool AirShowing => Airborne && AirTime >= minAirTime;

    void TrackAirtime(float dt)
    {
        CarController car = PlayerCar.Current != null ? PlayerCar.Current.Controller : null;

        // No car — mid-respawn, or the run is ending. Drop the jump rather than banking it,
        // since there is nothing to have landed.
        if (car == null)
        {
            Airborne = false;
            AirTime = 0f;
            return;
        }

        // Touching, not Grounded. Grounded is a WHEEL test, so a car that comes down on its
        // roof or its side is still "not grounded" and the counter would run through the whole
        // crash and only pay if it happened to settle on its tyres. Any part of the car hitting
        // anything ends the jump, which is what landing means.
        if (!car.Touching)
        {
            Airborne = true;
            touchingFor = 0f;
            AirTime = Mathf.Min(AirTime + dt, maxAirTime);
            return;
        }

        // ONE timer until you actually land. Ending the jump on the first frame of contact made
        // a single graze — a wingtip on a rock face, a wheel clipping a ledge mid-flight —
        // bank the jump and restart the counter from zero while the car was still in the air.
        // Reported on Everest, which is a jagged 70-degree face and grazes constantly, and it
        // read as "the timer resets for no reason".
        //
        // Contact has to PERSIST to count as a landing. A graze is contact for a frame or two;
        // a landing is contact that stays.
        touchingFor += dt;
        if (touchingFor < landingGrace) return;

        if (Airborne && AirTime >= minAirTime)
        {
            int worth = AirGears;
            if (worth > 0)
            {
                Score += worth;

                // Same colour and size rule the live counter used, so the popup that confirms
                // the payout looks like the thing the player was watching climb.
                Scored?.Invoke("AIRTIME  +" + worth, car.transform.position,
                               AirIsBig ? airColour : Color.white, AirIsBig);
            }
        }

        Airborne = false;
        AirTime = 0f;
        touchingFor = 0f;
    }

    /// <summary>How long the car has been in continuous contact. See <see cref="landingGrace"/>.</summary>
    float touchingFor;

    [Tooltip("Seconds of CONTINUOUS contact before a jump counts as landed. A graze against a " +
             "rock face mid-flight is contact for a frame or two; a landing is contact that " +
             "stays. Without this the timer banks and restarts from zero every time the car " +
             "brushes anything, which on Everest is constantly.\n\n" +
             "Too high and a genuine landing that immediately bounces reads as one long jump.")]
    public float landingGrace = 0.2f;

    [Tooltip("Colour of the airtime popup once it passes the gold threshold.")]
    public Color airColour = new Color(1f, 0.78f, 0.15f);

    [Tooltip("Colour of the popup for damage done to another car.")]
    public Color pvpColour = new Color(1f, 0.42f, 0.30f);

    void OnDamaged(CarDamage source, float damage, Vector3 point, bool sustained, bool byPlayer)
    {
        // WHOSE car was hurt, and who did it.
        //
        // Damage to your own car always counts. Damage to anyone ELSE'S counts only when you
        // caused it — a traffic car wrecking itself on a wall pays nothing, which is precisely
        // the objection that kept TrafficSpawner.scoreTrafficDamage switched off. With the
        // source and the byPlayer flag on the event, it can finally be turned on.
        bool mine = source == playerCar;
        if (!mine && !byPlayer) return;

        float rate = mine ? gearsPerDamage : gearsPerPvpDamage;

        // Scored at the multiplier in force WHEN THE HIT LANDED, and the popup shows this same
        // number. Working it out again after the combo has climbed would print a figure that
        // was never added to the score.
        float gained = damage * rate * Multiplier;
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
        if (worth >= minPopupGears)
        {
            // Wrecking someone else reads differently from being wrecked, so it is worth
            // saying which happened rather than printing the same white "+12" for both.
            Scored?.Invoke(mine ? "+" + worth : "WRECKER  +" + worth, point,
                           mine ? Color.white : pvpColour, !mine);
        }

        if (Time.time < comboRearmAt) return;
        comboRearmAt = Time.time + comboRearmInterval;

        Multiplier = Mathf.Min(Multiplier + comboPerHit, maxMultiplier);
    }

    void OnPartLost(CarDamage source, CarDamage.Part part, bool byPlayer)
    {
        if (part == null) return;

        // The SAME rule OnDamaged uses, and it was missing here. A part off your own car always
        // pays; a part off anyone else's pays only when you knocked it off. Without this, every
        // traffic car that wrecked itself on a rock paid the player and threw a popup — and
        // since there are only eight popup slots, that spam recycled the WRECKER popups before
        // they could be read, which is why car-on-car scoring looked broken as well.
        //
        // Worst on Everest, where obstacleAvoidance is 0 by design so the field ploughs into the
        // scenery constantly. That is why it looked map-specific.
        bool mine = source == playerCar;
        if (!mine && !byPlayer) return;

        // Wrecking someone else's panel pays at the PvP rate, for the same reason their damage
        // does: it is a deliberate act, where losing your own is mostly just what happens.
        float rate = mine ? gearsPerPartHealth : gearsPerPartHealth * (gearsPerPvpDamage / Mathf.Max(0.0001f, gearsPerDamage));
        int bonus = Mathf.RoundToInt(part.startingHealth * rate * Multiplier);
        if (bonus <= 0) return;

        Score += bonus;

        // The part is already detached and flying, so its transform is a live debris position
        // rather than a point on the car. That is the right place for the popup — it tracks
        // the thing the player is actually watching leave.
        Vector3 at = part.visual != null ? part.visual.position : transform.position;
        Scored?.Invoke(mine ? part.Label + "  +" + bonus
                            : "WRECKER  " + part.Label + "  +" + bonus,
                       at, mine ? Color.white : pvpColour, !mine);

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
