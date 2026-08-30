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

    readonly List<Renderer> painted = new List<Renderer>();
    MaterialPropertyBlock block;

    void Start()
    {
        Collect();
        Apply(colour);
    }

    void Collect()
    {
        painted.Clear();

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            if (materials == null) continue;

            foreach (Material material in materials)
            {
                if (material == null) continue;
                if (!material.name.StartsWith(paintMaterialName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                painted.Add(renderer);
                break;
            }
        }
    }

    /// <summary>Repaint. Safe to call before Start — it collects on demand.</summary>
    public void Apply(Color paint)
    {
        colour = paint;

        if (painted.Count == 0) Collect();
        block ??= new MaterialPropertyBlock();

        foreach (Renderer renderer in painted)
        {
            if (renderer == null) continue;

            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, paint);

            // URP Lit uses _BaseColor; the older Standard path uses _Color. Setting a property
            // the shader does not have is harmless, so both go in rather than sniffing.
            block.SetColor(ColorId, paint);
            renderer.SetPropertyBlock(block);
        }
    }
}
