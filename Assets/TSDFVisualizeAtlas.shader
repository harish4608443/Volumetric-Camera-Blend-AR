Shader "Hidden/TSDFVisualizeAtlas"
{
    Properties
    {
        _TSDFAtlas ("TSDF Atlas", 2D) = "white" {}
        _VolumeOpacity ("Volume Opacity", Float) = 0.5
    }
    
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        
        Pass
        {
            Name "TSDF Visualization"
            
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
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
                float3 rayDir : TEXCOORD1;
            };
            
            sampler2D _TSDFAtlas;
            float4 _TSDFAtlas_ST;
            float4x4 _CameraToWorldMatrix;
            float4x4 _WorldToCameraMatrix;
            int _VolumeResolution;
            float _VoxelSize;
            float _VolumeOpacity;
            float _TruncationDistance;
            float3 _VolumeCenter;
            
            // Convert 3D voxel coordinates to 2D atlas coordinates
            float2 VoxelToAtlas(int3 voxelCoord)
            {
                uint slicesPerRow = 512u / (uint)_VolumeResolution;
                uint sliceIndex = (uint)voxelCoord.z;
                uint sliceRow = sliceIndex / slicesPerRow;
                uint sliceCol = sliceIndex % slicesPerRow;
                
                float2 atlasCoord;
                atlasCoord.x = (float(sliceCol * (uint)_VolumeResolution) + voxelCoord.x + 0.5) / 512.0;
                atlasCoord.y = (float(sliceRow * (uint)_VolumeResolution) + voxelCoord.y + 0.5) / 512.0;
                
                return atlasCoord;
            }
            
            // Sample TSDF value at 3D position
            float SampleTSDF(float3 worldPos)
            {
                // Convert world position to voxel coordinates
                float3 volumeMin = _VolumeCenter - float3(_VolumeResolution * _VoxelSize * 0.5, _VolumeResolution * _VoxelSize * 0.5, _VolumeResolution * _VoxelSize * 0.5);
                float3 relPos = worldPos - volumeMin;
                float3 voxelPosF = relPos / _VoxelSize;
                
                // Check bounds
                if (any(voxelPosF < 0) || any(voxelPosF >= _VolumeResolution))
                    return _TruncationDistance;
                
                int3 voxelCoord = int3(voxelPosF);
                float2 atlasUV = VoxelToAtlas(voxelCoord);
                
                float4 tsdfData = tex2Dlod(_TSDFAtlas, float4(atlasUV, 0, 0));
                return tsdfData.r; // Distance value
            }
            
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                
                // Android UV flip
                #if UNITY_UV_STARTS_AT_TOP
                o.uv = float2(v.uv.x, 1.0 - v.uv.y);
                #else
                o.uv = v.uv;
                #endif
                
                // Calculate ray direction
                float2 screenPos = v.uv * 2.0 - 1.0;
                float4 rayClip = float4(screenPos, 1.0, 1.0);
                float4 rayView = mul(unity_CameraInvProjection, rayClip);
                rayView = float4(rayView.xy, -1.0, 0.0);
                o.rayDir = mul(_CameraToWorldMatrix, rayView).xyz;
                o.rayDir = normalize(o.rayDir);
                
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                // Simplified: Just show TSDF atlas visualization overlay
                // Sample center of volume to check TSDF data
                float3 volumeCenter = _VolumeCenter;
                float tsdfValue = SampleTSDF(volumeCenter);
                
                // Visualize TSDF values as color overlay
                float normalizedDist = saturate(tsdfValue / _TruncationDistance);
                float3 color = lerp(float3(1, 0, 0), float3(0, 1, 1), normalizedDist);
                
                // Show as semi-transparent overlay
                float alpha = _VolumeOpacity * 0.5;
                
                return float4(color, alpha);
            }
            ENDCG
        }
    }
    
    Fallback Off
}
