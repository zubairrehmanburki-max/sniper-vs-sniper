// Open water for the river, marsh and sea maps. Two wave normal layers drift across each other;
// the surface mirrors the sky and the scene (reflection probe) more at grazing angles (Fresnel);
// the sun leaves a sharp glint; the water is clear and coloured by the bed where it is shallow
// and goes to its deep colour with depth (from the camera depth texture); a band of foam lies
// where it meets the shore, a pier or a hull.
Shader "SniperDuel/Water"
{
    Properties
    {
        _BumpMap ("Wave normals", 2D) = "bump" {}
        _ShallowColor ("Shallow colour", Color) = (0.16, 0.24, 0.2, 1)
        _DeepColor ("Deep colour", Color) = (0.03, 0.07, 0.07, 1)
        _Absorb ("Depth to deep colour (1/m)", Float) = 0.45
        _Clarity ("Shallow see-through", Range(0, 1)) = 0.55
        _Scale1 ("Swell tile (m)", Float) = 46
        _Scale2 ("Chop tile (m)", Float) = 13
        _Bump1 ("Swell strength", Float) = 0.75
        _Bump2 ("Chop strength", Float) = 0.45
        _Flow1 ("Swell drift (m/s)", Vector) = (0.25, 0.45, 0, 0)
        _Flow2 ("Chop drift (m/s)", Vector) = (-0.5, 0.3, 0, 0)
        _Roughness ("Reflection roughness", Range(0, 1)) = 0.08
        _Reflect ("Reflection strength", Range(0, 1)) = 0.8
        _SunPower ("Glint sharpness", Float) = 900
        _SunStrength ("Glint strength", Float) = 6
        _FoamDepth ("Foam band depth (m)", Float) = 0.6
        _FoamColor ("Foam colour", Color) = (0.85, 0.88, 0.86, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BumpMap_ST;
                half4 _ShallowColor;
                half4 _DeepColor;
                float _Absorb;
                half _Clarity;
                float _Scale1;
                float _Scale2;
                half _Bump1;
                half _Bump2;
                float4 _Flow1;
                float4 _Flow2;
                half _Roughness;
                half _Reflect;
                float _SunPower;
                half _SunStrength;
                float _FoamDepth;
                half4 _FoamColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half3 Waves(float2 xz, float t)
            {
                half3 a = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, (xz + _Flow1.xy * t) / _Scale1), _Bump1);
                half3 b = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, (xz + _Flow2.xy * t) / _Scale2), _Bump2);
                // Whiteout blend of the two layers, tangent space (z up) to world (y up).
                half3 n = normalize(half3(a.xy + b.xy, a.z * b.z));
                return normalize(half3(n.x, n.z, n.y));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 pos = input.positionWS;
                half3 n = Waves(pos.xz, _Time.y);
                half3 v = GetWorldSpaceNormalizeViewDir(pos);

                // Water depth along the view: scene behind minus the surface, in metres.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneZ = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceZ = -TransformWorldToView(pos).z;
                float thickness = max(0, sceneZ - surfaceZ);
                half deep = 1 - exp(-thickness * _Absorb);

                half3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, deep);
                Light sun = GetMainLight(TransformWorldToShadowCoord(pos));
                half lit = sun.shadowAttenuation;
                // The body takes the sky's fill plus a little sun through the surface.
                body *= SampleSH(half3(0, 1, 0)) + sun.color * lit * saturate(dot(half3(0, 1, 0), sun.direction)) * 0.35;

                half nv = saturate(dot(n, v));
                half fresnel = (0.02 + 0.98 * pow(1 - nv, 5)) * _Reflect;
                half3 r = reflect(-v, n);
                // Keep reflections above the horizon so the far water mirrors the sky, not the ground.
                r.y = abs(r.y);
                half3 reflection = GlossyEnvironmentReflection(r, _Roughness, 1);

                half3 h = normalize(sun.direction + v);
                half glint = pow(saturate(dot(n, h)), _SunPower) * _SunStrength * lit;

                half3 colour = lerp(body, reflection, fresnel) + sun.color * glint;

                // Foam where the water thins against something standing in it, broken up by the waves.
                half foamNoise = saturate(n.x * 6 + n.z * 4 + 0.5);
                // _FoamDepth 0: no foam (still marsh water).
                half shore = _FoamDepth > 0 ? saturate(1 - thickness / _FoamDepth) : 0;
                half foam = shore * step(0.35, foamNoise + 0.3 * shore);
                colour = lerp(colour, _FoamColor.rgb * (SampleSH(half3(0, 1, 0)) + sun.color * lit * 0.6), foam * 0.85);

                // Shallow water lets the bed show through; deep water and grazing views are opaque.
                half alpha = saturate(lerp(1 - _Clarity, 1, deep) + fresnel + foam);
                colour = MixFog(colour, input.fogFactor);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
