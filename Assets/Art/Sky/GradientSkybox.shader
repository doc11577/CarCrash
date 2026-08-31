// A three-colour gradient skybox: sky, horizon, ground.
//
// Why not Unity's Skybox/Procedural: that one computes atmospheric scattering and a sun disc
// every pixel to derive a sky colour we would then be fighting to control. This costs LESS than
// the default — two lerps and a pow against a scattering model — and gives exact art direction,
// which is the point when the whole job is "make Quarry warm and Everest cold".
//
// Still one skybox draw, same as the default, so the answer to "does this cost more" is no.
//
// SET THE FOG COLOUR TO MATCH THE HORIZON COLOUR. On a 2 km course the far end of the map is the
// thing that gives away its size, and terrain that fades into the sky instead of ending in a hard
// edge is most of what makes a big map feel big. URP fog is per-pixel in the lit shader and is
// already being computed.
Shader "CarCrash/Gradient Skybox"
{
    Properties
    {
        _SkyColor     ("Sky",              Color) = (0.35, 0.50, 0.78, 1)
        _HorizonColor ("Horizon",          Color) = (0.74, 0.82, 0.90, 1)
        _GroundColor  ("Below horizon",    Color) = (0.20, 0.20, 0.22, 1)
        _SkySpread    ("Sky spread",       Range(0.2, 6)) = 1.8
        _GroundSpread ("Ground spread",    Range(0.2, 6)) = 2.2
        _Exposure     ("Exposure",         Range(0, 4)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction  : TEXCOORD0;
            };

            float4 _SkyColor;
            float4 _HorizonColor;
            float4 _GroundColor;
            float  _SkySpread;
            float  _GroundSpread;
            float  _Exposure;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // Object space on the skybox mesh IS the view direction, which is why this needs
                // no camera maths and works at any rotation.
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float height = dir.y;
                float t = saturate(abs(height));

                // Spread > 1 pushes the horizon band wider, which is what reads as haze.
                float3 above = lerp(_HorizonColor.rgb, _SkyColor.rgb, pow(t, 1.0 / _SkySpread));
                float3 below = lerp(_HorizonColor.rgb, _GroundColor.rgb, pow(t, 1.0 / _GroundSpread));

                float3 colour = height >= 0.0 ? above : below;
                return half4(colour * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
