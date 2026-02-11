Shader "Custom/SphereRendering"
{
    Properties
    {
        _SphereTex ("Sphere Texture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" }
        
        Pass
        {
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _SphereTex;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 sphereColor = tex2D(_SphereTex, i.uv);
                
                // RED (1,0,0) → Dark Blue (0,0,0.5) - Individual sphere surface
                if (sphereColor.r == 1.0 && sphereColor.g == 0.0 && sphereColor.b == 0.0)
                {
                    return fixed4(0.0, 0.0, 0.5, 1.0); // Dark Blue
                }
                // GREEN (0,1,0) → Bright Blue (0,0,1) - Merged spheres or small sphere
                else if (sphereColor.r == 0.0 && sphereColor.g == 1.0 && sphereColor.b == 0.0)
                {
                    return fixed4(0.0, 0.0, 1.0, 1.0); // Bright Blue
                }
                else
                {
                    return sphereColor; // Pass through other colors
                }
            }
            ENDCG
        }
    }
}
