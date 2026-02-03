Shader "Custom/TSDFRayMarch"
{
    Properties
    {
        _MainTex ("Camera Feed", 2D) = "white" {}
        _VolumeOpacity ("Volume Opacity", Range(0, 1)) = 0.5
        _StepSize ("Ray March Step Size", Range(0.001, 0.1)) = 0.02
        _MaxSteps ("Max Ray March Steps", Range(1, 300)) = 128
        _SurfaceThreshold ("Surface Threshold", Range(0.001, 0.1)) = 0.01
    }
    
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        
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
                float3 viewDir : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler3D TSDFVolume;
            
            float3 VolumeSize;
            float3 VolumeOrigin;
            float VoxelSize;
            float _VolumeOpacity;
            float _StepSize;
            int _MaxSteps;
            float _SurfaceThreshold;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                
                // Compute view direction in world space
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.viewDir = worldPos.xyz - _WorldSpaceCameraPos;
                
                return o;
            }
            
            // Sample TSDF volume
            float SampleTSDF(float3 worldPos)
            {
                // Convert world position to volume texture coordinates [0,1]
                float3 volumeExtent = VolumeSize * VoxelSize;
                float3 uvw = (worldPos - VolumeOrigin) / volumeExtent;
                
                // Check bounds
                if (any(uvw < 0) || any(uvw > 1))
                    return 1.0; // Outside volume = far from surface
                
                return tex3D(TSDFVolume, uvw).r;
            }
            
            // Compute gradient (normal) from TSDF
            float3 ComputeNormal(float3 worldPos)
            {
                float epsilon = VoxelSize * 2.0;
                
                float sdfX0 = SampleTSDF(worldPos - float3(epsilon, 0, 0));
                float sdfX1 = SampleTSDF(worldPos + float3(epsilon, 0, 0));
                float sdfY0 = SampleTSDF(worldPos - float3(0, epsilon, 0));
                float sdfY1 = SampleTSDF(worldPos + float3(0, epsilon, 0));
                float sdfZ0 = SampleTSDF(worldPos - float3(0, 0, epsilon));
                float sdfZ1 = SampleTSDF(worldPos + float3(0, 0, epsilon));
                
                float3 gradient = float3(
                    sdfX1 - sdfX0,
                    sdfY1 - sdfY0,
                    sdfZ1 - sdfZ0
                );
                
                return normalize(gradient + float3(0.001, 0.001, 0.001));
            }
            
            // Depth to RGB color
            float3 DepthToRGB(float depth)
            {
                float t = saturate(depth * 0.2);
                
                if (t < 0.25)
                    return lerp(float3(1, 0, 0), float3(1, 1, 0), t / 0.25);
                else if (t < 0.5)
                    return lerp(float3(1, 1, 0), float3(0, 1, 0), (t - 0.25) / 0.25);
                else if (t < 0.75)
                    return lerp(float3(0, 1, 0), float3(0, 1, 1), (t - 0.5) / 0.25);
                else
                    return lerp(float3(0, 1, 1), float3(0, 0, 1), (t - 0.75) / 0.25);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample camera feed
                fixed4 cameraColor = tex2D(_MainTex, i.uv);
                
                // DEBUG: Test if camera feed passes through
                return cameraColor;
                
                // Ray march setup
                float3 rayOrigin = _WorldSpaceCameraPos;
                float3 rayDir = normalize(i.viewDir);
                
                // Start from volume bounds
                float tMin = 0.1;
                float tMax = 5.0;
                
                float4 accumColor = float4(0, 0, 0, 0);
                float transmittance = 1.0;
                
                // Fixed step ray marching (simpler for mobile)
                [unroll(100)]
                for (int step = 0; step < 100; step++)
                {
                    if (transmittance < 0.01) break;
                    
                    float t = tMin + step * _StepSize;
                    if (t > tMax) break;
                    
                    float3 rayPos = rayOrigin + rayDir * t;
                    
                    float sdf = SampleTSDF(rayPos);
                    
                    // Near surface detection
                    if (abs(sdf) < _SurfaceThreshold)
                    {
                        // Compute surface normal
                        float3 normal = ComputeNormal(rayPos);
                        
                        // Distance from camera
                        float dist = t;
                        
                        // Color based on normal and depth
                        float3 normalColor = normal * 0.5 + 0.5;
                        float3 depthColor = DepthToRGB(dist);
                        float3 surfaceColor = normalColor * depthColor;
                        
                        // Surface opacity
                        float alpha = (1.0 - abs(sdf) / _SurfaceThreshold) * _VolumeOpacity * 2.0;
                        alpha = saturate(alpha);
                        
                        // Front-to-back compositing
                        accumColor.rgb += surfaceColor * alpha * transmittance;
                        accumColor.a += alpha * transmittance;
                        transmittance *= (1.0 - alpha);
                    }
                }
                
                // Always show camera feed blended with volume (never pure black)
                float3 finalColor = lerp(cameraColor.rgb, accumColor.rgb, accumColor.a * 0.8);
                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }
}
