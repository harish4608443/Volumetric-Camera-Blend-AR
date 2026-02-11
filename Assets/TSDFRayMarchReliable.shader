Shader "Custom/TSDFRayMarchReliable"
{
    Properties
    {
        _TSDFAtlas ("TSDF Atlas", 2D) = "white" {}
        _WeightAtlas ("Weight Atlas", 2D) = "black" {}
        _MinWeight ("Min Weight Threshold", Range(0.1, 10.0)) = 1.0
        _StepSize ("Step Size", Range(0.01, 0.1)) = 0.03
        _MaxSteps ("Max Steps", Int) = 256
    }
    
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
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

            sampler2D _TSDFAtlas;
            sampler2D _WeightAtlas;
            
            float3 _VolumeOrigin;
            float4 _VolumeResolution;
            float _VoxelSize;
            float _StepSize;
            float _MinWeight;
            int _SlicesPerRow;
            int _MaxSteps;
            
            float4x4 _InvView;
            float4x4 _InvProj;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                
                // View ray for ray marching
                float2 ndc = v.uv * 2.0 - 1.0;
                float4 viewPos = mul(_InvProj, float4(ndc, 1, 1));
                o.viewRay = viewPos.xyz / viewPos.w;
                
                return o;
            }

            // Convert 3D voxel position to 2D atlas UV
            float2 VoxelToAtlasUV(int3 voxel)
            {
                int z = voxel.z;
                int sliceY = z / _SlicesPerRow;
                int sliceX = z % _SlicesPerRow;
                
                int slicesPerCol = ceil(_VolumeResolution.z / _SlicesPerRow);
                
                float2 atlasUV = (float2(sliceX, sliceY) * _VolumeResolution.xy + voxel.xy + 0.5) / 
                                 float2(_SlicesPerRow * _VolumeResolution.x, slicesPerCol * _VolumeResolution.y);
                
                return atlasUV;
            }

            // Sample TSDF value from atlas
            float SampleTSDF(float3 worldPos)
            {
                float3 voxelPos = (worldPos - _VolumeOrigin) / _VoxelSize;
                
                if (any(voxelPos < 0) || any(voxelPos >= _VolumeResolution.xyz))
                    return 1.0;  // Outside volume
                
                int3 voxel = (int3)voxelPos;
                float2 atlasUV = VoxelToAtlasUV(voxel);
                
                return tex2Dlod(_TSDFAtlas, float4(atlasUV, 0, 0)).r;
            }

            // Sample weight value from atlas
            float SampleWeight(float3 worldPos)
            {
                float3 voxelPos = (worldPos - _VolumeOrigin) / _VoxelSize;
                
                if (any(voxelPos < 0) || any(voxelPos >= _VolumeResolution.xyz))
                    return 0.0;  // Outside volume
                
                int3 voxel = (int3)voxelPos;
                float2 atlasUV = VoxelToAtlasUV(voxel);
                
                return tex2Dlod(_WeightAtlas, float4(atlasUV, 0, 0)).r;
            }

            // Compute surface normal
            float3 ComputeNormal(float3 worldPos)
            {
                float eps = _VoxelSize;
                float3 n;
                n.x = SampleTSDF(worldPos + float3(eps, 0, 0)) - SampleTSDF(worldPos - float3(eps, 0, 0));
                n.y = SampleTSDF(worldPos + float3(0, eps, 0)) - SampleTSDF(worldPos - float3(0, eps, 0));
                n.z = SampleTSDF(worldPos + float3(0, 0, eps)) - SampleTSDF(worldPos - float3(0, 0, eps));
                return normalize(n + 0.001);
            }

            // Depth-based coloring (rainbow)
            float3 DepthColor(float depth)
            {
                float t = saturate(depth * 0.3);
                if (t < 0.5)
                    return lerp(float3(1,0,0), float3(0,1,0), t * 2);
                else
                    return lerp(float3(0,1,0), float3(0,0,1), (t - 0.5) * 2);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Ray origin (camera position in world space)
                float3 rayOrigin = _WorldSpaceCameraPos;
                
                // Ray direction (from camera through pixel)
                float3 viewRayWorld = mul(_InvView, float4(i.viewRay, 0)).xyz;
                float3 rayDir = normalize(viewRayWorld);
                
                // Ray marching
                float t = 0.01;  // Start slightly in front of camera
                float maxDist = 10.0;  // Max ray distance
                
                for (int step = 0; step < _MaxSteps; step++)
                {
                    if (t > maxDist)
                        break;
                    
                    float3 worldPos = rayOrigin + rayDir * t;
                    
                    // Sample TSDF and weight
                    float tsdf = SampleTSDF(worldPos);
                    float weight = SampleWeight(worldPos);
                    
                    // Check if we hit a reliable surface
                    if (abs(tsdf) < 0.01 && weight >= _MinWeight)
                    {
                        // Hit a reliable surface!
                        float3 normal = ComputeNormal(worldPos);
                        float3 color = DepthColor(t);
                        
                        // Simple lighting
                        float3 lightDir = normalize(float3(0, 1, -1));
                        float diffuse = max(0.3, dot(normal, lightDir));
                        
                        return fixed4(color * diffuse, 0.7);  // Semi-transparent
                    }
                    
                    // Step along ray
                    t += max(_StepSize, abs(tsdf) * 0.5);  // Sphere tracing optimization
                }
                
                // No hit - return transparent to show stripes underneath
                return fixed4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
