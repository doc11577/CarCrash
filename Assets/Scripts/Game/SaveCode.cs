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
    const string Version = "C1";

    /// <summary>The current progress as a code. Never returns null.</summary>
    public static string Export()
    {
        string body = string.Join("|", Version,
                                       PlayerWallet.Gears.ToString(),
                                       PlayerWallet.BestRun.ToString(),
                                       PlayerWallet.OwnedIds);

        return ToBase64Url(Encoding.UTF8.GetBytes(body + "|" + Checksum(body)));
    }

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
        if (parts.Length < 5 || parts[0] != Version)
        {
            message = "That code is from a different version of the game.";
            return false;
        }

        string owned = string.Join("|", parts, 3, parts.Length - 4);
        string signed = string.Join("|", parts, 0, parts.Length - 1);

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
