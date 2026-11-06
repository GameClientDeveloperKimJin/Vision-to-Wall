// Additive soft light on a quad that always faces the camera, for the projectile bolt. The white core reads as hot.
Shader "VisionToWall/BillboardGlow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (6, 0.1, 0.06, 1)
        _Softness ("Softness", Range(0.5, 8)) = 2.5
        _Size ("World Size", Float) = 0.4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "DisableBatching" = "True" }

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
                float _Softness;
                float _Size;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 centerVS = TransformWorldToView(TransformObjectToWorld(float3(0, 0, 0)));
                float2 corner = v.uv * 2.0 - 1.0;
                o.positionCS = TransformWViewToHClip(centerVS + float3(corner * _Size * 0.5, 0));
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 d = i.uv * 2.0 - 1.0;
                float falloff = pow(saturate(1.0 - dot(d, d)), _Softness);
                float core = pow(falloff, 6.0);
                half3 c = _Color.rgb * falloff + half3(1, 1, 1) * core * 2.0;
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
