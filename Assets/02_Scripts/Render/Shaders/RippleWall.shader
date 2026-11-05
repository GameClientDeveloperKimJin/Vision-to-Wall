// FR-07 media wall background: vertical gradient + ripple distortion + glow. URP Unlit.
// Driven by MediaWallController:
//   _Ripples[32]  float4 (x, y, startTime, strength), normalized wall UV (origin bottom-left)
//   _RippleCount  valid entries
//   _RippleTime   current time on the same clock as startTime (Time.unscaledTime)
//   _Intensity    Idle/Active blend * smoothed activity
//   _Aspect       wall width / height (keeps rings circular)
Shader "VisionToWall/RippleWall"
{
    Properties
    {
        _ColorTop ("Background Top", Color) = (0.03, 0.06, 0.16, 1)
        _ColorBottom ("Background Bottom", Color) = (0.0, 0.0, 0.03, 1)
        _GlowColor ("Glow", Color) = (0.35, 0.85, 1.0, 1)
        _RippleSpeed ("Ripple Speed (UV/s)", Float) = 0.35
        _RippleDecay ("Ripple Decay (1/s)", Float) = 1.6
        _RippleWidth ("Ripple Width (UV)", Float) = 0.035
        _Distortion ("Distortion", Float) = 0.025
        _PatternStrength ("Background Pattern", Range(0, 0.2)) = 0.012
        _GlowStrength ("Glow Strength", Range(0, 2)) = 0.6
        _Intensity ("Intensity", Range(0, 2)) = 1
        _Aspect ("Aspect", Float) = 1.7778
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_RIPPLES 32

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorTop;
                half4 _ColorBottom;
                half4 _GlowColor;
                float _RippleSpeed;
                float _RippleDecay;
                float _RippleWidth;
                float _Distortion;
                float _PatternStrength;
                float _GlowStrength;
                float _Intensity;
                float _Aspect;
            CBUFFER_END

            // Arrays cannot be material properties; set from C# with SetVectorArray.
            float4 _Ripples[MAX_RIPPLES];
            float _RippleCount;
            float _RippleTime;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float Pattern(float2 uv, float t)
            {
                // Soft drifting interference lines so the distortion is visible on a plain gradient.
                float2 p = float2(uv.x * _Aspect, uv.y) * 9.0;
                float a = sin(p.x + t * 0.15) * sin(p.y * 1.3 - t * 0.11);
                float b = sin((p.x + p.y) * 0.7 + t * 0.07);
                return (a * 0.6 + b * 0.4) * 0.5 + 0.5;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float2 p = float2(uv.x * _Aspect, uv.y);
                float t = _RippleTime;

                float2 offset = 0;
                float ringGlow = 0;
                float coreGlow = 0;
                int count = (int)_RippleCount;
                float invWidth = 1.0 / max(_RippleWidth, 1e-4);

                [loop]
                for (int k = 0; k < MAX_RIPPLES; k++)
                {
                    if (k >= count) break;

                    float4 r = _Ripples[k];
                    float age = t - r.z;
                    if (age < 0.0) continue;

                    float amp = r.w * exp(-_RippleDecay * age);
                    if (amp < 0.003) continue;

                    float2 d = p - float2(r.x * _Aspect, r.y);
                    float dist = length(d);
                    float x = (dist - age * _RippleSpeed) * invWidth;
                    float ring = exp(-x * x);

                    float2 dir = dist > 1e-4 ? d / dist : 0;
                    offset += dir * (ring * amp * sin(x * 3.14159));
                    ringGlow += ring * amp;
                    coreGlow += amp * exp(-dist * dist * 400.0);
                }

                // Many overlapping ripples (25 Hz input) must not wash the wall out: soft-saturate the sums.
                offset /= 1.0 + length(offset);
                float glow = 1.0 - exp(-(ringGlow * 0.6 + coreGlow * 0.5));

                float blend = saturate(_Intensity);
                float2 uv2 = uv + offset * _Distortion * _Intensity;

                half3 col = lerp(_ColorBottom.rgb, _ColorTop.rgb, saturate(uv2.y));
                col += _GlowColor.rgb * (Pattern(uv2, t) * _PatternStrength * (0.4 + 0.6 * blend));
                col += _GlowColor.rgb * glow * _GlowStrength * _Intensity;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
