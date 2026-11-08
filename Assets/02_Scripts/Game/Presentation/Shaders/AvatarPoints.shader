// Player figure as glowing dots. Each point is four vertices at the same position; uv.xy holds the corner and uv.z a
// per-point random. The vertex shader spreads the corners in view space. Points on the silhouette are brighter.
// uv1 is the point's rest position in the figure frame: the back creases use it, so they stay on the body when the
// mesh is skinned (R4 pose avatar). Lighting uses the posed (skinned) position and normal.
// Points on screen near the boss eye are dimmed (soft edge) so a raised arm does not hide the eye; the hit flash is not.
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
        [Header(Back of the figure)]
        _Ambient ("Flat Light Share", Range(0, 1)) = 0.25
        _LightDir ("Light Direction (world)", Vector) = (-0.5, 0.75, -0.45, 0)
        _CreaseDim ("Crease Dimming", Range(0, 1)) = 0.95
        _CreaseWidth ("Crease Half Width", Float) = 0.008
        _SpineY ("Spine Height (min, max)", Vector) = (0.10, 0.385, 0, 0)
        _BladeY ("Shoulder Blade Height (min, max)", Vector) = (0.20, 0.34, 0, 0)
        _BladeX ("Shoulder Blade Offset", Float) = 0.055
        _BladeSlope ("Shoulder Blade Slope", Float) = 0.28
        _BackCenterX ("Mesh Center X", Float) = 0
        [Header(Boss eye)]
        _BossViewport ("Boss Viewport (x, y)", Vector) = (0.5, 0.5, 0, 0)
        _BossFadeRadius ("Dim Radius (screen heights, 0 = off)", Float) = 0
        _BossFadeSoft ("Dim Edge Width", Float) = 0.05
        _BossFadeMin ("Brightness Inside", Range(0, 1)) = 0.25
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
                float _Ambient;
                float4 _LightDir;
                float _CreaseDim;
                float _CreaseWidth;
                float4 _SpineY;
                float4 _BladeY;
                float _BladeX;
                float _BladeSlope;
                float _BackCenterX;
                float4 _BossViewport;
                float _BossFadeRadius;
                float _BossFadeSoft;
                float _BossFadeMin;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 uv : TEXCOORD0; float3 rest : TEXCOORD1; };
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
                float diffuse = saturate(dot(n, normalize(_LightDir.xyz)));
                float front = (0.4 + 0.6 * facing) * _Ambient + (1.0 - _Ambient) * diffuse * 2.0;

                // The model has no back detail, so spine and shoulder blades are drawn as dark creases on the back.
                float3 p = v.rest;
                float ax = abs(p.x - _BackCenterX);
                float spine = step(_SpineY.x, p.y) * step(p.y, _SpineY.y) * saturate(1.0 - ax / _CreaseWidth);
                float bladeLine = abs(ax - (_BladeX + (_BladeY.y - p.y) * _BladeSlope));
                float blade = step(_BladeY.x, p.y) * step(p.y, _BladeY.y) * saturate(1.0 - bladeLine / _CreaseWidth);
                float crease = max(spine, blade) * step(p.z, 0.0);

                float twinkle = 1.0 - _Twinkle + _Twinkle * (sin(_Time.y * (2.0 + seed * 5.0) + seed * 40.0) * 0.5 + 0.5);
                half3 c = (_Color.rgb * front + _RimColor.rgb * rim) * twinkle * (1.0 - _CreaseDim * crease);
                // Boss eye: distance on screen in screen heights from the point's center to the boss.
                float4 centerCS = TransformWorldToHClip(world);
                float2 vp = centerCS.xy / centerCS.w;
                vp.y *= _ProjectionParams.x;
                vp = vp * 0.5 + 0.5;
                float2 toBoss = (vp - _BossViewport.xy) * float2(_ScreenParams.x / _ScreenParams.y, 1.0);
                float inner = _BossFadeRadius - max(_BossFadeSoft, 1e-4);
                float boss = lerp(_BossFadeMin, 1.0, smoothstep(inner, _BossFadeRadius, length(toBoss)));
                boss = _BossFadeRadius > 0.0 ? boss : 1.0;

                o.color = lerp(c * boss, _FlashColor.rgb, saturate(_Flash)) * nearSide;
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
