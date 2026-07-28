Shader "CityFlow/Skybox Cubemap Blend"
{
    Properties
    {
        [NoScaleOffset] _TexA ("Current Cubemap", Cube) = "grey" {}
        [NoScaleOffset] _TexB ("Next Cubemap", Cube) = "grey" {}
        _Blend ("Blend", Range(0, 1)) = 0
        _RotationA ("Current Rotation", Range(0, 360)) = 0
        _RotationB ("Next Rotation", Range(0, 360)) = 0
        _ExposureA ("Current Exposure", Range(0, 8)) = 1
        _ExposureB ("Next Exposure", Range(0, 8)) = 1
        [HideInInspector] _HorizonRotation ("Horizon Rotation", Vector) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "UnityCG.cginc"

            samplerCUBE _TexA;
            samplerCUBE _TexB;
            half _Blend;
            half _RotationA;
            half _RotationB;
            half _ExposureA;
            half _ExposureB;
            float4 _HorizonRotation;

            struct Attributes
            {
                float4 vertex : POSITION;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            float3 RotateAroundY(float3 direction, float degrees)
            {
                float radiansValue = radians(degrees);
                float sineValue;
                float cosineValue;
                sincos(radiansValue, sineValue, cosineValue);

                return float3(
                    cosineValue * direction.x -
                    sineValue * direction.z,
                    direction.y,
                    sineValue * direction.x +
                    cosineValue * direction.z);
            }

            float3 RotateByQuaternion(
                float3 direction,
                float4 rotation)
            {
                float inverseLength = rsqrt(
                    max(dot(rotation, rotation), 0.000001));
                float4 normalizedRotation =
                    rotation * inverseLength;
                return direction +
                    2.0 * cross(
                        normalizedRotation.xyz,
                        cross(
                            normalizedRotation.xyz,
                            direction) +
                        normalizedRotation.w * direction);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position =
                    UnityObjectToClipPos(input.vertex);
                output.direction = input.vertex.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 horizonLeveledDirection =
                    RotateByQuaternion(
                        input.direction,
                        _HorizonRotation);
                float3 directionA = RotateAroundY(
                    horizonLeveledDirection,
                    _RotationA);
                float3 directionB = RotateAroundY(
                    horizonLeveledDirection,
                    _RotationB);
                half3 colorA =
                    texCUBE(_TexA, directionA).rgb *
                    _ExposureA;
                half3 colorB =
                    texCUBE(_TexB, directionB).rgb *
                    _ExposureB;

                return half4(
                    lerp(
                        colorA,
                        colorB,
                        saturate(_Blend)),
                    1);
            }
            ENDCG
        }
    }

    Fallback Off
}
