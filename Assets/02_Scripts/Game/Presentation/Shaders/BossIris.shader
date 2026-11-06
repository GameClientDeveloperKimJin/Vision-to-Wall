// Front of the boss eye on a quad: black pupil, red pupil ring, blue iris strands, eyelids. Not a baked texture,
// so emission and closing are values. Driven by BossIrisView:
//   _Open  0 = lids closed, 1 = open
//   _Glow  emission multiplier; 0 = unlit sculpture, above 1 blooms
Shader "VisionToWall/BossIris"
{
    Properties
    {
        [HDR] _RingColor ("Pupil Ring", Color) = (3, 0.06, 0.05, 1)
        [HDR] _IrisColor ("Iris", Color) = (0.04, 0.3, 1.5, 1)
        _LidColor ("Lid", Color) = (0.035, 0.04, 0.05, 1)
        _DeadIris ("Unlit Iris", Color) = (0.02, 0.03, 0.05, 1)
        _PupilRadius ("Pupil Radius", Range(0.05, 0.6)) = 0.3
        _RingWidth ("Ring Width", Range(0.005, 0.2)) = 0.05
        _Open ("Open", Range(0, 1)) = 1
        _Glow ("Glow", Range(0, 8)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Unlit"
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RingColor;
                half4 _IrisColor;
                half4 _LidColor;
                half4 _DeadIris;
                float _PupilRadius;
                float _RingWidth;
                float _Open;
                float _Glow;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv * 2.0 - 1.0;
                return o;
            }

            float Strands(float angle, float radius)
            {
                float a = sin(angle * 37.0) * 0.5 + 0.5;
                float b = sin(angle * 83.0 + radius * 4.0) * 0.5 + 0.5;
                float c = sin(angle * 151.0 - radius * 7.0) * 0.5 + 0.5;
                return a * 0.45 + b * 0.35 + c * 0.2;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv);
                clip(1.0 - r);

                float glow = max(_Glow, 0.0);
                float iris01 = saturate((r - _PupilRadius) / max(1.0 - _PupilRadius, 1e-3));
                float angle = atan2(i.uv.y, i.uv.x);

                // Bright near the pupil, fading to a dark limbal edge.
                float strands = Strands(angle, r);
                float falloff = pow(1.0 - iris01, 1.6);
                half3 iris = _DeadIris.rgb + _IrisColor.rgb * glow * falloff * (0.25 + 0.75 * strands);

                float ring = exp(-pow((r - _PupilRadius) / max(_RingWidth, 1e-3), 2.0));
                iris += _RingColor.rgb * glow * ring;

                half3 col = r < _PupilRadius ? half3(0, 0, 0) + _RingColor.rgb * glow * ring : iris;

                // Lids close to a slit: the visible band's half height at this x.
                float limit = _Open * sqrt(saturate(1.0 - i.uv.x * i.uv.x));
                float aa = max(fwidth(i.uv.y), 1e-4);
                float open = smoothstep(limit + aa, limit - aa, abs(i.uv.y));

                float seam = exp(-pow(abs(abs(i.uv.y) - limit) / 0.02, 2.0)) * saturate(_Open * 8.0);
                half3 lid = _LidColor.rgb + _RingColor.rgb * glow * seam * 0.08;

                return half4(lerp(lid, col, open), 1);
            }
            ENDHLSL
        }
    }
}
