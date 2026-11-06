// Additive thin bright line for LineRenderer and TrailRenderer. Vertex color multiplies, so trails fade with their
// color gradient. A white core keeps the line readable through the decorative rays.
Shader "VisionToWall/LaserBeam"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (4, 0.04, 0.03, 1)
        _Core ("Core Width", Range(0.02, 0.8)) = 0.25
        _CoreBoost ("Core White", Range(0, 2)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Core;
                float _CoreBoost;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = abs(i.uv.y * 2.0 - 1.0);
                float halo = pow(saturate(1.0 - d), 2.0);
                float core = smoothstep(_Core, 0.0, d);
                half3 c = _Color.rgb * halo + half3(1, 1, 1) * core * _CoreBoost * max(_Color.r, max(_Color.g, _Color.b)) * 0.25;
                return half4(c * i.color.rgb * i.color.a, 0);
            }
            ENDHLSL
        }
    }
}
