using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// One-click replacement for the stock 2.15 MB LiberationSans SDF font asset.
/// </summary>
/// <remarks>
/// **Why this exists rather than a list of Font Asset Creator steps.** The stock asset lives in
/// `Assets/TextMesh Pro/Resources/`, and everything under a Resources folder is force-included in
/// the build whether or not anything references it — so it ships in full on every release. The
/// game draws digits, a few words and some part names; a trimmed atlas is roughly 100 KB.
///
/// **The three steps have to happen together.** Deleting the stock asset before the replacement
/// exists and is assigned leaves the project with no default font, which does not error — it
/// renders every label as nothing. So this generates, verifies, retargets, and only then deletes,
/// and it bails out at the first sign of trouble with the old asset still in place.
///
/// **The character set is measured, not guessed.** Every string literal in the UI scripts, plus
/// CarRoster.asset and all five scenes, was scanned for codepoints above U+007F on 2026-09-02.
/// The entire game needs ASCII plus exactly two characters. See CLAUDE.md.
/// </remarks>
public static class FontTrim
{
    const string SourceTtf = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
    const string OutputPath = "Assets/Art/Fonts/GameFont SDF.asset";
    const string StockFont = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    const string StockFallback = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset";

    // U+00B7 is the separator in "0 gears banked · best run 0" and the patch-note bullets.
    // U+2014 is the em dash in "New map — Bullseye" and most menu subtitles.
    const char MiddleDot = '·';
    const char EmDash = '—';

    [MenuItem("CarCrash/Trim TMP Font")]
    public static void Trim()
    {
        Font ttf = AssetDatabase.LoadAssetAtPath<Font>(SourceTtf);
        if (ttf == null)
        {
            Debug.LogError($"FontTrim: could not load the source font at {SourceTtf}. Nothing " +
                           "was changed.");
            return;
        }

        // Atlas 512 is ample for ~97 glyphs. The atlas is what costs the megabytes, so it is
        // the number to keep honest — do not raise it "to be safe".
        TMP_FontAsset trimmed = TMP_FontAsset.CreateFontAsset(
            ttf, 90, 5, GlyphRenderMode.SDFAA, 512, 512,
            AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: false);

        if (trimmed == null)
        {
            Debug.LogError("FontTrim: CreateFontAsset returned null. Nothing was changed.");
            return;
        }

        trimmed.name = "GameFont SDF";

        StringBuilder set = new StringBuilder();
        for (char c = ' '; c <= '~'; c++) set.Append(c);   // printable ASCII, U+0020 to U+007E
        set.Append(MiddleDot);
        set.Append(EmDash);

        if (!trimmed.TryAddCharacters(set.ToString(), out string missing))
        {
            Debug.LogWarning($"FontTrim: the source font has no glyph for: {missing}. Those " +
                             "characters will render as blank boxes. Continuing.");
        }

        // Static, or the asset keeps a live link to the TTF and grows glyphs at runtime — which
        // would defeat the point and drag the TTF into the build alongside it.
        trimmed.atlasPopulationMode = AtlasPopulationMode.Static;

        AssetDatabase.CreateAsset(trimmed, OutputPath);

        // The atlas texture and material are separate objects and must be nested inside the
        // asset, or they are lost on reload and the font renders as solid blocks.
        if (trimmed.atlasTextures != null && trimmed.atlasTextures.Length > 0)
        {
            trimmed.atlasTextures[0].name = trimmed.name + " Atlas";
            AssetDatabase.AddObjectToAsset(trimmed.atlasTextures[0], trimmed);
        }

        if (trimmed.material != null)
        {
            trimmed.material.name = trimmed.name + " Material";
            AssetDatabase.AddObjectToAsset(trimmed.material, trimmed);
        }

        EditorUtility.SetDirty(trimmed);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Verify before touching anything. A font asset with no glyph table is worse than the
        // 2 MB one it would replace.
        TMP_FontAsset check = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath);
        if (check == null || check.characterTable == null || check.characterTable.Count == 0)
        {
            Debug.LogError("FontTrim: the generated asset has no characters. The stock font has " +
                           "NOT been touched — fix this before deleting anything.");
            return;
        }

        if (!Retarget(check))
        {
            Debug.LogError("FontTrim: could not set the TMP default font. The stock font has NOT " +
                           "been deleted, so the game still renders. Set Default Font Asset on " +
                           "TMP Settings by hand, then delete the stock asset.");
            return;
        }

        AssetDatabase.DeleteAsset(StockFallback);
        AssetDatabase.DeleteAsset(StockFont);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"FontTrim: done. '{check.name}' has {check.characterTable.Count} characters, " +
                  $"saved to {OutputPath} (outside Resources, so it ships only because TMP " +
                  "Settings references it). The 2.15 MB stock asset is gone. Open the menu and " +
                  "check every label still reads correctly.");
    }

    /// <summary>
    /// Point TMP Settings at the new asset. The field is private, so it goes through
    /// SerializedObject rather than a setter that does not exist.
    /// </summary>
    static bool Retarget(TMP_FontAsset font)
    {
        TMP_Settings settings = Resources.Load<TMP_Settings>("TMP Settings");
        if (settings == null) return false;

        SerializedObject so = new SerializedObject(settings);
        SerializedProperty prop = so.FindProperty("m_defaultFontAsset");
        if (prop == null) return false;

        prop.objectReferenceValue = font;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(settings);
        return true;
    }
}
