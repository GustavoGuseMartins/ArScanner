Shader "ArScanner/PointCloud"
{
    Properties
    {
        [HideInInspector] _Cull ("Cull", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
        [HideInInspector] _RoundPoints ("Round points", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _RoundPoints;
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 color=input.color;
                clip(color.a-0.001h);
                if (_RoundPoints>0.5)
                {
                    float2 radius=input.uv*2.0-1.0;
                    float radiusSq=dot(radius,radius);
                    clip(1.0-radiusSq);
                    color.a*=saturate((1.0-radiusSq)/max(fwidth(radiusSq),0.001));
                }
                return color;
            }
            ENDHLSL
        }
    }
}
