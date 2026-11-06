// Energy shield on the built-in sphere: blue rim, glowing leaf-vein pattern (Voronoi cell borders on a warped
// sphere), and a flare around the struck point. Driven by ShieldView:
//   _Reveal  0 = hidden, 1 = full
//   _Hit     1 at a parry, fades to 0
//   _HitDir  world direction from the center to the struck point
Shader "VisionToWall/ShieldSphere"
{
    Properties
    {
        [HDR] _RimColor ("Rim", Color) = (0.06, 0.4, 1.4, 1)
        [HDR] _VeinColor ("Veins", Color) = (0.1, 0.6, 1.8, 1)
        [HDR] _HitColor ("Hit", Color) = (3, 1.6, 0.4, 1)
        _FillColor ("Fill", Color) = (0.004, 0.02, 0.06, 1)
        _VeinScale ("Vein Scale", Float) = 5
        _VeinWidth ("Vein Width", Range(0.005, 0.2)) = 0.05
        _Reveal ("Reveal", Range(0, 1)) = 1
        _Hit ("Hit", Range(0, 1)) = 0
        _HitDir ("Hit Direction", Vector) = (0, 0, -1, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half4 _VeinColor;
                half4 _HitColor;
                half4 _FillColor;
                float _VeinScale;
                float _VeinWidth;
                float _Reveal;
                float _Hit;
                float4 _HitDir;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(world);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetCameraPositionWS() - world;
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

            // Distance to the nearest Voronoi cell border, 0 on a border.
            float VeinDistance(float3 p)
            {
                float3 cell = floor(p);
                float3 f = p - cell;
                float d1 = 8.0;
                float d2 = 8.0;
                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float3 g = float3(x, y, z);
                    float3 r = g + Hash3(cell + g) - f;
                    float d = dot(r, r);
                    if (d < d1) { d2 = d1; d1 = d; }
                    else if (d < d2) { d2 = d; }
                }
                return sqrt(d2) - sqrt(d1);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                float facing = saturate(dot(n, v));
                float rim = pow(1.0 - facing, 3.0);

                float3 s = normalize(i.positionOS);
                float t = _Time.y * 0.15;
                float3 warped = s * _VeinScale + 0.35 * sin(s.yzx * 7.0 + t);
                float veinDist = VeinDistance(warped);
                float veins = smoothstep(_VeinWidth, 0.0, veinDist);
                float fineVeins = smoothstep(_VeinWidth * 0.6, 0.0, VeinDistance(warped * 2.3 + 11.0)) * 0.35;

                float hitAngle = saturate(dot(n, normalize(_HitDir.xyz)));
                float flare = pow(hitAngle, 18.0) * _Hit;
                float hitVeins = pow(hitAngle, 4.0) * _Hit * (veins + fineVeins);

                half3 c = _FillColor.rgb;
                c += _RimColor.rgb * rim;
                c += _VeinColor.rgb * (veins + fineVeins) * (0.25 + 0.75 * rim);
                c += _HitColor.rgb * (flare + hitVeins);

                return half4(c * _Reveal, 0);
            }
            ENDHLSL
        }
    }
}
