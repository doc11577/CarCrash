using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Small builder for the screens that need buttons. Shared by <see cref="MenuUI"/> and
/// <see cref="PauseMenu"/> so the two look like the same game and neither hand-rolls a canvas.
/// </summary>
/// <remarks>
/// Built in code for the same reason <see cref="ScoreHud"/> is: a hand-built Canvas is a couple
/// of dozen GameObjects whose anchors, pivots and font sizes can all be silently wrong in a
/// scene file and cannot be reviewed in a diff.
///
/// Unlike ScoreHud these canvases DO get a GraphicRaycaster, because their whole purpose is
/// being clicked. That is the one real cost here, and it is per pointer event rather than per
/// frame.
///
/// The palette matches RestartOverlay's loading bar, so the menu, the HUD and the loading screen
/// read as one product rather than three.
/// </remarks>
public static class UiKit
{
    public static readonly Color Ground = new Color(0.09f, 0.07f, 0.10f, 1f);
    public static readonly Color Accent = new Color(1f, 0.78f, 0.15f, 1f);
    public static readonly Color Ink = new Color(0.96f, 0.95f, 0.94f, 1f);
    public static readonly Color Muted = new Color(1f, 1f, 1f, 0.55f);
    public static readonly Color Slab = new Color(0.19f, 0.16f, 0.21f, 1f);

    /// <summary>A screen-space canvas that can be clicked.</summary>
    public static RectTransform Screen(Transform parent, string name, int order)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;

        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();

        return (RectTransform)go.transform;
    }

    /// <summary>
    /// Make sure something can route clicks to the UI, creating it if the scene has none.
    /// </summary>
    /// <remarks>
    /// This project uses the Input System package, so the module has to be
    /// InputSystemUIInputModule — the legacy StandaloneInputModule reads the old Input class,
    /// which is disabled, and every button would be silently dead. The module assigns itself
    /// the default actions in its own OnEnable, so there is nothing to wire.
    /// </remarks>
    public static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;

        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    /// <summary>Full-bleed background.</summary>
    public static Image Backdrop(Transform parent, Color colour)
    {
        GameObject go = new GameObject("Backdrop", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = go.AddComponent<Image>();
        image.color = colour;
        return image;
    }

    public static TextMeshProUGUI Text(Transform parent, string content, float size, Color colour,
                                       TextAlignmentOptions align, Vector2 pos, Vector2 box)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.color = colour;
        text.alignment = align;
        text.raycastTarget = false;
        text.richText = false;

        Centre(text.rectTransform, pos, box);
        return text;
    }

    public static UnityEngine.UI.Button Button(Transform parent, string label, Vector2 pos,
                                               Vector2 box, Action onClick, bool accent = false)
    {
        GameObject go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image background = go.AddComponent<Image>();
        background.color = Color.white;

        UnityEngine.UI.Button button = go.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = background;

        if (onClick != null) button.onClick.AddListener(() => onClick());

        Centre((RectTransform)go.transform, pos, box);

        TextMeshProUGUI text = Text(go.transform, label, 30f, Ink,
                                    TextAlignmentOptions.Center, Vector2.zero, box);
        text.fontStyle = FontStyles.Bold;

        Tint(button, accent);
        return button;
    }

    /// <summary>
    /// Recolour a button as accented or plain. Separate from construction so a selection can be
    /// moved between buttons at runtime — with one car in the list that is invisible, with two
    /// it is the only thing telling you which one is chosen.
    /// </summary>
    public static void Tint(UnityEngine.UI.Button button, bool accent)
    {
        if (button == null) return;

        Color idle = accent ? Accent : Slab;
        ColorBlock colours = button.colors;
        colours.normalColor = idle;
        colours.highlightedColor = Color.Lerp(idle, Color.white, 0.22f);
        colours.pressedColor = Color.Lerp(idle, Color.black, 0.25f);
        colours.selectedColor = colours.highlightedColor;
        colours.disabledColor = Color.Lerp(idle, Ground, 0.6f);
        colours.fadeDuration = 0.06f;
        button.colors = colours;

        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null) text.color = accent ? Ground : Ink;
    }

    /// <summary>Anchor to the middle of the screen and place by offset from centre.</summary>
    public static void Centre(RectTransform rect, Vector2 pos, Vector2 box)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = box;
    }
}
