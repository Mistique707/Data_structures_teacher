Shader "Pivot/Skybox Dusk"
{
    Properties
    {
        [HDR] _TopColour      ("Top",     Color) = (0.106, 0.118, 0.294, 1)
        [HDR] _HorizonColour  ("Horizon", Color) = (0.231, 0.165, 0.420, 1)
        [HDR] _GroundColour   ("Ground",  Color) = (0.071, 0.078, 0.180, 1)

        _HorizonWidth ("Horizon Width", Range(0.02, 1.5)) = 0.55
        _TopFalloff   ("Top Falloff",   Range(0.2, 4.0))  = 1.35
        _GroundFalloff("Ground Falloff",Range(0.2, 4.0))  = 1.10
        _Exposure     ("Exposure",      Range(0.0, 2.0))  = 1.0

        // A very slight vertical banding break, so a low-luminance gradient does not
        // show stair-stepping on a headset panel.
        _Dither ("Dither", Range(0.0, 0.02)) = 0.006
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
        ZTest LEqual

        Pass
        {
            Name "DuskGradient"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColour;
                half4 _HorizonColour;
                half4 _GroundColour;
                half  _HorizonWidth;
                half  _TopFalloff;
                half  _GroundFalloff;
                half  _Exposure;
                half  _Dither;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // TransformObjectToHClip uses the per-eye view-projection, which is
                // selected by UNITY_SETUP_INSTANCE_ID above. Each eye therefore gets
                // its own projection of the skybox mesh.
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // Unity draws the skybox on a mesh centred on the camera, so the
                // object-space position is the view ray. This stays eye-correct under
                // single pass instanced without any extra work: the attribute is the
                // same for both eyes, but the two eyes rasterise the mesh to different
                // screen positions, so the interpolated direction reaching a given
                // fragment differs per eye. Explicit per-eye camera positions would add
                // nothing, because a skybox sits at infinity and IPD produces no
                // parallax there.
                output.directionOS = input.positionOS.xyz;
                return output;
            }

            // Cheap ordered dither from screen position. Enough to break up banding
            // in a dark gradient without ever being visible as noise.
            half DitherOffset (float4 positionCS)
            {
                float2 p = positionCS.xy;
                float value = frac(dot(p, float2(0.06711056, 0.00583715)));
                return (frac(52.9829189 * value) - 0.5);
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 direction = normalize(input.directionOS);
                float height = direction.y;

                // Above the horizon, blend horizon -> top. Below it, horizon -> ground.
                half up = saturate(height / max(_HorizonWidth, 0.001));
                half down = saturate(-height / max(_HorizonWidth, 0.001));

                up = pow(up, _TopFalloff);
                down = pow(down, _GroundFalloff);

                half3 colour = _HorizonColour.rgb;
                colour = lerp(colour, _TopColour.rgb, up);
                colour = lerp(colour, _GroundColour.rgb, down);

                colour *= _Exposure;
                colour += DitherOffset(input.positionCS) * _Dither;

                return half4(max(colour, 0.0), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
