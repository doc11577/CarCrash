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
    }

    /// <summary>Repaint. Safe to call before Start — it collects on demand.</summary>
    public void Apply(Color paint)
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
            slot.renderer.SetPropertyBlock(block, slot.index);
        }
    }
}
