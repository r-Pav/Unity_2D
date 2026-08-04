Shader "Custom/SpriteDissolve"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _DissolveAmount ("Dissolve Amount", Range(0,1)) = 0
        _EdgeWidth ("Edge Width", Range(0.001,0.2)) = 0.06
        _EdgeColor ("Edge Color", Color) = (1,0.75,0.25,1)
        _NoiseScale ("Noise Scale", Range(5,100)) = 30
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _DissolveAmount;
            float _EdgeWidth;
            fixed4 _EdgeColor;
            float _NoiseScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // 二维伪随机噪声,零贴图依赖,让溶解边缘不规则
            float hash (float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);

                // 到贴图最近边缘的距离(0~0.5),边缘先溶解
                float edge = min(min(i.uv.x, 1.0 - i.uv.x), min(i.uv.y, 1.0 - i.uv.y));

                // 格子噪声扰动,让烧蚀边缘不规则
                float n = hash(floor(i.uv * _NoiseScale));

                // 合成并归一化到 0~1:边缘距离为主(0.7),噪声为辅(0.3)
                float dissolve = (edge * 0.7 + n * 0.3) * 2.0;

                clip(dissolve - _DissolveAmount);

                // 溶解边缘的亮带,过渡区 alpha 渐隐避免硬边
                float edgeGlow = smoothstep(_DissolveAmount, _DissolveAmount + _EdgeWidth, dissolve);
                c.rgb = lerp(_EdgeColor.rgb, c.rgb, edgeGlow);
                c.a *= edgeGlow;

                return c;
            }
            ENDCG
        }
    }
}
