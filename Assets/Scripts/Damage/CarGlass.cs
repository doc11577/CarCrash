using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Blows every window out once the car has taken enough damage.
/// </summary>
/// <remarks>
/// Straight out of the reference footage: at 185 and 204 gears its car still has a stock
/// silhouette, but the greenhouse is simply open. Missing glass is a large part of why that car
/// reads as destroyed while ours reads as scuffed, and it is far cheaper than deformation.
///
/// The glass is not a separate object on the E30 -- split_car.py keeps it as a material SLOT on
/// the body and the doors -- so there is nothing to detach. Instead the submesh using the glass
/// material has its triangles emptied, which removes the geometry from rendering outright rather
/// than fading it to transparent. Transparent glass still costs a draw call and full overdraw for
/// something the player is meant to perceive as gone.
/// </remarks>
[DisallowMultipleComponent]
public class CarGlass : MonoBehaviour
{
    [Tooltip("Total accumulated damage at which every window goes at once. Measured reference " +
             "point: one solid wall hit is worth about 700 damage, so this is roughly a third of " +
             "a decent shunt.")]
    public float shatterAtDamage = 250f;

    [Tooltip("Material name that identifies the glass submeshes. Matched case-insensitively " +
             "against the start of the name, so Unity's ' (Instance)' suffix does not matter.")]
    public string glassMaterialName = "Glass";

    /// <summary>One glass submesh, and the triangles needed to put it back.</summary>
    class Pane
    {
        public Mesh mesh;
        public int submesh;
        public int[] triangles;
    }

    readonly List<Pane> panes = new List<Pane>();
    CarDamage damage;
    bool shattered;

    /// <summary>True once the windows have gone.</summary>
    public bool Shattered => shattered;

    void Awake()
    {
        damage = GetComponent<CarDamage>();
    }

    void OnEnable()
    {
        if (damage != null) damage.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (damage != null) damage.Damaged -= OnDamaged;
    }

    void OnDamaged(CarDamage source, float amount, Vector3 where, bool sustained, bool byPlayer)
    {
        if (shattered || damage == null) return;
        if (damage.TotalDamage < shatterAtDamage) return;

        Shatter();
    }

    /// <summary>Empty every glass submesh. Safe to call twice.</summary>
    public void Shatter()
    {
        if (shattered) return;
        shattered = true;
        panes.Clear();

        // If CarDeformation is present it has already replaced every MeshFilter's mesh with its
        // own clone, and we must write into that same clone -- swapping in another one here would
        // leave deformation writing to a mesh nothing renders. With no CarDeformation the meshes
        // are still the imported ASSETS, and emptying their triangles would permanently gut the
        // FBX in the Editor, so in that case we clone first.
        bool meshesAlreadyCloned = GetComponent<CarDeformation>() != null;

        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            Renderer renderer = filter.GetComponent<Renderer>();
            if (renderer == null || filter.sharedMesh == null) continue;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null) continue;

            // Which submeshes on this object are glass? Resolve before touching anything, so a
            // mesh with no glass at all is never cloned.
            List<int> glassSubmeshes = null;
            int count = Mathf.Min(materials.Length, filter.sharedMesh.subMeshCount);

            for (int i = 0; i < count; i++)
            {
                if (materials[i] == null) continue;
                if (!materials[i].name.StartsWith(glassMaterialName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                (glassSubmeshes ??= new List<int>()).Add(i);
            }

            if (glassSubmeshes == null) continue;

            if (!filter.sharedMesh.isReadable)
            {
                Debug.LogError("CarGlass: mesh on '" + filter.name + "' is not readable, so its " +
                               "glass cannot be removed. Tick Read/Write Enabled on the model importer.",
                               filter);
                continue;
            }

            if (!meshesAlreadyCloned)
            {
                Mesh clone = Instantiate(filter.sharedMesh);
                clone.name = filter.sharedMesh.name + " (glass)";
                filter.mesh = clone;
            }

            Mesh mesh = filter.sharedMesh;

            foreach (int submesh in glassSubmeshes)
            {
                panes.Add(new Pane
                {
                    mesh = mesh,
                    submesh = submesh,
                    triangles = mesh.GetTriangles(submesh),
                });

                mesh.SetTriangles(System.Array.Empty<int>(), submesh);
            }
        }
    }

    /// <summary>Put the windows back. Called by CarDamage.Repair.</summary>
    public void Restore()
    {
        foreach (Pane pane in panes)
            if (pane.mesh != null) pane.mesh.SetTriangles(pane.triangles, pane.submesh);

        panes.Clear();
        shattered = false;
    }
}
