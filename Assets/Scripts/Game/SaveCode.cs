using System;
using System.Text;
using UnityEngine;

/// <summary>
/// The player's progress as a short text string they can copy, keep, and paste back in.
/// </summary>
/// <remarks>
/// Two jobs, and the second is the one that was actually asked for:
///
///  1. **A rescue when the browser will not store anything.** See <see cref="SaveHealth"/> —
///     inside a sandboxed Google Sites iframe, storage can be blocked and PlayerPrefs then fails
///     silently. A code the player holds is immune to that, because it never touches storage.
///  2. **Moving progress between devices** — the school Chromebook and a home machine. Storage
///     is per-browser and per-origin, so there is no other way to carry a balance across.
///
/// **Why not sign in with a Google account?** It was considered and it does not fit. It needs an
/// OAuth redirect, which a sandboxed cross-origin iframe cannot do cleanly; it needs a server to
/// hold the saves, which the whole project is built to avoid because a server is the one thing
/// school IT can block; and managed school accounts routinely refuse third-party OAuth outright.
/// A text code needs none of that and cannot be blocked by anything.
///
/// **Not encrypted, and deliberately not.** A player who wants to hand themselves gears can
/// already do it from devtools — `PlayerWallet` says so. Obfuscating the code would only make it
/// harder to support when someone pastes a broken one. The checksum is there to catch a
/// TRUNCATED or mistyped code, not a dishonest one: silently loading garbage would be far worse
/// than refusing it, because it would overwrite real progress with nonsense.
///
/// Format, before encoding: `C1|gears|bestRun|ownedIds|checksum`. Base64url with the padding
/// stripped, so the result is alphanumeric plus `-` and `_` — safe to paste anywhere, including
/// into a URL or a chat message that might mangle `+` and `/`.
/// </remarks>
public static class SaveCode
{
    /// <summary>
    /// Current format. Bumped to C2 on 2026-09-02 when paint ownership and per-car paint choices
    /// were added.
    /// </summary>
    const string Version = "C2";

    /// <summary>
    /// The format before paints. Still READ, never written — a code someone saved yesterday has
    /// to keep working, and a save system that loses progress to a format change is worse than
    /// no save system.
    /// </summary>
    const string LegacyVersion = "C1";

    /// <summary>The current progress as a code. Never returns null.</summary>
    public static string Export()
    {
        // v2 carries THREE variable-length lists (cars, paints, car-to-paint choices), and the
        // v1 trick of "everything between the fixed fields and the checksum is the list" cannot
        // tell three lists apart. So the inner lists are re-joined with COMMAS and the top level
        // stays a fixed seven fields split on '|'.
        //
        // That is the general fix for the bug v1 shipped with: **a delimited list inside a
        // delimited format needs a different delimiter, not a cleverer parser.**
        string body = string.Join("|", Version,
                                       PlayerWallet.Gears.ToString(),
                                       PlayerWallet.BestRun.ToString(),
                                       Inner(PlayerWallet.OwnedIds),
                                       Inner(CarColours.OwnedIds),
                                       Inner(CarColours.ChoiceIds));

        return ToBase64Url(Encoding.UTF8.GetBytes(body + "|" + Checksum(body)));
    }

    /// <summary>Pipe-delimited as stored, comma-delimited inside a save code.</summary>
    static string Inner(string piped) => (piped ?? "").Replace('|', ',');

    /// <summary>The reverse of <see cref="Inner"/>.</summary>
    static string Outer(string commas) => (commas ?? "").Replace(',', '|');

    /// <summary>
    /// Apply a code. Returns false and changes NOTHING if it is not a valid code, so a bad paste
    /// can never destroy the progress the player already has.
    /// </summary>
    public static bool TryImport(string code, out string message)
    {
        message = "";

        if (string.IsNullOrWhiteSpace(code))
        {
            message = "Paste a save code first.";
            return false;
        }

        // People paste with spaces, line breaks and stray quotes, especially off a second screen.
        StringBuilder cleaned = new StringBuilder(code.Length);
        foreach (char c in code)
            if (!char.IsWhiteSpace(c) && c != '"' && c != '\'') cleaned.Append(c);

        string body;
        try
        {
            body = Encoding.UTF8.GetString(FromBase64Url(cleaned.ToString()));
        }
        catch
        {
            message = "That is not a save code.";
            return false;
        }

        // version | gears | best | owned... | checksum
        //
        // The owned list is ITSELF pipe-delimited (PlayerWallet stores "p72|lct3000"), so the
        // field count is not fixed and a naive Split(...).Length != 5 rejects the save of every
        // player who owns two or more cars -- which is precisely the save worth keeping. Caught
        // by a round-trip test, never by inspection.
        //
        // So: read the fixed fields off the front, the checksum off the back, and treat
        // everything between as the owned list, pipes and all.
        string[] parts = body.Split('|');
        if (parts.Length < 5)
        {
            message = "That is not a save code.";
            return false;
        }

        // v1: version | gears | best | owned... | checksum, where the owned list is ITSELF
        // pipe-delimited, so the field count is not fixed. v2 moved the inner lists to commas
        // and is a fixed seven fields. Both are read, because a code someone saved yesterday
        // has to keep working — losing progress to a format change is the one thing a save
        // system may not do.
        string owned, paints, choices, signed;

        if (parts[0] == Version)
        {
            if (parts.Length != 7)
            {
                message = "That code is damaged — check it was copied in full.";
                return false;
            }

            owned = Outer(parts[3]);
            paints = Outer(parts[4]);
            choices = Outer(parts[5]);
            signed = string.Join("|", parts, 0, 6);
        }
        else if (parts[0] == LegacyVersion)
        {
            owned = string.Join("|", parts, 3, parts.Length - 4);
            paints = "";
            choices = "";
            signed = string.Join("|", parts, 0, parts.Length - 1);
        }
        else
        {
            message = "That code is from a different version of the game.";
            return false;
        }

        if (Checksum(signed) != parts[parts.Length - 1])
        {
            message = "That code is damaged — check it was copied in full.";
            return false;
        }

        if (!int.TryParse(parts[1], out int gears) || !int.TryParse(parts[2], out int best) ||
            gears < 0 || best < 0)
        {
            message = "That code is damaged — check it was copied in full.";
            return false;
        }

        PlayerWallet.Restore(gears, best, owned);
        CarColours.Restore(paints, choices);
        message = $"Restored {gears:N0} gears.";
        return true;
    }

    /// <summary>
    /// FNV-1a, four hex characters. Enough to catch a truncated or mistyped code, which is all
    /// it is for — this is a typo check, not a security measure.
    /// </summary>
    static string Checksum(string s)
    {
        uint hash = 2166136261u;
        foreach (char c in s)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return (hash & 0xFFFF).ToString("x4");
    }

    static string ToBase64Url(byte[] data)
    {
        return Convert.ToBase64String(data)
                      .TrimEnd('=')
                      .Replace('+', '-')
                      .Replace('/', '_');
    }

    static byte[] FromBase64Url(string s)
    {
        string padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 1: throw new FormatException("bad length");
        }
        return Convert.FromBase64String(padded);
    }
}
