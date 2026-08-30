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
    [Tooltip("The player's car. Damage the player takes IS the score in this game — the run " +
             "is about destroying your own car. Traffic will register itself at spawn through " +
             "Register(), so leave that to the spawner rather than listing cars here.")]
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
    public event Action<string, Vector3> Scored;

    [Header("Read-only — watch these in play mode")]
    [Tooltip("Cars currently being scored. 0 means Register never ran and NOTHING can score — " +
             "check Player Car above. 1 is correct until traffic exists.")]
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

    float comboExpiresAt;
    float comboRearmAt;
    bool banked;

    readonly List<CarDamage> scoring = new List<CarDamage>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Start, not Awake: a car wires up its own parts in Awake. Nothing here depends on
        // that today, but subscribing to a component that has not initialised yet is exactly
        // the ordering bug that will show up once traffic starts spawning mid-run.
        if (playerCar != null) Register(playerCar);
        else Debug.LogWarning("RunScore has no playerCar assigned — nothing will score.", this);
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
        if (worth >= minPopupGears) Scored?.Invoke("+" + worth, point);

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
        Scored?.Invoke(part.name.ToUpperInvariant() + "  +" + bonus, at);
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
