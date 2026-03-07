Shader "TSDFWeightCoverageMask"
{
    Properties
    {
        _WeightAtlas ("TSDF Weight Atlas", 2D) = "black" {}
        _DepthTex ("Depth Texture", 2D) = "black" {}
        _WeightThreshold ("Weight Threshold", Float) = 1.0
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        
        Pass
        {
            Name "GenerateCoverageMask"
            ZTest Always
            ZWrite Off
            Cull Off
            
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
            
            sampler2D _WeightAtlas;
            sampler2D _DepthTex;
            float _WeightThreshold;
            
            // TSDF volume parameters (set from script)
            float3 _VolumeOrigin;
            int3 _VolumeResolution;
            float _VoxelSize;
            int _SlicesPerRow;
            int _SliceRowCount;  // = ceil(volumeRes.z / slicesPerRow) — atlas height in slices
            
            // Camera matrices
            float4x4 _InvViewMatrix;
            float4x4 _InvProjMatrix;
            
            // Convert 3D voxel coordinates to 2D atlas UV
            // atlasWidth  = volumeRes * slicesPerRow
            // atlasHeight = volumeRes * sliceRowCount  (NOT the same as atlasWidth for non-square atlases!)
            float2 VoxelToAtlasUV(int3 voxelCoord, int slicesPerRow, int sliceRowCount, int volumeRes)
            {
                int sliceIndex = voxelCoord.z;
                int sliceRow = sliceIndex / slicesPerRow;
                int sliceCol = sliceIndex % slicesPerRow;
                
                int atlasWidth  = volumeRes * slicesPerRow;
                int atlasHeight = volumeRes * sliceRowCount;
                
                float2 atlasUV;
                atlasUV.x = (float)(sliceCol * volumeRes + voxelCoord.x) / (float)atlasWidth;
                atlasUV.y = (float)(sliceRow * volumeRes + voxelCoord.y) / (float)atlasHeight;  // correct height
                
                return atlasUV;
            }
            
            // Sample TSDF weight at world position
            float SampleWeightAtWorldPos(float3 worldPos)
            {
                // Transform world to volume local space
                float3 localPos = worldPos - _VolumeOrigin;
                
                // Convert to voxel coordinates
                float3 voxelPosF = localPos / _VoxelSize;
                voxelPosF += float3(_VolumeResolution) * 0.5; // Center offset
                
                // Check bounds
                if (voxelPosF.x < 0 || voxelPosF.x >= _VolumeResolution.x ||
                    voxelPosF.y < 0 || voxelPosF.y >= _VolumeResolution.y ||
                    voxelPosF.z < 0 || voxelPosF.z >= _VolumeResolution.z)
                {
                    return 0.0; // Outside volume
                }
                
                // Nearest neighbor lookup (fast)
                int3 voxelCoord = int3(voxelPosF);
                
                // Convert to atlas UV
                float2 atlasUV = VoxelToAtlasUV(voxelCoord, _SlicesPerRow, _SliceRowCount, _VolumeResolution.x);
                
                // Sample weight atlas
                float weight = tex2Dlod(_WeightAtlas, float4(atlasUV, 0, 0)).r;
                
                return weight;
            }
            
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                
                // Calculate view ray for depth reconstruction
                float2 screenPos = v.uv * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                screenPos.y = -screenPos.y;
                #endif
                
                float4 rayClip = float4(screenPos.x, screenPos.y, 1.0, 1.0);
                float4 rayView = mul(_InvProjMatrix, rayClip);
                rayView.xyz /= rayView.w;
                
                o.viewRay = mul((float3x3)_InvViewMatrix, rayView.xyz);
                
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                float3 rayDir = normalize(i.viewRay);

                // --- Try the real ARCore depth first ---
                float depth = tex2D(_DepthTex, i.uv).r;
                if (depth >= 0.001 && depth <= 20.0)
                {
                    float3 worldPos = _WorldSpaceCameraPos + rayDir * depth;
                    if (SampleWeightAtWorldPos(worldPos) >= _WeightThreshold)
                        return fixed4(1, 1, 1, 1);
                    // Valid depth but not scanned yet → black
                    return fixed4(0, 0, 0, 1);
                }

                // --- Depth invalid / stale (ARCore only fires at ~5 Hz) ---
                // Ray-march through the TSDF volume along this pixel's view ray.
                // If ANY sampled voxel has weight >= threshold the area was already scanned
                // → show camera feed regardless of whether we have live depth right now.
                // tEnd = full volume diagonal. Steps = 64 so stepSize (0.125m) < truncDist (0.15m),
                // guaranteeing no thin scanned band is ever skipped.
                float tStart = 0.05;
                float tEnd   = float(_VolumeResolution.x) * _VoxelSize; // 6.4 m
                int   steps  = 64;
                float tStep  = (tEnd - tStart) / float(steps);
                for (int s = 0; s < steps; s++)
                {
                    float t = tStart + (float(s) + 0.5) * tStep;
                    float3 samplePos = _WorldSpaceCameraPos + rayDir * t;
                    if (SampleWeightAtWorldPos(samplePos) >= _WeightThreshold)
                        return fixed4(1, 1, 1, 1); // previously scanned → camera feed
                }

                // Nothing scanned along this ray → stripes
                return fixed4(0, 0, 0, 1);
            }
            ENDCG
        }
    }
}
