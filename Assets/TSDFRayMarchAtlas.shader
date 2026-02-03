Shader "Custom/TSDFRayMarchAtlas"
{
    Properties
    {
        _MainTex ("Camera", 2D) = "white" {}
        _TSDFAtlas ("TSDF Atlas", 2D) = "white" {}
        _VolumeOpacity ("Opacity", Range(0,1)) = 0.5
        _StepSize ("Step Size", Range(0.001, 0.1)) = 0.03
        _SurfaceThreshold ("Surface Threshold", Range(0.001, 0.1)) = 0.03
    }
    
    SubShader
    {
        Tags { "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
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
                float3 viewRay : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler2D _TSDFAtlas;
            
            float3 _VolumeOrigin;
            float4 _VolumeResolution;
            float _VoxelSize;
            float _VolumeOpacity;
            float _StepSize;
            float _SurfaceThreshold;
            int _SlicesPerRow;
            
            float4x4 _InvView;
            float4x4 _InvProj;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                
                // CRITICAL: Flip UV vertically for Android ARCore camera texture
                #if UNITY_ANDROID
                o.uv.y = 1.0 - o.uv.y;
                #endif
                
                // View ray
                float2 ndc = v.uv * 2.0 - 1.0;
                float4 viewPos = mul(_InvProj, float4(ndc, 1, 1));
                o.viewRay = viewPos.xyz / viewPos.w;
                
                return o;
            }

            // Sample TSDF from atlas
            float SampleTSDF(float3 worldPos)
            {
                // World to voxel
                float3 voxelPos = (worldPos - _VolumeOrigin) / _VoxelSize;
                
                // Bounds check
                if (any(voxelPos < 0) || any(voxelPos >= _VolumeResolution.xyz))
                    return 1.0;
                
                int3 voxel = (int3)voxelPos;
                
                // Voxel to atlas UV
                int z = voxel.z;
                int sliceY = z / _SlicesPerRow;
                int sliceX = z % _SlicesPerRow;
                
                float2 atlasUV = (float2(sliceX, sliceY) + (voxel.xy + 0.5) / _VolumeResolution.xy) / 
                                 float2(_SlicesPerRow, ceil(_VolumeResolution.z / _SlicesPerRow));
                
                return tex2Dlod(_TSDFAtlas, float4(atlasUV, 0, 0)).r;
            }

            float3 ComputeNormal(float3 worldPos)
            {
                float eps = _VoxelSize;
                float3 n;
                n.x = SampleTSDF(worldPos + float3(eps, 0, 0)) - SampleTSDF(worldPos - float3(eps, 0, 0));
                n.y = SampleTSDF(worldPos + float3(0, eps, 0)) - SampleTSDF(worldPos - float3(0, eps, 0));
                n.z = SampleTSDF(worldPos + float3(0, 0, eps)) - SampleTSDF(worldPos - float3(0, 0, eps));
                return normalize(n + 0.001);
            }

            float3 DepthColor(float dist)
            {
                float t = saturate(dist * 0.3);
                if (t < 0.5)
                    return lerp(float3(1,0,0), float3(0,1,0), t * 2);
                else
                    return lerp(float3(0,1,0), float3(0,0,1), (t - 0.5) * 2);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // TEST: Just return solid green to verify shader executes
                return fixed4(0, 1, 0, 1);
            }
            ENDCG
        }
    }
}
