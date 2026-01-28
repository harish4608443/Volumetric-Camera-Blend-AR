// Upgrade NOTE: commented out 'float4x4 _CameraToWorld', a built-in variable
// Upgrade NOTE: replaced '_CameraToWorld' with 'unity_CameraToWorld'

Shader "Custom/TSDFRayMarch"
{
    Properties
    {
        _MainTex ("Camera Feed", 2D) = "white" {}
        _VolumeOpacity ("Volume Opacity", Range(0, 1)) = 0.5
        _StepSize ("Ray March Step Size", Range(0.001, 0.1)) = 0.01
        _MaxSteps ("Max Ray March Steps", Range(1, 500)) = 200
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
                float3 rayDir : TEXCOORD1;
                float3 rayOrigin : TEXCOORD2;
            };

            sampler2D _MainTex;
            Texture3D<float2> TSDFVolume;
            SamplerState samplerTSDFVolume;
            
            float3 VolumeSize;
            float3 VolumeOrigin;
            float VoxelSize;
            float _VolumeOpacity;
            float _StepSize;
            int _MaxSteps;
            
            // Camera matrices
            // float4x4 _CameraToWorld;
            float4x4 _CameraInvProjection;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                
                // Reconstruct view ray
                float4 clipPos = float4(v.uv * 2.0 - 1.0, 1.0, 1.0);
                float4 viewPos = mul(_CameraInvProjection, clipPos);
                o.rayDir = mul(unity_CameraToWorld, float4(viewPos.xyz, 0.0)).xyz;
                o.rayOrigin = _WorldSpaceCameraPos;
                
                return o;
            }
            
            // Sample TSDF volume
            float SampleTSDF(float3 worldPos)
            {
                float3 voxelPos = (worldPos - VolumeOrigin) / (VolumeSize * VoxelSize);
                
                if (any(voxelPos < 0) || any(voxelPos > 1))
                    return 1.0; // Outside volume
                
                float2 tsdfData = TSDFVolume.SampleLevel(samplerTSDFVolume, voxelPos, 0);
                return tsdfData.x; // Signed distance
            }
            
            // Depth to RGB gradient
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
                
                // Ray march through TSDF volume
                float3 rayDir = normalize(i.rayDir);
                float3 rayPos = i.rayOrigin;
                
                float4 accumColor = float4(0, 0, 0, 0);
                float transmittance = 1.0;
                
                for (int step = 0; step < _MaxSteps && transmittance > 0.01; step++)
                {
                    float sdf = SampleTSDF(rayPos);
                    
                    // Near surface (sdf close to 0)
                    if (abs(sdf) < 0.1)
                    {
                        float dist = length(rayPos - i.rayOrigin);
                        float3 surfaceColor = DepthToRGB(dist);
                        float alpha = (1.0 - abs(sdf) / 0.1) * _VolumeOpacity;
                        
                        // Front-to-back compositing
                        accumColor.rgb += surfaceColor * alpha * transmittance;
                        accumColor.a += alpha * transmittance;
                        transmittance *= (1.0 - alpha);
                    }
                    
                    // March along ray
                    rayPos += rayDir * _StepSize;
                }
                
                // Blend with camera feed
                return float4(lerp(cameraColor.rgb, accumColor.rgb, accumColor.a), 1.0);
            }
            ENDCG
        }
    }
}
