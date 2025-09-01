Shader "Unlit/Grid"
{
    Properties
    {
        _GridColour ("Grid Colour", color) = (1, 1, 1, 1)
        _BaseColour ("Base Colour", color) = (1, 1, 1, 0)
        _GridSpacing ("Grid Spacing", float) = 1
        _LineThickness ("Line Thickness", float) = .1
        _ODistance ("Start Transparency Distance", float) = 5
        _TDistance ("Full Transparency Distance", float) = 10
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            float4 _GridColour;
            float4 _BaseColour;
            float _GridSpacing;
            float _LineThickness;
            float _ODistance;
            float _TDistance;

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.worldPos = TransformObjectToWorld(v.positionOS.xyz);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = i.worldPos.xz / _GridSpacing;
                float2 wrapped = frac(uv) - 0.5f;
                float2 range = abs(wrapped);
                float2 speeds = fwidth(uv);
                float2 pixelRange = range / speeds;
                float lineWeight = saturate(min(pixelRange.x, pixelRange.y) - _LineThickness);
                half4 param = lerp(_GridColour, _BaseColour, lineWeight);

                half3 viewDirW = _WorldSpaceCameraPos - i.worldPos;
                half viewDist = length(viewDirW);
                half falloff = saturate((viewDist - _ODistance) / (_TDistance - _ODistance));
                param.a *= (1.0f - falloff);
                return param;
            }
            ENDHLSL
        }
    }
}