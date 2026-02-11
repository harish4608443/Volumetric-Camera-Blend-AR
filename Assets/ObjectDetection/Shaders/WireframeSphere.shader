Shader "Custom/WireframeSphere"
{
    Properties
    {
        _Color ("Color", Color) = (0, 1, 1, 0.3)
        _EmissionColor ("Emission Color", Color) = (0, 0.8, 0.8, 1)
        _Fresnel ("Fresnel Power", Range(0.1, 10)) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back  // Only render front faces - prevents internal mirroring

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            float4 _Color;
            float4 _EmissionColor;
            float _Fresnel;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Fresnel effect for edge glow (makes edges brighter)
                float fresnel = 1.0 - saturate(dot(i.worldNormal, i.viewDir));
                fresnel = pow(fresnel, _Fresnel);
                
                // Mix base color with emission based on fresnel
                float4 finalColor = _Color;
                finalColor.rgb += _EmissionColor.rgb * fresnel * 2.0;
                finalColor.a = lerp(_Color.a, 1.0, fresnel * 0.6);
                
                return finalColor;
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
