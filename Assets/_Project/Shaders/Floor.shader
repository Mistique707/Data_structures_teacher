Shader "Pivot/Floor"
{
    Properties
    {
        _BaseColour ("Base",      Color) = (0.129, 0.141, 0.310, 1)
        _GridColour ("Grid Line", Color) = (0.35, 0.33, 0.62, 0.35)

        _GridSpacing  ("Grid Spacing (m)", Range(0.05, 4.0)) = 0.5
        _GridThickness("Grid Thickness",   Range(0.002, 0.08)) = 0.012

        _FadeStart ("Fade Start (m)", Range(0.5, 40.0)) = 3.0
        _FadeEnd   ("Fade End (m)",   Range(1.0, 80.0)) = 14.0

        _AmbientLift    ("Ambient Lift",    Range(0.0, 2.0)) = 1.0
        _ShadowStrength ("Shadow Strength", Range(0.0, 1.0)) = 0.6
        _EdgeDarken     ("Edge Darken",     Range(0.0, 1.0)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // Shared declarations. Every pass uses this one CBUFFER, which is what keeps
        // the shader SRP Batcher compatible.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColour;
            half4 _GridColour;
            half  _GridSpacing;
            half  _GridThickness;
            half  _FadeStart;
            half  _FadeEnd;
            half  _AmbientLift;
            half  _ShadowStrength;
            half  _EdgeDarken;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // Screen-space derivatives keep the lines a constant width on screen at
            // any distance, which is what stops a grid shimmering under head motion.
            half GridMask (float2 planeXZ, half spacing, half thickness)
            {
                float2 cell = planeXZ / max(spacing, 0.0001);
                float2 gradient = fwidth(cell);
                float2 distanceToLine = abs(frac(cell - 0.5) - 0.5);
                float2 pixels = distanceToLine / max(gradient, 0.00001);

                half thick = max(thickness / max(spacing, 0.0001), 0.001);
                half2 mask = saturate(1.0 - smoothstep(thick, thick + 1.0, pixels));
                return max(mask.x, mask.y);
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = input.positionWS;

                // Radial fade so the floor dissolves into the dusk rather than ending
                // on a hard rim. Measured from the lab origin, not the camera, so it
                // does not swim when the user walks around.
                float radial = length(positionWS.xz);
                half fade = 1.0 - saturate((radial - _FadeStart) / max(_FadeEnd - _FadeStart, 0.001));

                half grid = GridMask(positionWS.xz, _GridSpacing, _GridThickness) * fade;
                half3 albedo = lerp(_BaseColour.rgb, _GridColour.rgb, grid * _GridColour.a);

                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 normalWS = normalize(input.normalWS);
                half lambert = saturate(dot(normalWS, mainLight.direction));
                half shadow = lerp(1.0, mainLight.shadowAttenuation, _ShadowStrength);

                half3 ambient = SampleSH(normalWS) * _AmbientLift;
                half3 lit = albedo * (ambient + mainLight.color * lambert * shadow);

                lit *= lerp(1.0 - _EdgeDarken, 1.0, fade);

                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings ShadowVert (ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag (ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 2.0
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert (DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag (DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
