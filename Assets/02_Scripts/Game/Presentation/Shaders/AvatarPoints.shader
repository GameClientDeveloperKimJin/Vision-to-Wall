// Player figure as glowing dots. Each point is four vertices at the same position; uv.xy holds the corner and uv.z a
// per-point random. The vertex shader spreads the corners in view space. Points on the silhouette are brighter.
//   _Flash  1 at a hit, fades to 0
Shader "VisionToWall/AvatarPoints"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.03, 0.25, 0.6, 1)
        [HDR] _RimColor ("Silhouette", Color) = (0.08, 0.5, 1.1, 1)
        [HDR] _FlashColor ("Hit Flash", Color) = (3, 0.1, 0.1, 1)
        _PointSize ("Point Size (world)", Float) = 0.016
        _Twinkle ("Twinkle", Range(0, 1)) = 0.35
        _Flash ("Flash", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

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
                half4 _RimColor;
                half4 _FlashColor;
                float _PointSize;
                float _Twinkle;
                float _Flash;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 corner : TEXCOORD0; half3 color : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                float3 view = TransformWorldToView(world);
                float seed = v.uv.z;

                float size = _PointSize * (0.6 + 0.8 * seed);
                view.xy += v.uv.xy * size;
                o.positionCS = TransformWViewToHClip(view);
                o.corner = v.uv.xy;

                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float3 toCam = normalize(GetCameraPositionWS() - world);
                float facing = saturate(dot(n, toCam));
                float rim = pow(1.0 - abs(dot(n, toCam)), 2.0);
                // Far side dots are off: seen through the back, the chest and face read as a figure facing the screen.
                float nearSide = step(0.0, dot(n, toCam));
                float front = 0.4 + 0.6 * facing;

                float twinkle = 1.0 - _Twinkle + _Twinkle * (sin(_Time.y * (2.0 + seed * 5.0) + seed * 40.0) * 0.5 + 0.5);
                half3 c = (_Color.rgb * front + _RimColor.rgb * rim) * twinkle;
                o.color = lerp(c, _FlashColor.rgb, saturate(_Flash)) * nearSide;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float falloff = pow(saturate(1.0 - dot(i.corner, i.corner)), 2.0);
                return half4(i.color * falloff, 0);
            }
            ENDHLSL
        }
    }
}
