using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tints a car's bodywork without creating a material per car.
/// </summary>
/// <remarks>
/// Uses a MaterialPropertyBlock rather than assigning `renderer.material`, which would
/// instantiate a copy of the material for every car and every panel that gets tinted — that is
/// how a three-car scene quietly turns one shared body material into thirty.
///
/// The E30's body texture is near-white, so a straight multiply on the base colour reads as
/// paint rather than as a colour cast.
///
/// Panels detached by <see cref="CarDamage"/> keep their tint: the property block is stored on
/// the renderer itself, and unparenting a GameObject does not disturb that. A red car sheds red
/// doors.
/// </remarks>
[DisallowMultipleComponent]
public class CarPaint : MonoBehaviour
{
    [Tooltip("Only renderers whose material name STARTS with this get painted, so wheels, glass " +
             "and the dark interior are left alone. This is a configured value rather than " +
             "something inferred, and the worst a wrong one does is tint a tyre.")]
    public string paintMaterialName = "Body";

    [Tooltip("Applied at Start. The spawner overwrites this for traffic.")]
    public Color colour = Color.white;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");

    /// <summary>One submesh to paint: a renderer and WHICH of its materials.</summary>
    struct Slot
    {
        public Renderer renderer;
        public int index;
    }

    [Header("Read-only")]
    [Tooltip("Submeshes painted. 0 means nothing matched Paint Material Name — check what the " +
             "materials are actually called on the imported model.")]
    [SerializeField] int paintedSlots;

    readonly List<Slot> slots = new List<Slot>();
    MaterialPropertyBlock block;

    void Start()
    {
        Collect();
        Apply(colour);
    }

    void Collect()
    {
        slots.Clear();

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            if (materials == null) continue;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null) continue;
                if (!material.name.StartsWith(paintMaterialName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                slots.Add(new Slot { renderer = renderer, index = i });
            }
        }

        paintedSlots = slots.Count;

        // Nothing matched, so the car keeps whatever colour its texture happens to be — and a
        // car that ignores its paint looks identical to a car that was painted that colour on
        // purpose. The De Tomaso P72's material is called `Standard32B531`, not `Body`, so it
        // stayed its native red and nothing anywhere said why.
        //
        // Listing what IS on the model turns "why is it red" into an answer rather than a hunt.
        if (slots.Count == 0)
        {
            System.Collections.Generic.HashSet<string> found =
                new System.Collections.Generic.HashSet<string>();

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null) found.Add(material.name);

            Debug.LogWarning(
                $"CarPaint on '{name}' painted nothing: no material starts with " +
                $"\"{paintMaterialName}\". Materials on this model are: " +
                $"{string.Join(", ", found)}. Set Paint Material Name to whichever one is the " +
                "bodywork.", this);
        }
    }

    /// <summary>
    /// The car's <see cref="CarPaint"/>, adding one if the prefab has none. Never returns null.
    /// </summary>
    /// <remarks>
    /// **Three of the four player prefabs had no CarPaint at all**, which is how the paint shop
    /// shipped doing nothing: `GetComponent&lt;CarPaint&gt;()` came back null and both the podium
    /// preview and the spawner quietly skipped. Traffic prefabs all had one, because traffic has
    /// been tinted since it existed — the player's car never needed painting until now.
    ///
    /// Added in code rather than by hand across four prefabs because it is a fact about how the
    /// game uses a car, not a per-car setting: EVERY car is paintable now. A prefab that wants a
    /// non-default material name still overrides it by carrying its own component, which is what
    /// the Aventador does (`Lamborginhi_base_phong`) — an added one only ever supplies the
    /// default, and `Collect` already logs every material on the model when nothing matches.
    /// </remarks>
    public static CarPaint Ensure(GameObject car)
    {
        if (car == null) return null;

        CarPaint paint = car.GetComponent<CarPaint>();
        return paint != null ? paint : car.AddComponent<CarPaint>();
    }

    /// <summary>
    /// Tint the bodywork, leaving the material's own finish alone. What traffic uses.
    /// </summary>
    public void Apply(Color paint) => Apply(paint, -1f, -1f);

    /// <summary>
    /// Tint the bodywork AND set its finish. Negative metallic or smoothness leaves that one as
    /// the material has it.
    /// </summary>
    /// <remarks>
    /// **A colour alone cannot make gold look like gold.** On a metallic surface the base colour
    /// stops being albedo and becomes the tint of the reflection, so metal is `_Metallic` plus a
    /// real reflectance value — a lighter grey with no metallic is shiny plastic.
    ///
    /// **Metal with nothing to reflect renders BLACK**, which is the trap here. Both scenes are
    /// fine as they stand: `MainMenu` has `m_AmbientMode: 0` and `m_DefaultReflectionMode: 0`, so
    /// the skybox supplies an environment even though the backdrop quad hides it from view, and
    /// every map has a real sky. **Anything that turns ambient to a flat colour, or a scene added
    /// without a skybox, will make every metallic paint look like tar** — and nothing will say
    /// why, because the mesh, the material and the colour are all still correct.
    ///
    /// Negative means "do not touch" rather than 0, because 0 is a legitimate value for both and
    /// traffic must keep whatever finish its material was authored with.
    /// </remarks>
    public void Apply(Color paint, float metallic, float smoothness)
    {
        colour = paint;

        if (slots.Count == 0) Collect();
        block ??= new MaterialPropertyBlock();

        foreach (Slot slot in slots)
        {
            if (slot.renderer == null) continue;

            // PER MATERIAL INDEX, not per renderer. A property block set on the whole renderer
            // applies to every submesh it draws, and the E30's body mesh carries [Body, Glass]
            // in one renderer -- so painting "the body" tinted the windows to match and the car
            // came out looking like a moulded toy.
            slot.renderer.GetPropertyBlock(block, slot.index);
            block.SetColor(BaseColorId, paint);

            // URP Lit uses _BaseColor; the older Standard path uses _Color. Setting a property
            // the shader does not have is harmless, so both go in rather than sniffing.
            block.SetColor(ColorId, paint);

            if (metallic >= 0f) block.SetFloat(MetallicId, Mathf.Clamp01(metallic));

            // _Smoothness is URP Lit; _Glossiness is the same idea on the Standard path. Both,
            // for the same reason the two colour properties are both set.
            if (smoothness >= 0f)
            {
                block.SetFloat(SmoothnessId, Mathf.Clamp01(smoothness));
                block.SetFloat(GlossinessId, Mathf.Clamp01(smoothness));
            }

            slot.renderer.SetPropertyBlock(block, slot.index);
        }
    }
}
