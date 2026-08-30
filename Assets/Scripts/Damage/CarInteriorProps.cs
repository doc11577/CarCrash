using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a handful of dark boxes inside the car — engine block, dashboard, seats, floor pan,
/// boot floor — so that a missing panel reveals something rather than a smooth black void.
/// </summary>
/// <remarks>
/// The reference footage is the argument for this. Its cavities are not empty: a missing hood
/// shows engine detail, a missing door shows a cabin, and the underside has a floor pan and
/// exhaust. Ours had `InteriorShell`, which is a smooth shrunken copy of the bodywork and reads
/// as a flat black hole.
///
/// Generated rather than modelled because the E30 source has no interior to reveal — the OBJ
/// contains exactly four objects (body, glass, two wheel clusters), so there is nothing for
/// split_car.py to keep. Generating in Unity also avoids a re-export and a full re-wire.
///
/// Cost is deliberately trivial: seven boxes welded into ONE mesh with ONE material, so 84
/// triangles and a single draw call. They sit inside the bodywork, so they are invisible until
/// something comes off, and they are built in Start() rather than Awake() so CarDeformation has
/// already collected its panel list and will not try to dent a solid engine block.
///
/// MEASURED BODY BOUNDS, in the car's local space, ground at y = 0, +z forward. Every default
/// box is sized against these. Take them from tools/blender/car_bounds.py after any re-export --
/// do NOT re-derive them, and do not trust an older figure written down elsewhere:
///
///   Body           x -0.820 .. 0.863   y 0.145 .. 1.355   z -2.578 .. 1.586
///   InteriorShell  x -0.763 .. 0.805   y 0.187 .. 1.306   z -2.499 .. 1.524
///   PartHood       x -0.761 .. 0.785   y 0.671 .. 0.912   z  0.240 .. 1.346
///   PartTrunk      x -0.765 .. 0.797   y 0.733 .. 1.284   z -2.386 .. -1.519
///   PartDoorL      x -0.801 ..-0.168   y 0.271 .. 1.082   z -1.248 .. 0.306
///   PartDoorR      x  0.219 .. 0.843   y 0.271 .. 1.082   z -1.248 .. 0.306
///
/// THE HOOD IS A SLOPE, NOT A CEILING, AND THAT IS THE TRAP. Its surface falls from 0.912 at
/// the cowl to 0.671 at the nose -- roughly 0.218 m of headroom lost per metre forward. A box
/// with a flat top therefore has least clearance at its FRONT edge, which is exactly where the
/// old dash and engine broke through. Available headroom at a given z is about
/// `0.912 - 0.218 * (z - 0.240)`.
/// </remarks>
[DisallowMultipleComponent]
public class CarInteriorProps : MonoBehaviour
{
    [System.Serializable]
    public struct Prop
    {
        public string name;
        public Vector3 center;
        public Vector3 size;
    }

    [Tooltip("Dark material for the interior. Leave empty to borrow whatever the InteriorShell " +
             "already uses, which is the CarInterior material split_car.py authored.")]
    public Material interiorMaterial;

    [Tooltip("Boxes to build, in the CAR's local space with the ground at y = 0 and +z forward. " +
             "Select the car to see them drawn as gizmos while you adjust them. Every default " +
             "below is checked against the MEASURED body bounds in the remarks -- if you move " +
             "one, check it against those rather than against the old figures in CLAUDE.md, " +
             "which were wrong and are what put the dash through the bonnet.")]
    public Prop[] props = new Prop[]
    {
        // Top at y 0.68. The hood surface falls from 0.912 at the cowl to 0.671 at the nose,
        // so a box that reaches z 1.20 has only about 0.70 of headroom there -- this is the
        // prop with the least clearance and the one to re-check first after any change.
        new Prop { name = "engine",     center = new Vector3(0f,     0.48f,  0.85f), size = new Vector3(0.80f, 0.40f, 0.70f) },

        // Top at y 0.89, just under the 0.912 scuttle, and held BEHIND the cowl at z 0.24.
        // It used to top out at 0.96 and reach z 0.65, which put it straight through the
        // bonnet where the windscreen meets it.
        new Prop { name = "dash",       center = new Vector3(0f,     0.80f,  0.05f), size = new Vector3(1.42f, 0.18f, 0.34f) },

        // Nudged back so the front-top corner cannot clip the windscreen base: at z 0.26 the
        // bodywork is only 0.908 high and the seat backs reach 0.98.
        new Prop { name = "seatL",      center = new Vector3(-0.34f, 0.72f, -0.06f), size = new Vector3(0.44f, 0.52f, 0.48f) },
        new Prop { name = "seatR",      center = new Vector3( 0.34f, 0.72f, -0.06f), size = new Vector3(0.44f, 0.52f, 0.48f) },

        new Prop { name = "rearBench",  center = new Vector3(0f,     0.70f, -0.62f), size = new Vector3(1.30f, 0.48f, 0.44f) },
        new Prop { name = "floorPan",   center = new Vector3(0f,     0.36f, -0.35f), size = new Vector3(1.48f, 0.08f, 2.55f) },

        // Sits at y 0.58-0.66, comfortably under the boot lid, which starts at 0.733.
        new Prop { name = "bootFloor",  center = new Vector3(0f,     0.62f, -1.85f), size = new Vector3(1.36f, 0.08f, 0.95f) },
    };

    GameObject built;

    void Start()
    {
        if (props == null || props.Length == 0) return;

        Material material = interiorMaterial != null ? interiorMaterial : BorrowShellMaterial();
        if (material == null)
        {
            // Not fatal, but the props would render in Unity's magenta error material, which is
            // far more obvious on screen than a missing interior. Better to build nothing.
            Debug.LogError("CarInteriorProps: no material. Assign one, or give the car an " +
                           "InteriorShell to borrow from.", this);
            return;
        }

        built = new GameObject("InteriorProps");
        built.transform.SetParent(transform, false);
        built.layer = gameObject.layer;

        built.AddComponent<MeshFilter>().mesh = BuildMesh();
        MeshRenderer renderer = built.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;

        // Nothing here ever needs to cast or receive a shadow: it is only ever seen through a
        // hole, and realtime shadows are off in this project anyway.
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    Material BorrowShellMaterial()
    {
        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
            if (renderer.name == "InteriorShell") return renderer.sharedMaterial;

        return null;
    }

    /// <summary>All the boxes welded into one mesh, so the whole interior is a single draw call.</summary>
    Mesh BuildMesh()
    {
        List<Vector3> verts = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> tris = new List<int>();

        foreach (Prop prop in props)
            AppendBox(prop.center, prop.size, verts, normals, tris);

        Mesh mesh = new Mesh { name = "InteriorProps" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// One box, with its faces split so the lighting stays hard-edged rather than rounding a
    /// 12-triangle block into a blob.
    /// </summary>
    static void AppendBox(Vector3 centre, Vector3 size, List<Vector3> verts, List<Vector3> normals, List<int> tris)
    {
        Vector3 h = size * 0.5f;

        // Face order: +X, -X, +Y, -Y, +Z, -Z. Each gets its own four vertices.
        Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        foreach (Vector3 normal in axes)
        {
            // Two vectors spanning the face.
            Vector3 u = new Vector3(normal.y, normal.z, normal.x);
            Vector3 v = Vector3.Cross(normal, u);

            Vector3 faceCentre = centre + Vector3.Scale(normal, h);
            Vector3 su = Vector3.Scale(u, h);
            Vector3 sv = Vector3.Scale(v, h);

            int baseIndex = verts.Count;

            verts.Add(faceCentre - su - sv);
            verts.Add(faceCentre + su - sv);
            verts.Add(faceCentre + su + sv);
            verts.Add(faceCentre - su + sv);

            for (int i = 0; i < 4; i++) normals.Add(normal);

            tris.Add(baseIndex);
            tris.Add(baseIndex + 2);
            tris.Add(baseIndex + 1);
            tris.Add(baseIndex);
            tris.Add(baseIndex + 3);
            tris.Add(baseIndex + 2);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (props == null) return;

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        Gizmos.matrix = transform.localToWorldMatrix;

        foreach (Prop prop in props)
            Gizmos.DrawWireCube(prop.center, prop.size);
    }
}
