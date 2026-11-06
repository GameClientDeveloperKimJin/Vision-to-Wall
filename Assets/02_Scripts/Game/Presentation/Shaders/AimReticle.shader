// Aim ring on a quad scaled to the hit radius: its outer edge is the judged area. An arc fills with _Progress.
Shader "VisionToWall/AimReticle"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (3, 0.04, 0.03, 1)
        _Progress ("Progress", Range(0, 1)) = 0
        _Width ("Ring Width", Range(0.005, 0.2)) = 0.035
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
                float _Progress;
                float _Width;
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

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv);
                float aa = max(fwidth(r), 1e-4);
                float outer = 1.0 - _Width;
                float ring = smoothstep(outer - aa, outer, r) * smoothstep(1.0, 1.0 - aa, r);

                // Clockwise from the top.
                float turn = frac(atan2(i.uv.x, i.uv.y) / 6.28318 + 1.0);
                float filled = turn <= _Progress ? 1.0 : 0.3;

                float fill = _Progress * _Progress * 0.06 * smoothstep(1.0, 0.2, r);
                return half4(_Color.rgb * (ring * filled + fill), 0);
            }
            ENDHLSL
        }
    }
}
