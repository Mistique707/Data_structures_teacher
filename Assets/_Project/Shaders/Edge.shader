Shader "Pivot/Edge"
{
    Properties
    {
        _ColourA ("Parent End", Color) = (0.45, 0.52, 0.95, 1)
        _ColourB ("Child End",  Color) = (0.30, 0.72, 0.95, 1)

        _AmbientLift ("Ambient Lift", Range(0.0, 2.0)) = 1.0
        _Saturate    ("Saturation",   Range(0.5, 2.0)) = 1.10

        [Header(Silhouette)]
        _EdgeDarken   ("Edge Darken",   Range(0.0, 1.0)) = 0.45
        _EdgeSaturate ("Edge Saturate", Range(1.0, 2.5)) = 1.35
        _EdgePower    ("Edge Power",    Range(0.5, 8.0)) = 2.1

        [Header(Gloss)]
        _Gloss     ("Gloss",          Range(0.0, 1.0)) = 0.35
        _SpecPower ("Specular Power", Range(4.0, 256.0)) = 90.0
        _GlossCap  ("Gloss Cap",      Range(0.1, 1.5)) = 0.45

        [Header(Traversal pulse)]
        [HDR] _PulseTint ("Pulse Tint", Color) = (1.0, 0.88, 0.40, 1)
        _PulsePosition ("Pulse Position", Range(-0.5, 1.5)) = -0.5
        _PulseWidth    ("Pulse Width",    Range(0.02, 0.6)) = 0.18
        _PulseStrength ("Pulse Strength", Range(0.0, 3.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // Edges are rubbery tubes, not lines, so they get the same shading language as
        // the bubbles: a colour that runs from parent to child, a darker saturated
        // silhouette so overlapping edges stay separable, and a capped highlight.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _ColourA;
            half4 _ColourB;
            half  _AmbientLift;
            half  _Saturate;
            half  _EdgeDarken;
            half  _EdgeSaturate;
            half  _EdgePower;
            half  _Gloss;
            half  _SpecPower;
            half  _GlossCap;
            half4 _PulseTint;
            half  _PulsePosition;
            half  _PulseWidth;
            half  _PulseStrength;
        CBUFFER_END

        // Per-edge, so the whole tree's edges are one instanced draw.
        UNITY_INSTANCING_BUFFER_START(PivotEdge)
            UNITY_DEFINE_INSTANCED_PROP(half4, _InstanceColourA)
            UNITY_DEFINE_INSTANCED_PROP(half4, _InstanceColourB)
            UNITY_DEFINE_INSTANCED_PROP(half4, _InstancePulse)   // xyz unused, w = strength
            UNITY_DEFINE_INSTANCED_PROP(half4, _InstancePulsePos) // x = position along tube
        UNITY_INSTANCING_BUFFER_END(PivotEdge)
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
                float  along      : TEXCOORD2;
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

                // Unity's cylinder runs from -1 to 1 in object Y, so this is the
                // fraction along the tube regardless of how far it has been stretched.
                output.along = saturate(input.positionOS.y * 0.5 + 0.5);
                return output;
            }

            half3 SaturateColour (half3 colour, half amount)
            {
                half luma = dot(colour, half3(0.2126, 0.7152, 0.0722));
                return lerp(luma.xxx, colour, amount);
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 a = UNITY_ACCESS_INSTANCED_PROP(PivotEdge, _InstanceColourA);
                half4 b = UNITY_ACCESS_INSTANCED_PROP(PivotEdge, _InstanceColourB);
                half4 pulse = UNITY_ACCESS_INSTANCED_PROP(PivotEdge, _InstancePulse);
                half4 pulsePos = UNITY_ACCESS_INSTANCED_PROP(PivotEdge, _InstancePulsePos);

                half3 colourA = a.a > 0.001 ? a.rgb : _ColourA.rgb;
                half3 colourB = b.a > 0.001 ? b.rgb : _ColourB.rgb;

                half3 body = SaturateColour(lerp(colourA, colourB, input.along), _Saturate);

                half3 normalWS = normalize(input.normalWS);
                half3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                half ndotv = saturate(dot(normalWS, viewWS));

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half lambert = saturate(dot(normalWS, mainLight.direction));
                half wrapped = lambert * 0.7 + 0.3;

                half3 ambient = SampleSH(normalWS) * _AmbientLift;
                half3 lit = body * (ambient + mainLight.color * wrapped * mainLight.shadowAttenuation);

                half edge = pow(1.0 - ndotv, _EdgePower);
                lit = SaturateColour(lit * lerp(1.0, 1.0 - _EdgeDarken, edge),
                                     lerp(1.0, _EdgeSaturate, edge));

                half3 halfway = normalize(mainLight.direction + viewWS);
                half specular = min(pow(saturate(dot(normalWS, halfway)), _SpecPower) * _Gloss, _GlossCap);

                // A band of light running the length of the tube: this is what the
                // traversal rides. Strength zero means the edge is idle and it costs
                // one smoothstep to say so.
                half position = pulse.a > 0.001 ? pulsePos.x : _PulsePosition;
                half strength = pulse.a > 0.001 ? pulse.a : _PulseStrength;
                half band = 1.0 - saturate(abs(input.along - position) / max(_PulseWidth, 0.001));
                half3 travelling = _PulseTint.rgb * (band * band * strength);

                return half4(lit + specular + travelling, 1.0);
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings ShadowVert (ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
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
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert (DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag (DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
