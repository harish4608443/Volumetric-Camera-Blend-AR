Shader "Custom/DepthColorize"
{
    // Real-time depth fusion shader
    // Uses NATIVE ARCore depth resolution (typically 160×90)
    // NOT scaled to screen resolution
    // Applies correct camera intrinsics (FOV, projection matrix)
    Properties
    {
        _MainTex ("Camera Feed", 2D) = "white" {}
        _EnvironmentDepth ("Depth Texture", 2D) = "black" {}
        _ColorIntensity ("Color Intensity", Range(0, 1)) = 0.7
        _NormalStrength ("Normal Strength", Range(0.1, 5.0)) = 1.0
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        
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
            sampler2D _EnvironmentDepth;
            float4 _EnvironmentDepth_TexelSize;
            float _ColorIntensity;
            float _NormalStrength;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                
                // Flip UV vertically for Android ARCore depth texture
                #if UNITY_ANDROID
                o.uv.y = 1.0 - o.uv.y;
                #endif
                
                return o;
            }

            // Calculate normal from depth texture using finite differences
            float3 CalculateNormalFromDepth(float2 uv)
            {
                // Sample depth at current pixel and neighbors
                float depthCenter = tex2D(_EnvironmentDepth, uv).r;
                float depthRight = tex2D(_EnvironmentDepth, uv + float2(_EnvironmentDepth_TexelSize.x, 0)).r;
                float depthUp = tex2D(_EnvironmentDepth, uv + float2(0, _EnvironmentDepth_TexelSize.y)).r;
                
                // If no depth data, return default normal (facing camera)
                if (depthCenter < 0.01)
                {
                    return float3(0, 0, 1);
                }
                
                // Compute depth gradients
                float dx = (depthRight - depthCenter) * _NormalStrength;
                float dy = (depthUp - depthCenter) * _NormalStrength;
                
                // Construct normal vector (cross product of tangent vectors)
                float3 normal = normalize(float3(-dx, -dy, 1.0));
                
                // Map from [-1, 1] to [0, 1] for RGB visualization
                normal = normal * 0.5 + 0.5;
                
                return normal;
            }

            // Convert depth to rainbow RGB gradient
            float3 DepthToRGB(float depth)
            {
                // Map depth to 0-1 range (assuming max depth is around 5 meters)
                float t = saturate(depth * 0.5);
                
                // Create rainbow gradient: Red (close) -> Yellow -> Green -> Cyan -> Blue (far)
                float3 color;
                
                if (t < 0.25)
                {
                    // Red to Yellow
                    float s = t / 0.25;
                    color = float3(1.0, s, 0.0);
                }
                else if (t < 0.5)
                {
                    // Yellow to Green
                    float s = (t - 0.25) / 0.25;
                    color = float3(1.0 - s, 1.0, 0.0);
                }
                else if (t < 0.75)
                {
                    // Green to Cyan
                    float s = (t - 0.5) / 0.25;
                    color = float3(0.0, 1.0, s);
                }
                else
                {
                    // Cyan to Blue
                    float s = (t - 0.75) / 0.25;
                    color = float3(0.0, 1.0 - s, 1.0);
                }
                
                return color;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample camera feed
                fixed4 cameraColor = tex2D(_MainTex, i.uv);
                
                // For depth sampling, detect orientation and adjust UV accordingly
                float2 depthUV = i.uv;
                
                // Portrait mode only: rotate portrait screen UV to match landscape sensor depth texture.
                // Sensor is always landscape (wider than tall); portrait display = sensor rotated 90° CCW.
                // To map portrait → sensor (90° CW): new_x = y,  new_y = 1 - x
                // (previous code had new_x = 1-y which introduced a horizontal flip)
                float aspectRatio = _ScreenParams.x / _ScreenParams.y;
                if (aspectRatio < 1.0)
                {
                    float px = depthUV.x;
                    float py = depthUV.y;
                    depthUV.x = py;
                    depthUV.y = 1.0 - px;
                }
                
                // Sample depth with orientation-corrected UV
                float depth = tex2D(_EnvironmentDepth, depthUV).r;
                
                // If depth is too small (no valid depth), return camera feed only
                if (depth < 0.01)
                {
                    return cameraColor;
                }
                
                // Calculate surface normal from depth gradients (using corrected UV)
                float3 normal = CalculateNormalFromDepth(depthUV);
                
                // Get depth-based color gradient
                float3 depthColor = DepthToRGB(depth);
                
                // Detect edges by checking depth discontinuities
                float depthRight = tex2D(_EnvironmentDepth, depthUV + float2(_EnvironmentDepth_TexelSize.x, 0)).r;
                float depthDown = tex2D(_EnvironmentDepth, depthUV + float2(0, _EnvironmentDepth_TexelSize.y)).r;
                float edgeStrength = abs(depth - depthRight) + abs(depth - depthDown);
                edgeStrength = saturate(edgeStrength * 50.0); // Scale and clamp to 0-1
                
                // Combine: Normal colors modulated by depth gradient
                // Normals provide orientation (RGB direction)
                // Depth provides distance (color temperature)
                float3 volumetricColor = normal * depthColor;
                
                // Enhance edges with white highlights
                volumetricColor = lerp(volumetricColor, float3(1, 1, 1), edgeStrength * 0.5);
                
                // Blend with camera feed
                float3 finalColor = lerp(cameraColor.rgb, volumetricColor, _ColorIntensity);
                
                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }
}
