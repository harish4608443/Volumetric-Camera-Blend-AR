Shader "Custom/VolumetricBlend"
{
    Properties
    {
        _MainTex ("Main Camera RGB", 2D) = "white" {}
        _VolumetricTex ("Volumetric Camera", 2D) = "white" {}
        _DepthTex ("Depth Texture", 2D) = "black" {}
        _VolumetricAlpha ("Volumetric Alpha", Range(0, 1)) = 0.5
        _MaxDepth ("Max Depth", Float) = 10.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _VolumetricTex;
            sampler2D _DepthTex;
            float _VolumetricAlpha;
            float _MaxDepth;
            int _UseDepth;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample AR camera feed (RGB)
                fixed4 cameraColor = tex2D(_MainTex, i.uv);
                
                // Sample volumetric camera (colored planes)
                fixed4 volumetricColor = tex2D(_VolumetricTex, i.uv);
                
                // Check if volumetric pixel is NOT the magenta background
                // Magenta = (1, 0, 1), so check if it's NOT magenta
                bool isNotBackground = !(volumetricColor.r > 0.9 && volumetricColor.b > 0.9 && volumetricColor.g < 0.1);
                
                // Start with camera feed
                fixed4 finalColor = cameraColor;
                
                // If volumetric has content (planes or test cube), blend it
                if (isNotBackground && (volumetricColor.r > 0.01 || volumetricColor.g > 0.01 || volumetricColor.b > 0.01))
                {
                    // Blend plane colors over camera feed
                    finalColor.rgb = lerp(cameraColor.rgb, volumetricColor.rgb, _VolumetricAlpha);
                }
                
                return finalColor;
            }
            ENDCG
        }
    }
}
