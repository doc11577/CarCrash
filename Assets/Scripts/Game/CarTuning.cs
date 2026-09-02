using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The dev tuner's values, and their persistence. PER CAR.
/// </summary>
/// <remarks>
/// The table lives here rather than in <see cref="PauseMenu"/> because two things need it: the
/// tuner that edits the values, and <see cref="PlayerCarSpawner"/>, which has to put them back on
/// a freshly spawned car. Two copies of a list like this would drift, and the failure would be
/// silent — you would tune a spring rate, restart, and quietly get the prefab's.
///
/// **Saved per car id.** The truck's spring rate is 2.5x the E30's by design, so one shared set
/// of numbers would be wrong for every car but the last one tuned.
///
/// **Cleared by RESET PROGRESS**, along with the wallet and dev mode, because a tuned car is
/// exactly the kind of state that makes "what does a new player see" impossible to answer.
/// PlayerPrefs cannot be enumerated, so an INDEX of tuned car ids is kept alongside — the same
/// approach PlayerWallet uses for owned cars, for the same reason.
/// </remarks>
public static class CarTuning
{
    /// <summary>
    /// Everything the dev tuner can change, and how to read and write it.
    /// </summary>
    /// <remarks>
    /// `key` is what goes in PlayerPrefs and must NEVER be renamed once used — a rename silently
    /// abandons whatever the player had saved under the old one. `step` is the +/- nudge.
    /// </remarks>
    public readonly struct Tunable
    {
        public readonly string label;
        public readonly string key;
        public readonly System.Func<CarController, float> get;
        public readonly System.Action<CarController, float> set;
        public readonly float step;

        public Tunable(string label, string key,
                       System.Func<CarController, float> get,
                       System.Action<CarController, float> set,
                       float step)
        {
            this.label = label;
            this.key = key;
            this.get = get;
            this.set = set;
            this.step = step;
        }
    }

    public static readonly Tunable[] Tunables =
    {
        new Tunable("Top speed",    "topSpeed",   c => c.topSpeed,         (c, v) => c.topSpeed = v,         2f),
        new Tunable("Engine power", "power",      c => c.enginePower,      (c, v) => c.enginePower = v,      200f),
        new Tunable("Grip front",   "gripF",      c => c.frontGrip,        (c, v) => c.frontGrip = Mathf.Clamp01(v), 0.05f),
        new Tunable("Grip rear",    "gripR",      c => c.rearGrip,         (c, v) => c.rearGrip = Mathf.Clamp01(v),  0.05f),

        // THE REAL GRIP DIAL. frontGrip and rearGrip are saturated in any actual slide -- see
        // CarController -- so this is the one that changes how planted a car feels.
        new Tunable("Grip force",   "gripForce",  c => c.maxGripForce,     (c, v) => c.maxGripForce = Mathf.Max(0f, v), 2000f),
        new Tunable("Downforce",    "downforce",  c => c.downforce,        (c, v) => c.downforce = Mathf.Max(0f, v), 0.1f),
        new Tunable("Spring",       "spring",     c => c.springStrength,   (c, v) => c.springStrength = v,   500f),
        new Tunable("Damper",       "damper",     c => c.damperStrength,   (c, v) => c.damperStrength = v,   200f),
        new Tunable("Anti-roll",    "antiRoll",   c => c.antiRollStrength, (c, v) => c.antiRollStrength = v, 500f),
        new Tunable("Steer angle",  "steer",      c => c.maxSteerAngle,    (c, v) => c.maxSteerAngle = v,    2f),
        new Tunable("Steer @ speed","steerHi",    c => c.highSpeedSteerAngle, (c, v) => c.highSpeedSteerAngle = v, 1f),
        new Tunable("Turn assist",  "turnAssist", c => c.turnAssist,       (c, v) => c.turnAssist = Mathf.Max(0f, v), 0.5f),
    };

    const string IndexKey = "carcrash.tuned";

    static string FieldKey(string carId, string field)
    {
        return "carcrash.tune." + carId + "." + field;
    }

    /// <summary>Write every tunable for this car. Called whenever the tuner changes a value.</summary>
    public static void Save(CarController car, string carId)
    {
        if (car == null || string.IsNullOrWhiteSpace(carId)) return;

        foreach (Tunable tunable in Tunables)
        {
            // InvariantCulture, or a machine with a comma decimal writes "0,85" and reads it
            // back as 85 — a grip value off by a hundred, only on some people's machines.
            PlayerPrefs.SetString(FieldKey(carId, tunable.key),
                                  tunable.get(car).ToString("R", CultureInfo.InvariantCulture));
        }

        Remember(carId);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Put saved values back onto a car. Silently does nothing where none were saved, so an
    /// untuned car keeps its prefab values field by field.
    /// </summary>
    public static void Apply(CarController car, string carId)
    {
        if (car == null || string.IsNullOrWhiteSpace(carId)) return;

        foreach (Tunable tunable in Tunables)
        {
            string stored = PlayerPrefs.GetString(FieldKey(carId, tunable.key), "");
            if (string.IsNullOrEmpty(stored)) continue;

            if (float.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture,
                               out float value))
            {
                tunable.set(car, value);
            }
        }
    }

    /// <summary>True if this car has anything saved. For a "tuned" marker in the UI.</summary>
    public static bool HasSaved(string carId)
    {
        if (string.IsNullOrWhiteSpace(carId)) return false;
        return PlayerPrefs.HasKey(FieldKey(carId, Tunables[0].key));
    }

    /// <summary>Forget every car's tuning. Called by RESET PROGRESS.</summary>
    public static void ResetAll()
    {
        foreach (string carId in Index())
        {
            foreach (Tunable tunable in Tunables)
                PlayerPrefs.DeleteKey(FieldKey(carId, tunable.key));
        }

        PlayerPrefs.DeleteKey(IndexKey);
        PlayerPrefs.Save();
    }

    static List<string> Index()
    {
        string raw = PlayerPrefs.GetString(IndexKey, "");
        List<string> ids = new List<string>();

        foreach (string id in raw.Split('|'))
            if (!string.IsNullOrEmpty(id)) ids.Add(id);

        return ids;
    }

    static void Remember(string carId)
    {
        // Ids cannot contain '|' — CarRoster.Buy enforces the same thing for owned cars.
        if (carId.Contains("|")) return;

        List<string> ids = Index();
        if (ids.Contains(carId)) return;

        ids.Add(carId);
        PlayerPrefs.SetString(IndexKey, string.Join("|", ids));
    }
}
