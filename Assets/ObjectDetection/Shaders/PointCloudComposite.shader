Shader "ARRealism/PointCloudComposite"
{
    Properties
    {
        _CameraTex ("Camera Texture", 2D) = "white" {}
        _MaskTex ("Point Cloud Mask", 2D) = "white" {}
        _StripeTex ("Stripe Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _CameraTex;
            sampler2D _MaskTex;
            sampler2D _StripeTex;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 cameraCol = tex2D(_CameraTex, i.uv);
                fixed4 maskCol = tex2D(_MaskTex, i.uv);
                fixed4 stripeCol = tex2D(_StripeTex, i.uv);
                float mask = maskCol.r; // 1 = scanned (white), 0 = incomplete (black)
                
                // Blend: show camera where scanned, show stripes where incomplete
                // Result = camera * mask + stripes * (1 - mask)
                fixed4 result = cameraCol * mask + stripeCol * (1.0 - mask);
                return result;
            }
            ENDCG
        }
    }
}
