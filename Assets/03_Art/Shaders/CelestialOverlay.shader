Shader "CityFlow/Celestial Overlay"
{
    Properties
    {
        [HDR] _Color ("Body Color", Color) = (1, 0.82, 0.32, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "UnityCG.cginc"

            half4 _Color;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float radialDistance =
                    distance(input.uv, float2(0.5, 0.5)) * 2.0;
                half core = 1.0 - smoothstep(
                    0.78,
                    1.0,
                    radialDistance);
                half glow = 1.0 - smoothstep(
                    0.45,
                    1.0,
                    radialDistance);
                half alpha = saturate(
                    core + glow * 0.28) * _Color.a;
                half3 color = _Color.rgb *
                    lerp(1.0, 1.35, glow);
                return half4(color, alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
