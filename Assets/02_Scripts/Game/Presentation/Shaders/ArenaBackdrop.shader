// Combat background in one full-screen pass, behind everything the overlay camera draws: black space, perspective
// grid floor and ceiling, rays spreading from the vanishing point, data pillars and waveforms at both sides.
// Deliberately dim: the bright red of the real aim line and shots must stay the brightest thing on screen.
//   _Reveal       0 = hidden (the wall shows through), 1 = full
//   _VanishPoint  0~1 screen position of the boss
Shader "VisionToWall/ArenaBackdrop"
{
    Properties
    {
        _SpaceColor ("Space", Color) = (0.001, 0.001, 0.003, 1)
        _GridColor ("Grid", Color) = (0.015, 0.07, 0.14, 1)
        _RayCyan ("Ray Cyan", Color) = (0.02, 0.18, 0.25, 1)
        _RayViolet ("Ray Violet", Color) = (0.1, 0.03, 0.2, 1)
        _RayRed ("Ray Red", Color) = (0.12, 0.01, 0.02, 1)
        _PillarColor ("Data Pillars", Color) = (0.02, 0.1, 0.13, 1)
        _RayStrength ("Ray Strength", Range(0, 2)) = 0.55
        _GridStrength ("Grid Strength", Range(0, 2)) = 0.7
        _Reveal ("Reveal", Range(0, 1)) = 1
        _VanishPoint ("Vanish Point", Vector) = (0.5, 0.72, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SpaceColor;
                half4 _GridColor;
                half4 _RayCyan;
                half4 _RayViolet;
                half4 _RayRed;
                half4 _PillarColor;
                float _RayStrength;
                float _GridStrength;
                float _Reveal;
                float4 _VanishPoint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // Full screen in clip space; the transform only keeps the quad from being culled.
                o.positionCS = float4(v.uv * 2.0 - 1.0, UNITY_RAW_FAR_CLIP_VALUE, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                o.positionCS.y = -o.positionCS.y;
                #endif
                o.uv = v.uv;
                return o;
            }

            float Hash(float x) { return frac(sin(x * 127.1) * 43758.5453); }

            float GridLines(float2 p, float2 width)
            {
                float2 g = abs(frac(p) - 0.5);
                float2 d = fwidth(p);
                float2 line01 = smoothstep(0.5 - width - d, 0.5 - d * 0.5, g);
                return max(line01.x, line01.y);
            }

            float3 Grid(float2 p)
            {
                // Floor below the vanishing point and ceiling above it.
                float h = abs(p.y);
                float depth = 0.35 / max(h, 0.004);
                float2 plane = float2(p.x * depth * 2.2, depth * 2.2 + _Time.y * 0.5);
                float lines = GridLines(plane, 0.015);
                float fade = smoothstep(0.04, 0.35, h);
                return _GridColor.rgb * lines * fade * _GridStrength;
            }

            float3 Rays(float2 p)
            {
                const float Count = 36.0;
                float angle = atan2(p.y, p.x) / 6.28318 + 0.5;
                float slot = angle * Count;
                float index = floor(slot);
                float f = frac(slot) - 0.5;

                float seed = Hash(index + 3.0);
                float present = step(0.45, seed);
                float kind = Hash(index + 11.0);
                float3 color = kind < 0.45 ? _RayCyan.rgb : (kind < 0.8 ? _RayViolet.rgb : _RayRed.rgb);

                float r = length(p);
                float width = lerp(0.008, 0.025, Hash(index + 23.0));
                float along = smoothstep(0.03, 0.3, r);
                float dash = smoothstep(0.1, 0.3, frac(r * (0.4 + seed) - _Time.y * (0.05 + seed * 0.12) + seed));
                float beam = smoothstep(width + fwidth(slot), width * 0.4, abs(f));
                return color * beam * present * along * dash * _RayStrength;
            }

            float3 Pillars(float2 uv)
            {
                float side = 1.0 - smoothstep(0.08, 0.15, min(uv.x, 1.0 - uv.x));
                if (side <= 0.0) return 0;

                float2 cell = float2(uv.x * 90.0, uv.y * 40.0 + _Time.y * (0.4 + Hash(floor(uv.x * 90.0)) * 1.2));
                float2 id = floor(cell);
                float bit = step(0.62, Hash(id.x * 17.0 + id.y * 3.1));
                float2 f = frac(cell);
                float glyph = step(0.2, f.x) * step(f.x, 0.8) * step(0.2, f.y) * step(f.y, 0.8);
                float column = step(0.4, Hash(id.x + 5.0));

                float wave = sin(uv.y * 24.0 + _Time.y * 0.8) * 0.02 + sin(uv.y * 57.0 - _Time.y * 1.3) * 0.008;
                float waveX = min(uv.x, 1.0 - uv.x) - 0.11;
                float waveLine = smoothstep(0.004, 0.0, abs(waveX - wave));

                return _PillarColor.rgb * side * (bit * glyph * column * 0.55 + waveLine * 0.8);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 p = (i.uv - _VanishPoint.xy) * float2(aspect, 1.0);

                float3 c = _SpaceColor.rgb;
                c += Grid(p);
                c += Rays(p);
                c += Pillars(i.uv);

                return half4(c, saturate(_Reveal));
            }
            ENDHLSL
        }
    }
}
