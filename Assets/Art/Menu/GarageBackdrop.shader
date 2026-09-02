// The garage's animated backdrop: a dark grid that lights up around the pointer, with a wave of
// accent colour that sweeps across when you change car.
//
// All of it is fragment maths on ONE quad — no textures, no particles, no post FX, nothing that
// touches the download. That matters because this is a menu: the whole screen is being redrawn
// for something the player looks at for ten seconds, so it has to be cheap enough that nobody
// has to think about it. Cost is a handful of frac/length/saturate per pixel and one draw call,
// against the four-car, forty-boulder scenes this engine already runs at 60.
//
// Unlit on purpose. A lit backdrop would need a light, react to the car's shadow, and pick up
// the scene's ambient — none of which is wanted for what is essentially a flat graphic.
Shader "CarCrash/Garage Backdrop"
{
    Properties
    {
        _Base        ("Background",        Color) = (0.012, 0.011, 0.015, 1)
        _Accent      ("Lit accent",       Color) = (1.0, 0.78, 0.15, 1)
        _Idle        ("Unlit lattice",    Color) = (0.105, 0.110, 0.125, 1)

        _GridScale   ("Lattice density",   Range(4, 80)) = 22
        _LineWidth   ("Line width",        Range(0.002, 0.12)) = 0.030
        _Slant       ("Slant degrees",     Range(-90, 90)) = -24

        // The lattice is faintly visible in GREY at rest and turns gold where light falls on it,
        // so the screen has structure without the gold being permanently on.
        _Ambient     ("Idle brightness",   Range(0, 1)) = 0.30

        _Pointer     ("Pointer UV",        Vector) = (0.5, 0.5, 0, 0)
        // In QUAD HEIGHTS now that the distance is aspect-corrected, so this is much smaller a
        // number than it looks: 0.10 is about a tenth of the screen height across.
        _PointerGlow ("Pointer radius",    Range(0.01, 1.2)) = 0.10
        _PointerLift ("Pointer strength",  Range(0, 4)) = 2.4

        // x = position along the sweep axis, y = direction (-1 or +1), z = width, w = strength.
        _Wave        ("Wave",              Vector) = (-2, 1, 0.16, 0)

        // x,y = centre in quad uv; z = current radius in quad heights; w = strength.
        // Two of them so a quick second click does not cut the first ring off mid-flight.
        _Ripple0     ("Ripple 0",          Vector) = (0.5, 0.5, -1, 0)
        _Ripple1     ("Ripple 1",          Vector) = (0.5, 0.5, -1, 0)
        _RippleWidth ("Ripple thickness",  Range(0.01, 0.5)) = 0.07
        _Pulse       ("Idle pulse",        Range(0, 1)) = 0.0
        _Aspect      ("Quad aspect",       Float) = 1.78
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite On

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            float4 _Base;
            float4 _Accent;
            float4 _Idle;
            float  _GridScale;
            float  _LineWidth;
            float  _Ambient;
            float  _Slant;
            float  _Aspect;
            float4 _Pointer;
            float  _PointerGlow;
            float  _PointerLift;
            float4 _Wave;
            float  _Pulse;
            float4 _Ripple0;
            float4 _Ripple1;
            float  _RippleWidth;

            // One expanding ring of light. Aspect-corrected for the same reason the pointer glow
            // is: without it the shockwave from a click is a wide ellipse, not a circle.
            float RingAt(float2 uv, float4 r, float width, float aspect)
            {
                float2 d = (uv - r.xy) * float2(aspect, 1.0);
                float ring = 1.0 - saturate(abs(length(d) - r.z) / max(0.001, width));

                // Squared, so the ring has a soft core and no hard edge where it ends.
                return ring * ring * r.w;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // --- the lattice ---------------------------------------------------------
                // THREE line families at 60 degrees to each other, not two at 90. A square grid
                // reads as graph paper; three axes give a triangular lattice whose lines trace
                // out hexagons without any cell actually being a hexagon, which is the look
                // wanted here.
                //
                // uv.x is scaled by the quad's aspect first, or the cells come out stretched
                // into long horizontal lozenges on a backdrop three times wider than it is tall.
                float2 p = float2(uv.x * _Aspect, uv.y) * _GridScale;

                float sn, cs;
                sincos(radians(_Slant), sn, cs);
                p = float2(p.x * cs - p.y * sn, p.x * sn + p.y * cs);

                // Project onto the three axes. 0.8660254 is sin(60).
                float3 axis;
                axis.x = p.y;
                axis.y = p.x * 0.8660254 + p.y * 0.5;
                axis.z = p.x * 0.8660254 - p.y * 0.5;

                // Distance to the nearest line on each axis, zero ON the line.
                float3 edge = min(frac(axis), 1.0 - frac(axis));

                // fwidth keeps the line a constant width ON SCREEN however the quad is scaled or
                // angled, which is what stops it aliasing into a moire mess toward the edges of
                // a very large backdrop.
                float3 aa = fwidth(axis) * 1.5;

                float3 w0 = _LineWidth.xxx;
                float3 lines = 1.0 - smoothstep(w0, w0 + aa, edge);

                // max, not sum: where two families cross, adding them would double the
                // brightness and stud the lattice with bright dots at every intersection.
                float grid = saturate(max(max(lines.x, lines.y), lines.z));

                // --- pointer glow --------------------------------------------------------
                // ASPECT-CORRECTED, or it is an oval. uv is the quad's, and the quad is about
                // three times wider than it is tall, so an un-corrected `length(uv - pointer)`
                // measures three times as far vertically as horizontally per unit of screen —
                // which draws a wide ellipse and reads as a smear rather than a light.
                //
                // Multiplying x by the aspect puts both axes in the same units (quad heights),
                // and because the quad is parallel to the near plane a circle here is a circle
                // on screen.
                float2 delta = (uv - _Pointer.xy) * float2(_Aspect, 1.0);

                // Squared falloff: a linear one has a visible hard edge where it reaches zero,
                // and the whole effect is meant to fade out without an obvious boundary.
                float d = length(delta) / max(0.001, _PointerGlow);
                float glow = saturate(1.0 - d);
                glow *= glow;

                // --- the sweep -----------------------------------------------------------
                // _Wave.x travels from off one edge to off the other. Projected onto x only,
                // because the arrows move horizontally and the sweep should read as going the
                // way you just clicked.
                float along = _Wave.y > 0.0 ? uv.x : 1.0 - uv.x;
                float wave = saturate(1.0 - abs(along - _Wave.x) / max(0.001, _Wave.z));
                wave = wave * wave * _Wave.w;

                // --- click shockwaves ---------------------------------------------------
                float ripple = RingAt(uv, _Ripple0, _RippleWidth, _Aspect)
                             + RingAt(uv, _Ripple1, _RippleWidth, _Aspect);

                // --- idle life -----------------------------------------------------------
                // Slow breathing so the screen is not dead when the mouse is still. Keyed off
                // uv.y as well as time so it reads as a drift rather than the whole thing
                // flashing at once.
                float pulse = (sin(_Time.y * 1.1 - uv.y * 4.0) * 0.5 + 0.5) * _Pulse;

                // How much light is falling here, from the pointer and from a passing sweep.
                float heat = saturate(glow * _PointerLift + wave * 2.2 + ripple * 2.6 + pulse * 0.35);

                // The lattice is GREY at rest and gold where it is lit, rather than gold
                // everywhere at varying brightness. Tinting is what makes the pointer read as a
                // light being carried over the surface instead of a dimmer switch.
                float3 tint = lerp(_Idle.rgb, _Accent.rgb, heat);
                float lit = _Ambient + heat * 1.7;

                // The ring also lifts the bare surface between the lines, so a shockwave reads as
                // light travelling outward rather than the lattice alone flickering.
                lit += ripple * 0.5;

                float3 colour = _Base.rgb + tint * grid * lit;

                // A much fainter lift on the empty space between the lines, so a sweep reads as
                // light travelling across a surface rather than the lines alone blinking on.
                colour += _Accent.rgb * (wave * 0.10 + ripple * 0.14);

                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
