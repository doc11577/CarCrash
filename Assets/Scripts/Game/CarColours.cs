using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The paint shop: which colours exist, which are bought, and which car wears which.
/// </summary>
/// <remarks>
/// **The palette is a static table, not an Inspector list.** Prices and unlocks are content two
/// places must agree on — the garage that sells them and the spawner that applies them — and this
/// project has learned that lesson once already with <see cref="CarRoster"/>: two copies of a list
/// drift silently, and you buy one thing and get another.
///
/// **Colour is stored PER CAR**, so painting the truck does not repaint the Aventador. Same
/// reasoning as <see cref="CarTuning"/>, which keeps a spring rate per car.
///
/// **Ownership is stored by ID, never by index.** Reordering the palette must not hand the player
/// a colour they did not buy. Free colours are never written to prefs — owned by definition, and
/// storing them would mean one could be lost by clearing storage.
///
/// Tints go through <see cref="CarPaint"/>, which writes a MaterialPropertyBlock per submesh. The
/// colours MULTIPLY a near-white body texture, so they read darker and greyer on the car than in
/// a swatch. The palette is picked with that in mind — see CarPaint for why a saturated primary
/// comes out as flat poster colour.
/// </remarks>
public static class CarColours
{
    /// <summary>One free or buyable paint, including its FINISH.</summary>
    /// <remarks>
    /// A paint is a colour AND a surface. The paid four are sold as metals, and a metal that is
    /// merely a lighter shade of grey is the con this palette exists to avoid — what makes silver
    /// read as silver is `metallic`, not the RGB.
    /// </remarks>
    public readonly struct Paint
    {
        public readonly string id;
        public readonly string displayName;
        public readonly Color colour;
        public readonly int price;

        /// <summary>URP `_Metallic`. 0 is painted bodywork, 1 is bare metal.</summary>
        public readonly float metallic;

        /// <summary>URP `_Smoothness`. Higher is a sharper, glassier reflection.</summary>
        public readonly float smoothness;

        /// <summary>
        /// How to draw this paint as a FLAT UI swatch.
        /// </summary>
        /// <remarks>
        /// **A flat square cannot show shine, and that makes the honest colours misleading.**
        /// Platinum's reflectance is correctly duller than silver's — real platinum is greyer —
        /// so drawn raw, the 200,000 paint looks like a worse version of the 50,000 one. On the
        /// car it does not, because most of what you see off a metal is reflected light.
        ///
        /// Lifting the swatch toward white in proportion to `metallic` is that reflected light,
        /// approximated. Derived from the paint rather than hand-picked per colour, so a tenth
        /// paint needs no second decision — and the value on the CAR is untouched.
        /// </remarks>
        public Color Swatch => Color.Lerp(colour, Color.white, metallic * 0.3f);

        /// <summary>Free paints are owned by everyone and never written to prefs.</summary>
        public bool Free => price <= 0;

        public bool Owned => Free || PlayerPrefs.GetInt(OwnedKey(id), 0) == 1;

        public Paint(string id, string displayName, Color colour, int price,
                     float metallic, float smoothness)
        {
            this.id = id;
            this.displayName = displayName;
            this.colour = colour;
            this.price = price;
            this.metallic = metallic;
            this.smoothness = smoothness;
        }
    }

    static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

    /// <summary>Five free, four bought. Order is display order; ids are permanent.</summary>
    /// <remarks>
    /// The free five are muted road-car colours, for the reason recorded under CarPaint: these
    /// multiply a near-white texture, so anything saturated reads as a moulded toy.
    ///
    /// The paid four are deliberately a different KIND of finish rather than merely a brighter
    /// hue — silver, gold, platinum, phantom black. A colour someone pays 500,000 gears for has
    /// to look like a different material, or the purchase reads as a con.
    /// </remarks>
    public static readonly Paint[] Palette =
    {
        //                                                              price  metal  smooth
        new Paint("white",    "WHITE",         Rgb(232, 232, 230),      0,     0f,    0.50f),
        new Paint("red",      "RED",           Rgb(122,  23,  28),      0,     0f,    0.50f),
        new Paint("navy",     "NAVY",          Rgb( 31,  51, 102),      0,     0f,    0.50f),
        new Paint("green",    "RACING GREEN",  Rgb( 26,  66,  43),      0,     0f,    0.50f),
        new Paint("graphite", "GRAPHITE",      Rgb( 61,  64,  69),      0,     0f,    0.45f),

        // **The metals use real reflectance values, not "a lighter grey".** On a metallic
        // surface the base colour stops being albedo and becomes the tint of the REFLECTION, so
        // the RGB has to be the metal's actual F0 or it reads as painted plastic that happens to
        // be shiny. These are the standard measured figures.
        new Paint("silver",   "SILVER",        Rgb(248, 245, 233),  50000,     1f,    0.80f),
        new Paint("gold",     "GOLD",          Rgb(255, 195,  86), 100000,     1f,    0.86f),
        new Paint("platinum", "PLATINUM",      Rgb(173, 164, 150), 200000,     1f,    0.93f),

        // Obsidian is volcanic GLASS, not metal: near-black, and glossier than any of the metals.
        // Full metallic would make it a black mirror with no depth, so it keeps some diffuse and
        // takes the highest smoothness in the palette. Platinum being darker than silver is
        // correct and deliberate — real platinum is greyer.
        new Paint("phantom",  "PHANTOM BLACK", Rgb( 14,  15,  19), 500000,  0.55f,    0.97f),
    };

    public const string DefaultId = "white";

    const string OwnedIndexKey = "carcrash.paints";
    const string CarIndexKey = "carcrash.carpaint.index";

    static string OwnedKey(string paintId) => "carcrash.paint." + paintId;
    static string CarKey(string carId) => "carcrash.carpaint." + carId;

    /// <summary>Look a paint up by id. Falls back to the default rather than returning null.</summary>
    public static Paint Get(string paintId)
    {
        foreach (Paint p in Palette)
            if (p.id == paintId) return p;

        foreach (Paint p in Palette)
            if (p.id == DefaultId) return p;

        return Palette[0];
    }

    /// <summary>Which paint a car is wearing. Every car starts on the default.</summary>
    public static Paint For(string carId)
    {
        if (string.IsNullOrWhiteSpace(carId)) return Get(DefaultId);

        Paint chosen = Get(PlayerPrefs.GetString(CarKey(carId), DefaultId));

        // A paint that was owned when it was chosen but is not now — after a reset, or a save
        // code from another profile — must not keep being worn. Fall back rather than silently
        // applying something unpaid for.
        return chosen.Owned ? chosen : Get(DefaultId);
    }

    /// <summary>Paint a car. Refuses a colour that is not owned.</summary>
    public static bool Choose(string carId, string paintId)
    {
        if (string.IsNullOrWhiteSpace(carId)) return false;

        Paint paint = Get(paintId);
        if (!paint.Owned) return false;

        PlayerPrefs.SetString(CarKey(carId), paint.id);
        RememberCar(carId);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>Buy a paint. Once bought it unlocks for EVERY car, not just the one on show.</summary>
    /// <remarks>
    /// Per-car unlocks were considered and rejected: at 500,000 gears, buying phantom black four
    /// times over is not a progression curve, it is a wall. The colour is the reward.
    /// </remarks>
    public static bool Buy(string paintId)
    {
        Paint paint = Get(paintId);
        if (paint.Owned) return true;
        if (!PlayerWallet.Spend(paint.price)) return false;

        PlayerPrefs.SetInt(OwnedKey(paint.id), 1);
        RememberPaint(paint.id);
        PlayerPrefs.Save();
        return true;
    }

    // ---- persistence plumbing ---------------------------------------------------------------
    //
    // PlayerPrefs cannot be enumerated, so an INDEX of what has been written is kept alongside.
    // Same approach PlayerWallet uses for owned cars and CarTuning for tuned cars, and for the
    // same reason: without it, RESET PROGRESS cannot find the keys it needs to delete.

    static List<string> Split(string raw)
    {
        List<string> ids = new List<string>();
        foreach (string id in raw.Split('|'))
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        return ids;
    }

    static void RememberCar(string carId)
    {
        // Ids cannot contain the delimiter. CarRoster.Buy enforces the same thing for owned cars.
        if (carId.Contains("|") || carId.Contains(":")) return;

        List<string> ids = Split(PlayerPrefs.GetString(CarIndexKey, ""));
        if (ids.Contains(carId)) return;

        ids.Add(carId);
        PlayerPrefs.SetString(CarIndexKey, string.Join("|", ids));
    }

    static void RememberPaint(string paintId)
    {
        List<string> ids = Split(PlayerPrefs.GetString(OwnedIndexKey, ""));
        if (ids.Contains(paintId)) return;

        ids.Add(paintId);
        PlayerPrefs.SetString(OwnedIndexKey, string.Join("|", ids));
    }

    /// <summary>Every paid paint owned, pipe-separated. For the save code.</summary>
    public static string OwnedIds => PlayerPrefs.GetString(OwnedIndexKey, "");

    /// <summary>Every car-to-paint choice as `car:paint` pairs, pipe-separated. For the save code.</summary>
    public static string ChoiceIds
    {
        get
        {
            List<string> pairs = new List<string>();
            foreach (string carId in Split(PlayerPrefs.GetString(CarIndexKey, "")))
            {
                string paint = PlayerPrefs.GetString(CarKey(carId), "");
                if (!string.IsNullOrEmpty(paint)) pairs.Add(carId + ":" + paint);
            }
            return string.Join("|", pairs);
        }
    }

    /// <summary>Put a saved paint shop back. Used by the save code.</summary>
    public static void Restore(string ownedIds, string choiceIds)
    {
        ResetAll();

        foreach (string paintId in Split(ownedIds))
        {
            PlayerPrefs.SetInt(OwnedKey(paintId), 1);
            RememberPaint(paintId);
        }

        foreach (string pair in Split(choiceIds))
        {
            int split = pair.IndexOf(':');
            if (split <= 0 || split >= pair.Length - 1) continue;

            RememberCar(pair.Substring(0, split));
            PlayerPrefs.SetString(CarKey(pair.Substring(0, split)), pair.Substring(split + 1));
        }

        PlayerPrefs.Save();
    }

    /// <summary>Forget every purchase and every choice. Called by RESET PROGRESS.</summary>
    public static void ResetAll()
    {
        foreach (string paintId in Split(PlayerPrefs.GetString(OwnedIndexKey, "")))
            PlayerPrefs.DeleteKey(OwnedKey(paintId));

        foreach (string carId in Split(PlayerPrefs.GetString(CarIndexKey, "")))
            PlayerPrefs.DeleteKey(CarKey(carId));

        PlayerPrefs.DeleteKey(OwnedIndexKey);
        PlayerPrefs.DeleteKey(CarIndexKey);
        PlayerPrefs.Save();
    }
}
