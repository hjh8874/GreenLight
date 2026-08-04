Shader "Hidden/CityFlow/NightWindowOverlay"
{
    Properties
    {
        _BaseMap("Building Texture", 2D) = "white" {}
        _BaseColor("Window Light", Color) = (1, 0.9, 0.72, 1)
        _EmissionIntensity("Emission Intensity", Float) = 1.35
        _Enabled("Enabled", Float) = 0
        _WindowMaskProfile("Window Mask Profile", Float) = 1
        _BuildingSeed("Building Seed", Float) = 0
        _BuildingBottom("Building Bottom", Float) = 0
        _BuildingHeight("Building Height", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest+20"
            "RenderType" = "TransparentCutout"
        }

        Pass
        {
            Name "WindowLight"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _EmissionIntensity;
                float _Enabled;
                float _WindowMaskProfile;
                float _BuildingSeed;
                float _BuildingBottom;
                float _BuildingHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(
                    input.positionOS.xyz);
                output.normalOS = input.normalOS;
                output.positionOS = input.positionOS.xyz;
                output.positionCS = TransformWorldToHClip(
                    output.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            float WindowHash(float2 cell, float seed)
            {
                return frac(sin(dot(cell + seed,
                                    float2(12.9898, 78.233))) *
                            43758.5453);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(_Enabled - 0.5);

                float height01 = saturate(
                    (input.positionWS.z - _BuildingBottom) /
                    max(_BuildingHeight, 0.0001));

                if (_WindowMaskProfile < 1.5)
                {
                    float2 windowDistance = abs(
                        input.uv - float2(0.136, 0.476));
                    clip(0.0045 - max(
                        windowDistance.x,
                        windowDistance.y));
                }
                else if (_WindowMaskProfile < 2.5)
                {
                    half3 albedo = SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        input.uv).rgb;
                    half luminance = dot(
                        albedo,
                        half3(0.2126h, 0.7152h, 0.0722h));

                    clip(input.uv.y - 0.045);
                    clip(0.255 - input.uv.y);
                    clip(0.30h - luminance);

                    float floorIndex = clamp(
                        floor(
                            (input.positionOS.y - 4.0) /
                            3.6),
                        0.0,
                        5.0);
                    float paneColumn =
                        step(0.175, input.uv.x) +
                        step(0.400, input.uv.x) +
                        step(0.625, input.uv.x) +
                        step(0.840, input.uv.x);
                    float3 normalOS = normalize(input.normalOS);
                    float usesXSide = step(
                        abs(normalOS.z),
                        abs(normalOS.x));
                    float sideIndex = usesXSide * 2.0 +
                        lerp(
                            step(0.0, normalOS.z),
                            step(0.0, normalOS.x),
                            usesXSide);
                    float2 windowCell = float2(
                        paneColumn + sideIndex * 7.0,
                        floorIndex);
                    float lightChance = 0.27;
                    float windowRandom = WindowHash(
                        windowCell,
                        _BuildingSeed);
                    if (floorIndex > 0.5)
                    {
                        float belowWindowRandom = WindowHash(
                            float2(
                                windowCell.x,
                                floorIndex - 1.0),
                            _BuildingSeed);
                        if (belowWindowRandom < lightChance)
                        {
                            discard;
                        }
                    }
                    clip(lightChance - windowRandom);
                }
                else
                {
                    clip(input.uv.x);
                    clip(0.335 - input.uv.x);
                    clip(input.uv.y);
                    clip(0.215 - input.uv.y);
                }

                half3 warmWindowColor = _BaseColor.rgb *
                    half3(1.0h, 0.62h, 0.22h);
                return half4(
                    warmWindowColor * _EmissionIntensity,
                    _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
