using UnityEngine;

/// <summary>
/// Black rubber left on the road when a tyre is sliding or spinning up.
/// </summary>
/// <remarks>
/// TRAIL RENDERERS, one per wheel, rather than a decal system or a mesh that grows. A trail is a
/// strip of quads Unity already maintains and culls, it has a lifetime built in so nothing has to
/// be pooled or expired, and it costs one draw call for the whole car because all four share a
/// material. A custom skid-mesh system is the "right" answer in a game that can afford it; this
/// one is measured against a Jasper Lake Chromebook, and four trails is the version that fits.
///
/// ⚠ ATTACHED TO THE PLAYER'S CAR ONLY. Eight cars' worth is thirty-two trails, each accumulating
/// geometry for its whole lifetime, and the chase camera means nobody is looking at the marks an
/// AI leaves. If they ever are, it should be the two REAR wheels only.
///
/// NOTHING IS DOWNLOADED. The strip's soft edge is a texture generated in code — twelve pixels of
/// alpha ramp — so this adds nothing to a build that is already over its download budget, and
/// there is no third-party licence to record.
///
/// The trail is deliberately NOT parented to the wheel. A trail renderer records its own
/// transform's position each frame; parenting it to a spinning, steering, suspension-riding wheel
/// mesh would record all of that. It rides a plain child object pinned to the contact patch.
/// </remarks>
[RequireComponent(typeof(CarController))]
[DisallowMultipleComponent]
public class SkidMarks : MonoBehaviour
{
    [Tooltip("Material for the marks. Leave EMPTY and one is built in code from the URP unlit " +
             "shader.\n\n" +
             "⚠ ASSIGN ONE FOR A WEB BUILD. A shader only reaches a build if something at BUILD " +
             "TIME depends on it, and a material created at runtime is not that — the same trap " +
             "that shipped Update 2 with no garage backdrop. A material asset dragged in here is " +
             "a real dependency; a runtime one works perfectly in the Editor and vanishes in the " +
             "build, leaving no marks and no error.")]
    public Material markMaterial;

    [Tooltip("Width of a mark in metres. A little under the tyre, so the pair read as two " +
             "stripes rather than one band.")]
    public float width = 0.24f;

    [Tooltip("Seconds a mark stays on the road. Longer looks better and costs geometry — every " +
             "second of trail is vertices held alive per wheel.")]
    public float lifetime = 5f;

    [Tooltip("Sideslip in degrees at which the tyres start to mark. Below this the car is " +
             "cornering and a real tyre leaves nothing.")]
    public float slideAngle = 12f;

    [Tooltip("Marks also appear under hard acceleration below this speed, in m/s — wheelspin off " +
             "the line. Above it the tyres have hooked up and only sliding marks.")]
    public float spinUpSpeed = 9f;

    [Tooltip("Minimum speed for any mark at all, in m/s. Stops a parked car drawing on the road " +
             "while it settles on its springs.")]
    public float minSpeed = 3f;

    [Tooltip("Metres above the contact point the mark is drawn. Just enough to beat z-fighting " +
             "with the road; more and the marks visibly float.")]
    public float lift = 0.02f;

    /// <summary>
    /// Trails per wheel. TWO, used alternately.
    /// </summary>
    /// <remarks>
    /// ⚠ A TrailRenderer switched off and on again CONNECTS the two segments — you get a straight
    /// black line from where the last skid ended to where the next one begins, straight across
    /// the map. Clearing it instead deletes the marks still fading on the road, so the only skid
    /// you can ever see is the one happening now.
    ///
    /// Two slots used alternately fixes both: the finished skid is left alone to fade while the
    /// next one starts on the other slot. This is the same trick, for the same reason, as the
    /// two shockwave rings in CarPodium.
    /// </remarks>
    const int SlotsPerWheel = 2;

    CarController car;
    Rigidbody body;
    TrailRenderer[] trails;
    Transform[] emitters;
    int[] slot;
    bool[] wasMarking;

    void Awake()
    {
        car = GetComponent<CarController>();
        body = GetComponent<Rigidbody>();

        if (car.wheels == null || car.wheels.Length == 0)
        {
            enabled = false;
            return;
        }

        Material material = markMaterial != null ? markMaterial : BuildMaterial();
        if (material == null)
        {
            enabled = false;
            return;
        }

        int count = car.wheels.Length * SlotsPerWheel;
        trails = new TrailRenderer[count];
        emitters = new Transform[count];
        slot = new int[car.wheels.Length];
        wasMarking = new bool[car.wheels.Length];

        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject($"Skid{i / SlotsPerWheel}_{i % SlotsPerWheel}");
            go.transform.SetParent(transform, false);
            emitters[i] = go.transform;

            TrailRenderer trail = go.AddComponent<TrailRenderer>();
            trail.material = material;
            trail.time = lifetime;
            trail.startWidth = width;
            trail.endWidth = width;
            trail.numCapVertices = 0;
            trail.numCornerVertices = 0;
            trail.minVertexDistance = 0.25f;
            trail.autodestruct = false;
            trail.emitting = false;

            // No shadows either way. A flat black strip lying on the road cannot usefully cast
            // one, and receiving would darken it into invisibility. Realtime shadows are off
            // project-wide in any case.
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            // Fades out over its life rather than vanishing. A mark that disappears in one frame
            // reads as a glitch; rubber on tarmac gets rubbed away.
            trail.colorGradient = new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(Color.black, 0f),
                    new GradientColorKey(Color.black, 1f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0.55f, 0f),
                    new GradientAlphaKey(0.45f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                },
            };

            trails[i] = trail;
        }
    }

    /// <summary>Last-resort material, built in code. See the warning on <see cref="markMaterial"/>.</summary>
    Material BuildMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogWarning(
                "SkidMarks found no URP Unlit shader, so no tyre marks will be drawn. Assign a " +
                "Mark Material in the Inspector — and do that anyway before a Web build, because " +
                "a shader nothing references at build time is stripped out of it.", this);
            return null;
        }

        Material material = new Material(shader) { name = "SkidMark (runtime)" };

        // Transparent, so the road shows through the rubber. Set by hand because a runtime
        // material starts from the shader's defaults, which are opaque.
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_ZWrite", 0f);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        material.mainTexture = BuildTexture();
        return material;
    }

    /// <summary>A soft-edged strip, generated rather than imported.</summary>
    /// <remarks>
    /// Sixteen pixels across, opaque in the middle and fading at both edges, so the mark has a
    /// worn edge instead of a hard cut. Point-filtered would band; bilinear over sixteen pixels
    /// stretched to a quarter of a metre is smooth.
    /// </remarks>
    static Texture2D BuildTexture()
    {
        const int size = 16;
        Texture2D texture = new Texture2D(size, 1, TextureFormat.RGBA32, mipChain: false)
        {
            name = "SkidMark",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        for (int x = 0; x < size; x++)
        {
            // 0 at both edges, 1 across the middle.
            float across = Mathf.Abs(x / (size - 1f) - 0.5f) * 2f;
            float alpha = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.55f, 1f, across));
            texture.SetPixel(x, 0, new Color(0f, 0f, 0f, alpha));
        }

        texture.Apply();
        return texture;
    }

    void LateUpdate()
    {
        if (trails == null) return;

        float speed = body != null ? body.linearVelocity.magnitude : 0f;
        float slip = Mathf.Abs(car.Sideslip);

        // Two ways to mark: sliding, and spinning the wheels up from low speed. The second is
        // what puts a mark down when you plant it off the line, which is most of what makes
        // acceleration feel violent rather than merely fast.
        bool sliding = slip >= slideAngle;
        bool spinningUp = speed < spinUpSpeed && car.Driver != null && car.Driver.Throttle > 0.6f;
        bool handbrake = car.Driver != null && car.Driver.Handbrake;

        bool marking = speed >= minSpeed && (sliding || spinningUp || handbrake);

        for (int i = 0; i < car.wheels.Length; i++)
        {
            CarController.Wheel wheel = car.wheels[i];
            if (wheel == null) continue;

            // Only a wheel that is ON the road, and still attached to the car, marks it.
            bool draw = marking && wheel.grounded && !wheel.detached;

            // Wheelspin marks come from the DRIVEN wheels only. A front tyre being dragged along
            // does not lay rubber, and marking all four off the line reads as the car sliding
            // rather than accelerating.
            if (spinningUp && !sliding && !handbrake && !wheel.powered) draw = false;

            // A NEW skid takes the other slot, leaving the previous one to fade where it lies.
            if (draw && !wasMarking[i])
            {
                slot[i] = (slot[i] + 1) % SlotsPerWheel;

                TrailRenderer fresh = trails[i * SlotsPerWheel + slot[i]];
                emitters[i * SlotsPerWheel + slot[i]].position =
                    wheel.contactPoint + Vector3.up * lift;

                // Cleared AFTER the emitter is moved, so the trail cannot record one segment
                // from wherever this slot was last used to where the car is now.
                fresh.Clear();
            }

            wasMarking[i] = draw;

            int index = i * SlotsPerWheel + slot[i];
            if (draw) emitters[index].position = wheel.contactPoint + Vector3.up * lift;
            trails[index].emitting = draw;
        }
    }
}
