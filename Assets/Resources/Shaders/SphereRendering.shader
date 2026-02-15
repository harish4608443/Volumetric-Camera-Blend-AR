Shader "Custom/SphereRendering"
{
    Properties
    {
        _MainTex ("AR Camera Texture", 2D) = "white" {}
        _SphereTex ("Sphere Texture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        
        Pass
        {
            ZWrite Off
            ZTest Always
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
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
                // Sample AR camera background
                float4 bgColor = tex2D(_MainTex, i.uv);
                
                // Sample sphere rendering
                float4 sphereColor = tex2D(_SphereTex, i.uv);
                
                // Convert GREEN (0,1,0) → Bright Blue (0,0,1) for small/merged spheres
                if (sphereColor.g > 0.9 && sphereColor.r < 0.1 && sphereColor.b < 0.1)
                {
                    sphereColor = fixed4(0.0, 0.0, 1.0, sphereColor.a); // Bright Blue, preserve alpha
                }
                // Convert RED (1,0,0) → Dark Blue (0,0,0.5) for individual spheres
                else if (sphereColor.r > 0.9 && sphereColor.g < 0.1 && sphereColor.b < 0.1)
                {
                    sphereColor = fixed4(0.0, 0.0, 0.5, sphereColor.a); // Dark Blue, preserve alpha
                }
                
                // Alpha blend sphere over background
                float3 finalColor = lerp(bgColor.rgb, sphereColor.rgb, sphereColor.a);
                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }
}
