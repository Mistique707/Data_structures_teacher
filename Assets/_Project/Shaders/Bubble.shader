Shader "Pivot/Bubble"
{
    Properties
    {
        [MainColor] _BaseColour ("Base", Color) = (0.30, 0.62, 1.0, 1)

        [Header(Body)]
        _TopLift    ("Top Lift",    Range(0.0, 1.0)) = 0.34
        _BottomSink ("Bottom Sink", Range(0.0, 1.0)) = 0.28
        _Saturate   ("Saturation",  Range(0.5, 2.0)) = 1.12

        [Header(Gloss)]
        [HDR] _SpecularTint ("Specular Tint", Color) = (1, 1, 1, 1)
        _Gloss      ("Gloss",           Range(0.0, 1.0)) = 0.55
        _SpecPower  ("Specular Power",  Range(4.0, 256.0)) = 150.0
        _GlossCap   ("Gloss Cap",       Range(0.2, 1.5)) = 0.72

        [Header(Fixed glint)]
        _GlintStrength ("Glint Strength", Range(0.0, 2.0)) = 0.55
        _GlintSize     ("Glint Size",     Range(0.01, 0.3)) = 0.055
        _GlintDir      ("Glint Direction (object space)", Vector) = (-0.34, 0.80, -0.50, 0)

        [Header(Edge separation)]
        _EdgeDarken   ("Edge Darken",     Range(0.0, 1.0)) = 0.52
        _EdgeSaturate ("Edge Saturate",   Range(1.0, 2.5)) = 1.45
        _EdgePower    ("Edge Power",      Range(0.5, 8.0)) = 1.9

        [Header(Rim)]
        _RimStrength ("Rim Strength", Range(0.0, 2.0)) = 0.42
        _RimPower    ("Rim Power",    Range(0.5, 24.0)) = 11.0
        [HDR] _RimTint ("Rim Tint", Color) = (0.82, 0.90, 1.0, 1)

        [Header(Inner glow)]
        _InnerGlow ("Inner Glow", Range(0.0, 1.5)) = 0.22

        [Header(Legibility guard)]
        _GuardRadius   ("Guard Radius",    Range(0.0, 1.0)) = 0.62
        _GuardSoftness ("Guard Softness",  Range(0.01, 0.6)) = 0.24
        _GuardSpecKill ("Guard Spec Kill", Range(0.0, 1.0)) = 0.85
        _GuardDarken   ("Guard Darken",    Range(0.0, 0.6)) = 0.16

        [Header(State)]
        [HDR] _Highlight ("Highlight Tint", Color) = (0, 0, 0, 0)
        _HighlightAmount ("Highlight Amount", Range(0.0, 1.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // Opaque on purpose. The reference read is a solid glossy orb, and with fifteen
        // bubbles overlapping on a tiled GPU, layered transparency is the one thing
        // guaranteed to wreck fill rate. Everything that says "glass" here is faked on
        // an opaque surface: a vertical body gradient, a light-driven specular lobe, a
        // fixed glint that keeps the orb reading the same from any angle, a Fresnel rim,
        // and an inner glow. No post-processing is involved in any of it.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4  _BaseColour;
            half   _TopLift;
            half   _BottomSink;
            half   _Saturate;
            half4  _SpecularTint;
            half   _Gloss;
            half   _SpecPower;
            half   _GlossCap;
            half   _GlintStrength;
            half   _GlintSize;
            float4 _GlintDir;
            half   _EdgeDarken;
            half   _EdgeSaturate;
            half   _EdgePower;
            half   _RimStrength;
            half   _RimPower;
            half4  _RimTint;
            half   _InnerGlow;
            half   _GuardRadius;
            half   _GuardSoftness;
            half   _GuardSpecKill;
            half   _GuardDarken;
            half4  _Highlight;
            half   _HighlightAmount;
        CBUFFER_END

        // Per-node colour and state live here rather than in the material, so two
        // hundred bubbles are one mesh, one material and one instanced draw. A
        // MaterialPropertyBlock writing these bypasses the SRP Batcher and lands in
        // the instancing path, which is the cheaper of the two for identical spheres.
        UNITY_INSTANCING_BUFFER_START(PivotBubble)
            UNITY_DEFINE_INSTANCED_PROP(half4, _InstanceColour)
            UNITY_DEFINE_INSTANCED_PROP(half4, _InstanceHighlight)
        UNITY_INSTANCING_BUFFER_END(PivotBubble)
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
                float3 normalOS   : TEXCOORD2;
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

                // Kept in object space so the fixed glint and the body gradient rotate
                // with the bubble instead of swimming when it is grabbed and turned.
                output.normalOS = normalize(input.normalOS);
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

                half4 instanceColour = UNITY_ACCESS_INSTANCED_PROP(PivotBubble, _InstanceColour);
                half4 instanceHighlight = UNITY_ACCESS_INSTANCED_PROP(PivotBubble, _InstanceHighlight);

                // A zero instance colour means nothing wrote a block, so fall back to
                // the material's own colour and stay visible in the Inspector preview.
                half3 baseColour = instanceColour.a > 0.001 ? instanceColour.rgb : _BaseColour.rgb;

                half3 normalWS = normalize(input.normalWS);
                half3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                half ndotv = saturate(dot(normalWS, viewWS));

                // ---- body -------------------------------------------------------
                // Lighter towards the top, deeper towards the bottom. Object space, so
                // the gradient belongs to the bubble rather than to the world.
                // The lift is multiplicative, not additive. Adding a constant to every
                // channel drags dark colours towards white and the deeper depths turn
                // milky, which destroys the depth coding the tree is read by; scaling
                // keeps the hue and only changes the brightness.
                half vertical = input.normalOS.y * 0.5 + 0.5;
                half3 body = baseColour;
                body = lerp(body, saturate(body * (1.0 + _TopLift * 2.0)),
                            smoothstep(0.45, 1.0, vertical));
                body = lerp(body, body * (1.0 - _BottomSink), smoothstep(0.55, 0.0, vertical));
                body = SaturateColour(body, _Saturate);

                // ---- legibility guard -------------------------------------------
                // The number is the product. This is a disc facing the viewer, sized in
                // silhouette fractions, inside which the specular and the glint are
                // suppressed and the body is nudged darker. It exists so a white glossy
                // highlight can never sit underneath a white glyph and erase it.
                //
                // The silhouette is what actually sells "glossy", and the guard never
                // touches it: the rim lives at low ndotv, the guard at high ndotv.
                half cosEdge = sqrt(saturate(1.0 - _GuardRadius * _GuardRadius));
                half guard = smoothstep(cosEdge - _GuardSoftness, cosEdge + _GuardSoftness, ndotv);

                // ---- lighting ---------------------------------------------------
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half lambert = saturate(dot(normalWS, mainLight.direction));
                half wrapped = lambert * 0.72 + 0.28;   // wrapped diffuse keeps the dark side readable

                half3 ambient = SampleSH(normalWS);
                half3 lit = body * (ambient + mainLight.color * wrapped * mainLight.shadowAttenuation);

                // ---- edge separation --------------------------------------------
                // The single most important thing at bin density. With fifteen orbs
                // overlapping, a bright rim makes neighbours melt together; a deep,
                // more saturated edge is what lets one bubble read as in front of
                // another. Darker and richer towards the silhouette, applied to the
                // lit result so it survives whatever the light is doing.
                half edge = pow(1.0 - ndotv, _EdgePower);
                lit = SaturateColour(lit * lerp(1.0, 1.0 - _EdgeDarken, edge),
                                     lerp(1.0, _EdgeSaturate, edge));

                // ---- specular ---------------------------------------------------
                half3 halfway = normalize(mainLight.direction + viewWS);
                half ndoth = saturate(dot(normalWS, halfway));
                half specular = pow(ndoth, _SpecPower) * _Gloss;

                // ---- fixed glint ------------------------------------------------
                // The reference orbs carry a small highlight blob that reads the same
                // from every angle because it is painted into the art. Reproduced in
                // object space so the bubble keeps its identity as it tumbles. Kept
                // deliberately tight: a broad one erases the hue and the orbs stop
                // being colour-coded, which is what the tree is read by.
                half3 glintDir = normalize(_GlintDir.xyz);
                half glintDot = saturate(dot(input.normalOS, glintDir));
                half glint = smoothstep(1.0 - _GlintSize, 1.0, glintDot) * _GlintStrength;

                half specMask = lerp(1.0, 1.0 - _GuardSpecKill, guard);

                // Capped, so no combination of light angle and glint can ever blow a
                // bubble to white. Hue is information here, not decoration.
                half glossAmount = min((specular + glint) * specMask, _GlossCap);
                half3 gloss = _SpecularTint.rgb * glossAmount;

                // ---- rim --------------------------------------------------------
                // A thin bright line just inside the dark edge: the shell catching
                // light. High power on purpose, so it stays a line rather than a haze.
                half fresnel = pow(1.0 - ndotv, _RimPower) * _RimStrength;
                half3 rim = _RimTint.rgb * fresnel;

                // ---- inner glow -------------------------------------------------
                // Sits between the body and the dark edge, which is where a real shell
                // would gather light. Not a bloom substitute, and it does not need one.
                half inner = smoothstep(0.15, 0.8, 1.0 - ndotv) * (1.0 - edge) * _InnerGlow;
                half3 glow = body * inner;

                half3 colour = lit + gloss + rim + glow;

                // The guard darkens last, so it also calms the rim and glow bleeding
                // into the middle without flattening the edges.
                colour *= lerp(1.0, 1.0 - _GuardDarken, guard);

                // ---- state tint -------------------------------------------------
                half highlightAmount = max(_HighlightAmount, instanceHighlight.a);
                half3 highlightTint = instanceHighlight.a > 0.001 ? instanceHighlight.rgb : _Highlight.rgb;
                colour = lerp(colour, highlightTint, highlightAmount);

                return half4(colour, 1.0);
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
