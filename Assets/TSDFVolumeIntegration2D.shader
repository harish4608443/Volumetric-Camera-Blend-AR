Shader "Custom/TSDFVolumeIntegration2D"
{
    Properties
    {
        _DepthTex ("Depth", 2D) = "black" {}
        _PrevTSDF ("Previous TSDF", 2D) = "white" {}
        _PrevWeight ("Previous Weight", 2D) = "black" {}
    }
    
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        
        // Pass 0: Integrate TSDF
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_tsdf
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
            };

            sampler2D _DepthTex;
            sampler2D _PrevTSDF;
            sampler2D _PrevWeight;
            
            float4x4 _ViewMatrix;
            float4x4 _ProjMatrix;
            float3 _VolumeOrigin;
            float4 _VolumeResolution;
            float _VoxelSize;
            float _TruncDist;
            float _MaxDepth;
            int _SlicesPerRow;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Convert atlas UV to voxel coordinates
            int3 AtlasUVToVoxel(float2 uv)
            {
                int sliceX = (int)(uv.x * _SlicesPerRow);
                int sliceY = (int)(uv.y * ceil(_VolumeResolution.z / _SlicesPerRow));
                int z = sliceY * _SlicesPerRow + sliceX;
                
                float2 localUV = frac(uv * float2(_SlicesPerRow, ceil(_VolumeResolution.z / _SlicesPerRow)));
                int x = (int)(localUV.x * _VolumeResolution.x);
                int y = (int)(localUV.y * _VolumeResolution.y);
                
                return int3(x, y, z);
            }

            float4 frag_tsdf (v2f i) : SV_Target
            {
                int3 voxel = AtlasUVToVoxel(i.uv);
                
                // Out of bounds check
                if (voxel.x >= _VolumeResolution.x || voxel.y >= _VolumeResolution.y || voxel.z >= _VolumeResolution.z)
                {
                    return tex2D(_PrevTSDF, i.uv);
                }
                
                // Voxel world position
                float3 voxelWorld = _VolumeOrigin + (voxel + 0.5) * _VoxelSize;
                
                // Transform to camera space
                float4 voxelCam = mul(_ViewMatrix, float4(voxelWorld, 1.0));
                
                // Behind camera - keep previous
                if (voxelCam.z <= 0)
                {
                    return tex2D(_PrevTSDF, i.uv);
                }
                
                // Project to screen
                float4 voxelClip = mul(_ProjMatrix, voxelCam);
                float2 depthUV = (voxelClip.xy / voxelClip.w) * 0.5 + 0.5;
                
                // Out of depth bounds - keep previous
                if (depthUV.x < 0 || depthUV.x > 1 || depthUV.y < 0 || depthUV.y > 1)
                {
                    return tex2D(_PrevTSDF, i.uv);
                }
                
                // Flip Y for Android
                #if UNITY_ANDROID
                depthUV.y = 1.0 - depthUV.y;
                #endif
                
                // Sample depth at native resolution
                float depth = tex2D(_DepthTex, depthUV).r;
                
                // Invalid depth - keep previous
                if (depth < 0.01 || depth > _MaxDepth)
                {
                    return tex2D(_PrevTSDF, i.uv);
                }
                
                // Compute SDF
                float sdf = depth - voxelCam.z;
                
                // Behind surface truncation
                if (sdf < -_TruncDist)
                {
                    return tex2D(_PrevTSDF, i.uv);
                }
                
                // Normalize
                sdf = clamp(sdf / _TruncDist, -1.0, 1.0);
                
                // Weighted average
                float prevSDF = tex2D(_PrevTSDF, i.uv).r;
                float prevWeight = tex2D(_PrevWeight, i.uv).r;
                float newWeight = 1.0;
                float totalWeight = min(prevWeight + newWeight, 100.0);
                float updatedSDF = (prevSDF * prevWeight + sdf * newWeight) / totalWeight;
                
                return float4(updatedSDF, 0, 0, 1);
            }
            ENDCG
        }
        
        // Pass 1: Integrate weights
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_weight
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
            };

            sampler2D _DepthTex;
            sampler2D _PrevWeight;
            
            float4x4 _ViewMatrix;
            float4x4 _ProjMatrix;
            float3 _VolumeOrigin;
            float4 _VolumeResolution;
            float _VoxelSize;
            float _TruncDist;
            float _MaxDepth;
            int _SlicesPerRow;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            int3 AtlasUVToVoxel(float2 uv)
            {
                int sliceX = (int)(uv.x * _SlicesPerRow);
                int sliceY = (int)(uv.y * ceil(_VolumeResolution.z / _SlicesPerRow));
                int z = sliceY * _SlicesPerRow + sliceX;
                
                float2 localUV = frac(uv * float2(_SlicesPerRow, ceil(_VolumeResolution.z / _SlicesPerRow)));
                int x = (int)(localUV.x * _VolumeResolution.x);
                int y = (int)(localUV.y * _VolumeResolution.y);
                
                return int3(x, y, z);
            }

            float4 frag_weight (v2f i) : SV_Target
            {
                int3 voxel = AtlasUVToVoxel(i.uv);
                
                if (voxel.x >= _VolumeResolution.x || voxel.y >= _VolumeResolution.y || voxel.z >= _VolumeResolution.z)
                {
                    return tex2D(_PrevWeight, i.uv);
                }
                
                float3 voxelWorld = _VolumeOrigin + (voxel + 0.5) * _VoxelSize;
                float4 voxelCam = mul(_ViewMatrix, float4(voxelWorld, 1.0));
                
                if (voxelCam.z <= 0)
                {
                    return tex2D(_PrevWeight, i.uv);
                }
                
                float4 voxelClip = mul(_ProjMatrix, voxelCam);
                float2 depthUV = (voxelClip.xy / voxelClip.w) * 0.5 + 0.5;
                
                if (depthUV.x < 0 || depthUV.x > 1 || depthUV.y < 0 || depthUV.y > 1)
                {
                    return tex2D(_PrevWeight, i.uv);
                }
                
                #if UNITY_ANDROID
                depthUV.y = 1.0 - depthUV.y;
                #endif
                
                float depth = tex2D(_DepthTex, depthUV).r;
                
                if (depth < 0.01 || depth > _MaxDepth)
                {
                    return tex2D(_PrevWeight, i.uv);
                }
                
                float sdf = depth - voxelCam.z;
                
                if (sdf < -_TruncDist)
                {
                    return tex2D(_PrevWeight, i.uv);
                }
                
                float prevWeight = tex2D(_PrevWeight, i.uv).r;
                float totalWeight = min(prevWeight + 1.0, 100.0);
                
                return float4(totalWeight, 0, 0, 1);
            }
            ENDCG
        }
    }
}
