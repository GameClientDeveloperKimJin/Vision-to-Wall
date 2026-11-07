// Soft additive light cone for the virtual moving light. Bright at the fixture (uv.y = 0), fading to the end, and
// faded at the silhouette so the cone reads as a volume of light rather than a solid shape.
Shader "VisionToWall/DeviceBeam"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.9, 0.85, 0.6, 1)
        _Intensity ("Intensity", Range(0, 2)) = 0.35
        _LengthFade ("Length Fade", Range(0.2, 4)) = 1.4
        _EdgeSoftness ("Edge Softness", Range(0.2, 4)) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _LengthFade;
                float _EdgeSoftness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float facing = abs(dot(normalize(i.normalWS), normalize(i.viewWS)));
                float edge = pow(saturate(facing), _EdgeSoftness);
                float along = pow(saturate(1.0 - i.uv.y), _LengthFade);
                half3 c = _Color.rgb * (_Intensity * edge * along);
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
