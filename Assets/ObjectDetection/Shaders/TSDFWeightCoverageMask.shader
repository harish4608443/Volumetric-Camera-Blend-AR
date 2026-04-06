Shader "TSDFWeightCoverageMask"
{
    Properties
    {
        _WeightAtlas ("TSDF Weight Atlas", 2D) = "black" {}
        _DepthTex ("Depth Texture", 2D) = "black" {}
        _WeightThreshold ("Weight Threshold", Float) = 1.0
        _DebugWeights ("Debug Weight Heatmap (0=off 1=on)", Float) = 0
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
            float _DebugWeights;
            
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
            
            // Convert world position to atlas UV (shared by weight and TSDF samplers)
            float2 WorldPosToAtlasUV(float3 worldPos)
            {
                float3 localPos = worldPos - _VolumeOrigin;
                float3 voxelPosF = localPos / _VoxelSize;
                voxelPosF += float3(_VolumeResolution) * 0.5; // center offset
                if (voxelPosF.x < 0 || voxelPosF.x >= _VolumeResolution.x ||
                    voxelPosF.y < 0 || voxelPosF.y >= _VolumeResolution.y ||
                    voxelPosF.z < 0 || voxelPosF.z >= _VolumeResolution.z)
                    return float2(-1, -1); // sentinel: out of bounds
                int3 voxelCoord = int3(voxelPosF);
                return VoxelToAtlasUV(voxelCoord, _SlicesPerRow, _SliceRowCount, _VolumeResolution.x);
            }

            // Sample TSDF weight at world position
            float SampleWeightAtWorldPos(float3 worldPos)
            {
                float2 uv = WorldPosToAtlasUV(worldPos);
                if (uv.x < 0) return 0.0; // outside volume
                return tex2Dlod(_WeightAtlas, float4(uv, 0, 0)).r;
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

                // --- Debug: weight heatmap ---
                if (_DebugWeights > 0.5)
                {
                    float depth = tex2D(_DepthTex, i.uv).r;
                    float w = 0.0;
                    if (depth >= 0.001 && depth <= 20.0)
                    {
                        w = SampleWeightAtWorldPos(_WorldSpaceCameraPos + rayDir * depth);
                    }
                    else
                    {
                        float tStart = 0.05;
                        float tEnd   = float(_VolumeResolution.x) * _VoxelSize;
                        float tStep  = (tEnd - tStart) / 32.0;
                        for (int s = 0; s < 32; s++)
                        {
                            float t = tStart + (float(s) + 0.5) * tStep;
                            float wr = SampleWeightAtWorldPos(_WorldSpaceCameraPos + rayDir * t);
                            if (wr > w) w = wr;
                        }
                    }
                    return fixed4(saturate(w / (_WeightThreshold * 5.0)), 0, 0, 1);
                }

                // --- Phase 1: fast-exit if exact depth point is scanned ---
                // Only returns camera feed early. If it misses (depth noise, VIO drift, sub-voxel
                // precision at threshold=5.0), it falls through to Phase 2 rather than returning
                // stripes — this eliminates the one-frame flicker artifacts on scanned areas.
                float depth = tex2D(_DepthTex, i.uv).r;
                if (depth >= 0.001 && depth <= 20.0)
                {
                    float3 worldPos = _WorldSpaceCameraPos + rayDir * depth;
                    if (SampleWeightAtWorldPos(worldPos) >= _WeightThreshold)
                        return fixed4(1, 1, 1, 1); // scanned at exact depth → camera feed
                    // Miss: fall through to Phase 2 ray-march rather than returning stripes.
                    // Phase 2 will find the scanned voxel if it exists nearby, absorbing
                    // any depth noise or VIO drift without causing flickering artifacts.
                }

                // --- Phase 2: full ray-march (authoritative decision) ---
                // Runs when depth is stale/invalid AND when Phase 1 misses (depth noise, drift).
                // Searches the whole volume — if the area was scanned, it will be found here.
                // This is what gives the smooth "slowly clears as you observe" behaviour.
                float tStart = 0.05;
                float tEnd   = float(_VolumeResolution.x) * _VoxelSize;
                int   steps  = 64;
                float tStep  = (tEnd - tStart) / float(steps);
                for (int s = 0; s < steps; s++)
                {
                    float t = tStart + (float(s) + 0.5) * tStep;
                    float3 samplePos = _WorldSpaceCameraPos + rayDir * t;
                    if (SampleWeightAtWorldPos(samplePos) >= _WeightThreshold)
                        return fixed4(1, 1, 1, 1); // scanned voxel found → camera feed
                }

                // Nothing scanned along this ray → stripes
                return fixed4(0, 0, 0, 1);
            }
            ENDCG
        }
    }
}
