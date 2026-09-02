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

    /// <param name="fontSize">
    /// Label size. Defaults to 30, which suits a full-height button. A list that has had to
    /// compress itself to fit passes a smaller value — a 30pt label in a 34pt button spills
    /// out of it, which looks exactly like the clipping the compression was meant to prevent.
    /// </param>
    public static UnityEngine.UI.Button Button(Transform parent, string label, Vector2 pos,
                                               Vector2 box, Action onClick, bool accent = false,
                                               float fontSize = 30f)
    {
        GameObject go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image background = go.AddComponent<Image>();
        background.color = Color.white;

        UnityEngine.UI.Button button = go.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = background;

        if (onClick != null) button.onClick.AddListener(() => onClick());

        Centre((RectTransform)go.transform, pos, box);

        TextMeshProUGUI text = Text(go.transform, label, fontSize, Ink,
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

        // Hover goes GOLD. An already-gold button lightens instead, or the accent button would
        // have no hover state at all.
        Color hover = accent ? Color.Lerp(Accent, Color.white, 0.28f) : Accent;

        ColorBlock colours = button.colors;
        colours.normalColor = idle;
        colours.highlightedColor = hover;
        colours.pressedColor = Color.Lerp(hover, Color.black, 0.25f);

        // SELECTED must match NORMAL, not highlighted.
        //
        // Unity leaves a clicked button SELECTED in the EventSystem, and a selected button keeps
        // drawing its selectedColor whether or not the pointer is still on it. With selected set
        // to the highlight, every button you had ever clicked stayed lit up after the mouse moved
        // away, and the only way to clear it was to click something else. Matching normal means
        // the highlight belongs to hover alone, which is what a highlight is.
        colours.selectedColor = idle;
        colours.disabledColor = Color.Lerp(idle, Ground, 0.6f);
        colours.fadeDuration = 0.06f;
        button.colors = colours;

        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>();
        if (text == null) return;

        Color labelIdle = accent ? Ground : Ink;
        text.color = labelIdle;

        // The LABEL has to change with the background, and this is the reason a hover colour
        // cannot just be set and forgotten. A ColorBlock tints the button's target graphic only,
        // so near-white Ink text over a gold hover is a contrast ratio of about 1.3 — legible in
        // a screenshot, unreadable in motion. Dark text on gold is about 5.5.
        //
        // One tiny component per button, added once and updated in place, because uGUI has no
        // built-in way to drive anything but the target graphic from the ColorBlock.
        LabelTint tint = button.GetComponent<LabelTint>();
        if (tint == null) tint = button.gameObject.AddComponent<LabelTint>();
        tint.Set(text, labelIdle, Ground);
    }

    /// <summary>
    /// Swaps a button's label colour on hover, since a ColorBlock cannot.
    /// </summary>
    class LabelTint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        TextMeshProUGUI label;
        Color idle;
        Color hover;

        public void Set(TextMeshProUGUI text, Color idleColour, Color hoverColour)
        {
            label = text;
            idle = idleColour;
            hover = hoverColour;

            // Re-tinted while the pointer is already over it — the garage repaints buttons on
            // every purchase — so settle on the idle colour rather than assuming not-hovered.
            if (label != null) label.color = idle;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (label != null) label.color = hover;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (label != null) label.color = idle;
        }
    }

    /// <summary>
    /// Where the rows of a growing list go, so the list cannot run off the bottom of its page.
    /// </summary>
    /// <remarks>
    /// Both list pages were laid out as a fixed step from a fixed top, which is correct only
    /// for the number of entries that happened to exist when it was written. The garage fitted
    /// two cars and clipped on the third: the blurb, OWNED line and CC-BY credit all drew
    /// straight through the third button. Map select has the identical bug waiting at four maps.
    ///
    /// A row is given a SLOT out of a fixed band. The slot is capped at the comfortable size, so
    /// nothing moves until the list would otherwise overflow — the two-map page looks exactly as
    /// it did — and past that point rows and their labels shrink together instead of colliding
    /// with whatever sits underneath.
    ///
    /// This is not a scroll view. Past roughly eight rows the text is too small to read and a
    /// real ScrollRect is the answer; it needs a viewport, a mask and its own raycaster, which
    /// is not worth building for a roster this size.
    /// </remarks>
    /// <param name="top">Top edge of the band, in page coordinates.</param>
    /// <param name="bottom">Bottom edge of the band. Nothing is drawn below this.</param>
    /// <param name="count">How many rows.</param>
    /// <param name="maxSlot">Comfortable spacing per row, and the cap.</param>
    /// <param name="padding">Space kept clear inside each slot, above and below the row.</param>
    public static ListBand Band(float top, float bottom, int count, float maxSlot,
                                float padding, float maxHeight)
    {
        ListBand band;
        band.slot = Mathf.Min(maxSlot, (top - bottom) / Mathf.Max(1, count));
        band.top = top;
        band.height = Mathf.Max(18f, Mathf.Min(maxHeight, band.slot - padding));
        // Below about 40pt of button the default 30pt label no longer fits inside it.
        band.fontSize = Mathf.Clamp(band.height * 0.38f, 15f, 30f);
        return band;
    }

    public struct ListBand
    {
        public float top;
        public float slot;
        public float height;
        public float fontSize;

        /// <summary>Centre of row i.</summary>
        public float Centre(int i) => top - slot * (i + 0.5f);

        /// <summary>Bottom edge of the last row, so what follows can be placed under it.</summary>
        public float BottomOf(int count) => top - slot * count;
    }

    /// <summary>
    /// True while the player is typing into a text box anywhere.
    /// </summary>
    /// <remarks>
    /// Every global hotkey has to check this. `R` restarts the run and `TAB` resumes, and the dev
    /// tuner puts editable number boxes on the pause screen — so without it, typing a value and
    /// happening to hit R throws away the run you were tuning. The keypress reaches
    /// `Keyboard.current` whether or not the field accepts the character, so a numeric field
    /// filtering the letter out is no protection at all.
    ///
    /// Asks the EventSystem what is selected rather than tracking fields, so it covers every box
    /// in the game including any added later.
    /// </remarks>
    public static bool Typing()
    {
        EventSystem events = EventSystem.current;
        if (events == null) return false;

        GameObject selected = events.currentSelectedGameObject;
        if (selected == null) return false;

        TMP_InputField field = selected.GetComponent<TMP_InputField>();
        return field != null && field.isFocused;
    }

    /// <summary>A single-line text box.</summary>
    /// <remarks>
    /// Built by hand rather than from a prefab, for the same reason everything else here is.
    /// TMP_InputField needs three pieces wired to each other — a viewport with a RectMask2D, a
    /// text component inside it, and a placeholder — and it silently does nothing if any of
    /// them is missing, which is a miserable thing to debug in a scene file.
    /// </remarks>
    public static TMP_InputField Field(Transform parent, string placeholder, Vector2 pos,
                                       Vector2 box, float size = 28f)
    {
        GameObject go = new GameObject("Field", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Centre((RectTransform)go.transform, pos, box);

        Image background = go.AddComponent<Image>();
        background.color = new Color(0.13f, 0.11f, 0.15f, 1f);

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(go.transform, false);
        RectTransform view = (RectTransform)viewport.transform;
        view.anchorMin = Vector2.zero;
        view.anchorMax = Vector2.one;
        view.offsetMin = new Vector2(14f, 6f);
        view.offsetMax = new Vector2(-14f, -6f);
        viewport.AddComponent<RectMask2D>();

        TextMeshProUGUI text = Text(viewport.transform, "", size, Ink,
                                    TextAlignmentOptions.Left, Vector2.zero, box);
        Stretch(text.rectTransform);

        TextMeshProUGUI hint = Text(viewport.transform, placeholder, size, Muted,
                                    TextAlignmentOptions.Left, Vector2.zero, box);
        Stretch(hint.rectTransform);

        TMP_InputField field = go.AddComponent<TMP_InputField>();
        field.targetGraphic = background;
        field.textViewport = view;
        field.textComponent = text;
        field.placeholder = hint;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.text = "";

        return field;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
