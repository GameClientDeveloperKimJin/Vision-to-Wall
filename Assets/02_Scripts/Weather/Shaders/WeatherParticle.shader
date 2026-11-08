// Additive textured weather particle (rain, snow, cloud, fog, sparkle, sun glow).
// Sprite alpha (imported "Alpha from Grayscale") x vertex color (ParticleSystem color) x _Fade.
// _Fade is driven by MediaWallController: lower in combat (crimson) and while a hand moves, so the
// hand-reactive particles stay readable. Drawn before the motion particles (Transparent-10).
Shader "VisionToWall/WeatherParticle"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Fade ("Fade", Range(0, 1)) = 1
        _Boost ("Boost", Range(0, 4)) = 1
        _Tint ("Tint (meshes without vertex color, e.g. the sun quad)", Color) = (1, 1, 1, 1)
        [Toggle] _UseVertexColor ("Use vertex color", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Fade;
                float _Boost;
                half4 _Tint;
                float _UseVertexColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                half4 vc = _UseVertexColor > 0.5 ? i.color : half4(1, 1, 1, 1);
                half4 col = vc * _Tint;
                half3 c = col.rgb * (col.a * a * _Fade * _Boost);
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
