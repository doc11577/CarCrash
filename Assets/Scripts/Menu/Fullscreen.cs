using UnityEngine;

/// <summary>
/// Fullscreen toggle, aimed at a Web build embedded in Google Sites.
/// </summary>
/// <remarks>
/// On the Web target `Screen.fullScreen` goes through the browser Fullscreen API, which has two
/// rules that decide whether this works at all:
///
///   1. It must be triggered by a REAL user gesture. Calling it from Start, a timer or a scene
///      load is silently refused, which is why this only ever runs from a button click.
///   2. Inside an iframe it needs the parent to have granted fullscreen. This game is embedded
///      two iframes deep in Google Sites, and we do not control the outer one — so this can be
///      refused through no fault of the code.
///
/// Because of (2) there is a SECOND fullscreen control in tools/embed.html, which can check
/// `document.fullscreenEnabled` and tell the player when the page is not allowed to go
/// fullscreen. That one is the more reliable of the two; this one exists so the option is
/// somewhere sensible while playing.
///
/// Chromebook fallback, worth telling players: the dedicated fullscreen key (F4 position) makes
/// the whole browser fullscreen. It is not canvas fullscreen but it hides the Sites chrome,
/// which is most of the benefit, and nothing can block it.
/// </remarks>
public static class Fullscreen
{
    public static bool Active => Screen.fullScreen;

    public static void Toggle()
    {
        Screen.fullScreen = !Screen.fullScreen;
    }

    /// <summary>Label for a toggle button, so the two menus cannot disagree about wording.</summary>
    public static string Label => Screen.fullScreen ? "EXIT FULLSCREEN" : "FULLSCREEN";
}
