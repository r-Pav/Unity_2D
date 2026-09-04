Shader "Custom/SpriteDissolve"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _DissolveAmount ("Dissolve Amount", Range(0,1)) = 0
        _DissolveDir ("Dissolve Direction", Range(0,3)) = 2
        _EdgeWidth ("Edge Width", Range(0.001,0.2)) = 0.06
        _EdgeColor ("Edge Color", Color) = (1,0.75,0.25,1)
        _NoiseScale ("Noise Scale", Range(5,100)) = 30
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float _DissolveAmount;
            float _DissolveDir;
            float _EdgeWidth;
            half4 _EdgeColor;
            float _NoiseScale;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.color = v.color;
                o.uv = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            // 二维伪随机噪声,零贴图依赖,让溶解边缘不规则
            float hash (float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;

                // 单向溶解方向选择(无分支,纯 step 数学,兼容性最好)
                // 0=下→上 1=上→下 2=左→右 3=右→左
                float s0 = 1.0 - step(0.5, _DissolveDir);
                float s1 = step(0.5, _DissolveDir) * (1.0 - step(1.5, _DissolveDir));
                float s2 = step(1.5, _DissolveDir) * (1.0 - step(2.5, _DissolveDir));
                float s3 = step(2.5, _DissolveDir);
                float edge = s0 * i.uv.y
                           + s1 * (1.0 - i.uv.y)
                           + s2 * i.uv.x
                           + s3 * (1.0 - i.uv.x);

                // 格子噪声扰动,让烧蚀边缘不规则
                float n = hash(floor(i.uv * _NoiseScale));

                // 合成:edge 已归一化到 0~1
                float dissolve = edge * 0.7 + n * 0.3;

                clip(dissolve - _DissolveAmount);

                // 溶解边缘的亮带,过渡区 alpha 渐隐避免硬边
                float edgeGlow = smoothstep(_DissolveAmount, _DissolveAmount + _EdgeWidth, dissolve);
                c.rgb = lerp(_EdgeColor.rgb, c.rgb, edgeGlow);
                c.a *= edgeGlow;

                return c;
            }
            ENDHLSL
        }
    }
}
