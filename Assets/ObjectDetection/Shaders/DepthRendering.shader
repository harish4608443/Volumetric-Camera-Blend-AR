Shader "Custom/DepthRendering"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0.7, 0.9, 0.4)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        LOD 100
        
        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            
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
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };
            
            fixed4 _Color;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                // IntelliCap sphere colors from ARKitBackground_IntelliCap.shader:
                // sphereColor = fixed4(0.102, 0.173, 0.475, 1.0) - dark blue
                // Alpha based on depth difference and view angle
                
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(i.viewDir);
                
                // Fresnel effect for edge transparency
                float fresnel = 1.0 - abs(dot(normal, viewDir));
                
                // IntelliCap uses depth-difference alpha clamped at 0.8
                // We approximate with fresnel: 0.3-0.8 range
                float alpha = lerp(0.3, 0.8, fresnel);
                
                // Dark blue color matching IntelliCap
                return fixed4(0.102, 0.173, 0.475, alpha);
            }
            ENDCG
        }
    }
}
