// Direct volume rendering for 3D datasets. Coordinates: object space in [0,1]^3, eye in object space.
// Units: texture space (normalised). Assumes axis-aligned unit cube.
Shader "Custom/VolumeRendering"
{
    Properties
    {
        _VolumeTexture ("Data Texture (Generated)", 3D) = "" {}
        _GradientTex("Gradient Texture (Generated)", 3D) = "" {}
        _NoiseTex("Noise Texture (Generated)", 2D) = "white" {}
        _GradientTexture("Transfer Function Texture (Generated)", 2D) = "" {}
        _RedChannelTF("Red Channel Transfer Function", 2D) = "" {}
        _GreenChannelTF("Green Channel Transfer Function", 2D) = "" {}
        _ShadowVolume("Shadow Volume Texture (Generated)", 3D) = "" {}
        _Intensity("Intensity", Range(0.1, 6.0)) = 1
        _Threshold("Threshold", Range(0.0, 0.6)) = 0
        _SliceMin("Slice Min", Range(0.0, 1.0)) = 0.0
        _SliceMax("Slice Max", Range(0.0, 1.0)) = 1.0
        _StepCount("Ray Steps", Range(128,1024)) = 128
        _MinVal("Min Value", Range(0.0, 1.0)) = 0.006
        _MaxVal("Max Value", Range(0.0, 1.0)) = 0.6
        _MinGradient("Gradient visibility threshold", Range(0.0, 1.0)) = 0.0
        _LightingGradientThresholdStart("Gradient threshold for lighting (end)", Range(0.0, 1.0)) = 0.0
        _LightingGradientThresholdEnd("Gradient threshold for lighting (start)", Range(0.0, 1.0)) = 0.0
        _SecondaryDataTex ("Secondary Data Texture (Generated)", 3D) = "" {}
        _SecondaryTFTex("Transfer Function Texture for secondary volume", 2D) = "" {}
        _GradMax("Gradient magnitude normalisation", Float) = 1.75
        [HideInInspector] _ShadowVolumeTextureSize("Shadow volume dimensions", Vector) = (1, 1, 1)
        [HideInInspector] _TextureSize("Dataset dimensions", Vector) = (1, 1, 1)
        
        // Adaptive ray marching
        [Toggle(ADAPTIVE_RAYMARCHING_ON)] _ADAPTIVE_RAYMARCHING_ON ("Enable Adaptive Ray Marching", Float) = 0
        _EmptySpaceThreshold("Empty Space Threshold", Range(0.0, 1.0)) = 0.001
        _EmptySpaceStepFactor("Empty Space Step Factor", Range(1.0, 10.0)) = 2.0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        LOD 100
        Cull Front
        ZTest LEqual
        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma multi_compile MODE_DVR MODE_MIP MODE_SURF
            #pragma multi_compile __ TF2D_ON
            #pragma multi_compile __ DUAL_CHANNEL_TF_ON
            #pragma multi_compile __ CROSS_SECTION_ON
            #pragma multi_compile __ LIGHTING_ON
            #pragma multi_compile __ SHADOWS_ON
            #pragma multi_compile __ DEPTHWRITE_ON
            #pragma multi_compile __ RAY_TERMINATE_ON
            #pragma multi_compile __ USE_MAIN_LIGHT
            #pragma multi_compile __ CUBIC_INTERPOLATION_ON
            #pragma multi_compile __ SECONDARY_VOLUME_ON
            #pragma multi_compile MULTIVOLUME_NONE MULTIVOLUME_OVERLAY MULTIVOLUME_ISOLATE
            #pragma multi_compile _ ADAPTIVE_RAYMARCHING_ON
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Include/TricubicSampling.cginc"

            #define AMBIENT_LIGHTING_FACTOR 0.2
            #define JITTER_FACTOR 5.0

            struct vert_in
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 vertex : POSITION;
                float4 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct frag_in
            {
                UNITY_VERTEX_OUTPUT_STEREO
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 vertexLocal : TEXCOORD1;
                half3 normal : NORMAL;
            };

            struct frag_out
            {
                half4 colour : SV_TARGET;
#if defined(DEPTHWRITE_ON)
                float depth : SV_DEPTH;
#endif
            };

            sampler3D _VolumeTexture;
            sampler3D _GradientTex;
            sampler2D _NoiseTex;
            sampler2D _GradientTexture;
            sampler2D _RedChannelTF;
            sampler2D _GreenChannelTF;
            sampler3D _ShadowVolume;
            sampler3D _SecondaryDataTex;
            sampler2D _SecondaryTFTex;

            float _MinVal;
            float _MaxVal;
            float3 _TextureSize;
            float3 _ShadowVolumeTextureSize;

            float _MinGradient;
            float _LightingGradientThresholdStart;
            float _LightingGradientThresholdEnd;
            float _GradMax;


            float _Intensity;
            float _Threshold;
            float _SliceMin;
            float _SliceMax;
            float _StepCount;
            float _EmptySpaceThreshold;
            float _EmptySpaceStepFactor;

#if defined(CROSS_SECTION_ON)
#include "Include/VolumeCutout.cginc"
#else
            bool IsCutout(float3 currPos)
            {
                return false;
            }
#endif

            struct RayInfo
            {
                float3 startPos;
                float3 endPos;
                float3 direction;
                float2 aabbInters;
            };

            struct RaymarchInfo
            {
                RayInfo ray;
                int numSteps;
                float numStepsRecip;
                float stepSize;
            };

            float3 getViewRayDir(float3 vertexLocal)
            {
                if(unity_OrthoParams.w == 0)
                {
                    // Perspective
                    return normalize(ObjSpaceViewDir(float4(vertexLocal, 0.0f)));
                }
                else
                {
                    // Orthographic
                    float3 camfwd = mul((float3x3)unity_CameraToWorld, float3(0,0,-1));
                    float4 camfwdobjspace = mul(unity_WorldToObject, camfwd);
                    return normalize(camfwdobjspace);
                }
            }

            // Find ray intersection points with axis aligned bounding box
            float2 intersectAABB(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax)
            {
                float3 tMin = (boxMin - rayOrigin) / rayDir;
                float3 tMax = (boxMax - rayOrigin) / rayDir;
                float3 t1 = min(tMin, tMax);
                float3 t2 = max(tMin, tMax);
                float tNear = max(max(t1.x, t1.y), t1.z);
                float tFar = min(min(t2.x, t2.y), t2.z);
                return float2(tNear, tFar);
            };

            // Get a ray for the specified fragment (back-to-front)
            RayInfo getRayBack2Front(float3 vertexLocal)
            {
                RayInfo ray;
                ray.direction = getViewRayDir(vertexLocal);
                ray.startPos = vertexLocal + float3(0.5f, 0.5f, 0.5f);
                // Find intersections with axis aligned boundinng box (the volume)
                ray.aabbInters = intersectAABB(ray.startPos, ray.direction, float3(0.0, 0.0, 0.0), float3(1.0f, 1.0f, 1.0));

                // Check if camera is inside AABB
                const float3 farPos = ray.startPos + ray.direction * ray.aabbInters.y - float3(0.5f, 0.5f, 0.5f);
                float4 clipPos = UnityObjectToClipPos(float4(farPos, 1.0f));
                if(unity_OrthoParams.w == 0)
                {
                    float3 viewDir = ObjSpaceViewDir(float4(vertexLocal, 0.0f));
                    float viewDist = length(viewDir);
                    if (ray.aabbInters.y > viewDist)
                    {
                        ray.aabbInters.y = viewDist;
                    }
                }

                ray.endPos = ray.startPos + ray.direction * ray.aabbInters.y;
                return ray;
            }

            // Get a ray for the specified fragment (front-to-back)
            RayInfo getRayFront2Back(float3 vertexLocal)
            {
                RayInfo ray = getRayBack2Front(vertexLocal);
                ray.direction = -ray.direction;
                float3 tmp = ray.startPos;
                ray.startPos = ray.endPos;
                ray.endPos = tmp;
                return ray;
            }

            RaymarchInfo initRaymarch(RayInfo ray, int maxNumSteps)
            {
                RaymarchInfo raymarchInfo;
                raymarchInfo.stepSize = 1.732f/*greatest distance in box*/ / maxNumSteps;
                raymarchInfo.numSteps = (int)clamp(abs(ray.aabbInters.x - ray.aabbInters.y) / raymarchInfo.stepSize, 1, maxNumSteps);
                raymarchInfo.numStepsRecip = 1.0 / raymarchInfo.numSteps;
                return raymarchInfo;
            }

            // Gets the colour from a 1D Transfer Function (x = density)
            half4 getTF1DColour(float density)
            {
                return tex2Dlod(_GradientTexture, float4(density, 0.0f, 0.0f, 0.0f));
            }

            // Gets the colour from a 2D Transfer Function (x = density, y = gradient magnitude)
            half4 getTF2DColour(float density, float gradientMagnitude)
            {
                return tex2Dlod(_GradientTexture, float4(density, gradientMagnitude, 0.0f, 0.0f));
            }

            // Gets the colour from a secondary 1D Transfer Function (x = density)
            half4 getSecondaryTF1DColour(float density)
            {
                return tex2Dlod(_SecondaryTFTex, float4(density, 0.0f, 0.0f, 0.0f));
            }

            // Gets the colour from red channel transfer function
            half4 getRedChannelTFColour(float density)
            {
                return tex2Dlod(_RedChannelTF, float4(density, 0.0f, 0.0f, 0.0f));
            }

            // Gets the colour from green channel transfer function
            half4 getGreenChannelTFColour(float density)
            {
                return tex2Dlod(_GreenChannelTF, float4(density, 0.0f, 0.0f, 0.0f));
            }

            // Gets the red channel density at the specified position
            float getRedChannelDensity(float3 pos)
            {
#if defined(CUBIC_INTERPOLATION_ON)
                return interpolateTricubicFast(_VolumeTexture, float3(pos.x, pos.y, pos.z), _TextureSize).r;
#else
                return tex3Dlod(_VolumeTexture, float4(pos.x, pos.y, pos.z, 0.0f)).r;
#endif
            }

            // Gets the green channel density at the specified position
            float getGreenChannelDensity(float3 pos)
            {
#if defined(CUBIC_INTERPOLATION_ON)
                return interpolateTricubicFast(_VolumeTexture, float3(pos.x, pos.y, pos.z), _TextureSize).g;
#else
                return tex3Dlod(_VolumeTexture, float4(pos.x, pos.y, pos.z, 0.0f)).g;
#endif
            }

            // Gets the density at the specified position
            float getDensity(float3 pos)
            {
#if defined(CUBIC_INTERPOLATION_ON)
                return interpolateTricubicFast(_VolumeTexture, float3(pos.x, pos.y, pos.z), _TextureSize);
#else
                return tex3Dlod(_VolumeTexture, float4(pos.x, pos.y, pos.z, 0.0f));
#endif
            }

            // Gets the density of the secondary volume at the specified position
            float getSecondaryDensity(float3 pos)
            {
                return tex3Dlod(_SecondaryDataTex, float4(pos.x, pos.y, pos.z, 0.0f));
            }

            // Gets the density at the specified position, without tricubic interpolation
            float getDensityNoTricubic(float3 pos)
            {
                return tex3Dlod(_VolumeTexture, float4(pos.x, pos.y, pos.z, 0.0f));
            }

            // Gets the gradient at the specified position
            float3 getGradient(float3 pos)
            {
#if defined(CUBIC_INTERPOLATION_ON)
                return interpolateTricubicFast(_GradientTex, float3(pos.x, pos.y, pos.z), _TextureSize).rgb;
#else
                return tex3Dlod(_GradientTex, float4(pos.x, pos.y, pos.z, 0.0f)).rgb;
#endif
            }

            // Get the light direction (using main light or view direction, based on setting)
            float3 getLightDirection(float3 viewDir)
            {
#if defined(USE_MAIN_LIGHT)
                return normalize(mul(unity_WorldToObject, _WorldSpaceLightPos0.xyz));
#else
                return viewDir;
#endif
            }

            // Performs lighting calculations, and returns a modified colour.
            half3 calculateLighting(half3 col, half3 normal, half3 lightDir, half3 eyeDir, half specularIntensity)
            {
                // Invert normal if facing opposite direction of view direction.
                // Optimised version of: if(dot(normal, eyeDir) < 0.0) normal *= -1.0
                normal *= (step(0.0, dot(normal, eyeDir)) * 2.0 - 1.0);

                half ndotl = max(dot(normal, lightDir), 0.0f);
                half3 diffuse = ndotl * col;
                half3 ambient = AMBIENT_LIGHTING_FACTOR * col;
                half3 result = diffuse + ambient;
                return half3(min(result.r, 1.0f), min(result.g, 1.0f), min(result.b, 1.0f));
            }

            float calculateShadow(float3 pos, float3 lightDir)
            {
#if defined(CUBIC_INTERPOLATION_ON)
                return interpolateTricubicFast(_ShadowVolume, float3(pos.x, pos.y, pos.z), _ShadowVolumeTextureSize);
#else
                return tex3Dlod(_ShadowVolume, float4(pos.x, pos.y, pos.z, 0.0f));
#endif
            }

            // Converts local position to depth value
            float localToDepth(float3 localPos)
            {
                float4 clipPos = UnityObjectToClipPos(float4(localPos, 1.0f));

#if defined(SHADER_API_GLCORE) || defined(SHADER_API_OPENGL) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
                return (clipPos.z / clipPos.w) * 0.5 + 0.5;
#else
                return clipPos.z / clipPos.w;
#endif
            }

            frag_in vert_main (vert_in v)
            {
                frag_in o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.vertexLocal = v.vertex;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            // Direct Volume Rendering
            frag_out frag_dvr(frag_in i)
            {
                #define MAX_NUM_STEPS 512
                #define OPACITY_THRESHOLD (1.0 - 1.0 / 255.0)
                const int samplingRate = (int)_StepCount;

                RayInfo ray = getRayFront2Back(i.vertexLocal);
                RaymarchInfo raymarchInfo = initRaymarch(ray, samplingRate);

                half3 lightDir = normalize(ObjSpaceViewDir(float4(float3(0.0f, 0.0f, 0.0f), 0.0f)));

                // Create a small random offset in order to remove artifacts
                ray.startPos += (JITTER_FACTOR * ray.direction * raymarchInfo.stepSize) * tex2D(_NoiseTex, float2(i.uv.x, i.uv.y)).r;

                half4 col = half4(0.0f, 0.0f, 0.0f, 0.0f);
                float tDepth = 1.0;
                float t = 0.0;
                for (int iStep = 0; iStep < raymarchInfo.numSteps; iStep++)
                {
                    const float3 currPos = ray.startPos + t * ray.direction;

                    if(currPos.x < 0.0 || currPos.x > 1.0 || currPos.y < 0.0 || currPos.y > 1.0 || currPos.z < 0.0 || currPos.z > 1.0)
                        break;

                    if (currPos.z < _SliceMin || currPos.z > _SliceMax)
                    {
                        t += raymarchInfo.stepSize;
                        continue;
                    }

                    // Perform slice culling (cross section plane)
#if defined(CROSS_SECTION_ON)
                    if(IsCutout(currPos))
                    {
                        t += raymarchInfo.stepSize;
                    	continue;
                    }
#endif
                    
                    const float density = getDensity(currPos);

#if defined(ADAPTIVE_RAYMARCHING_ON)
                    // If in empty space, take a larger step
                    if(density < _EmptySpaceThreshold)
                    {
                        t += raymarchInfo.stepSize * _EmptySpaceStepFactor;
                        iStep += (int)_EmptySpaceStepFactor - 1;
                        continue;
                    }
#endif

#if defined(CUBIC_INTERPOLATION_ON) && !defined(MULTIVOLUME_OVERLAY) && !defined(MULTIVOLUME_ISOLATE)
                    // Optimisation: First get density without tricubic interpolation, before doing an early return
                    if (getTF1DColour(getDensityNoTricubic(currPos)).a == 0.0)
                    {
                        t += raymarchInfo.stepSize;
                        continue;
                    }
#endif
                    if (density < _Threshold)
                    {
                        t += raymarchInfo.stepSize;
                        continue;
                    }



                    // Apply visibility window
                    if (density < _MinVal || density > _MaxVal)
                    {
                        t += raymarchInfo.stepSize;
                        continue;
                    }

                    // Apply 1D transfer function
#if !TF2D_ON
#if defined(DUAL_CHANNEL_TF_ON)
                    // Use separate transfer functions for red and green channels
                    const float redDensity = getRedChannelDensity(currPos);
                    const float greenDensity = getGreenChannelDensity(currPos);
                    
                    half4 redSrc = getRedChannelTFColour(redDensity);
                    half4 greenSrc = getGreenChannelTFColour(greenDensity);
                    
                    redSrc.a *= _Intensity;
                    greenSrc.a *= _Intensity;
                    
                    // Combine red and green channels
                    half4 src = half4(redSrc.rgb * redSrc.a + greenSrc.rgb * greenSrc.a, max(redSrc.a, greenSrc.a));
#else
                    half4 src = getTF1DColour(density);
                    src.a *= _Intensity;
#endif
#if !defined(MULTIVOLUME_OVERLAY) && !defined(MULTIVOLUME_ISOLATE)
                    if (src.a == 0.0)
                    {
                        t += raymarchInfo.stepSize;
                        continue;
                    }
#endif

#if defined(MULTIVOLUME_OVERLAY) || defined(MULTIVOLUME_ISOLATE)
                    const float secondaryDensity = getSecondaryDensity(currPos);
                    half4 secondaryColour = getSecondaryTF1DColour(secondaryDensity);
#if MULTIVOLUME_OVERLAY
                    src = secondaryColour.a > 0.0 ? secondaryColour : src;
#elif MULTIVOLUME_ISOLATE
                    src.a = secondaryColour.a > 0.0 ? src.a : 0.0;
#endif
#endif
#endif

                    // Calculate gradient (needed for lighting and 2D transfer functions)
#if defined(TF2D_ON) || defined(LIGHTING_ON)
                    float3 gradient = getGradient(currPos);
                    float gradMag = length(gradient);

                    float gradMagNorm = gradMag / _GradMax;
#endif

                    // Apply 2D transfer function
#if TF2D_ON
                    half4 src = getTF2DColour(density, gradMagNorm);
                    src.a *= _Intensity;
                    if (src.a == 0.0)
                    {
                        t += raymarchInfo.stepSize;
                        continue;
                    }
#endif

                    // Apply lighting
#if defined(LIGHTING_ON)
                    half factor = smoothstep(_LightingGradientThresholdStart, _LightingGradientThresholdEnd, gradMag);
                    half3 shaded = calculateLighting(src.rgb, gradient / gradMag, getLightDirection(-ray.direction), -ray.direction, 0.3f);
                    src.rgb = lerp(src.rgb, shaded, factor);
#if defined(SHADOWS_ON)
                    half shadow = calculateShadow(currPos, getLightDirection(-ray.direction));
                    src.rgb *= (1.0f - shadow);
#endif
#endif

                    // Opacity correction
                    float blendFactor = 512.0/ _StepCount;
                    src.a = 1.0f - pow(1.0f - src.a, blendFactor);
                    src.rgb *= src.a;
                    col = (1.0f - col.a) * src + col;

                    if (col.a > 0.15) {
                        float currentTDepth = t / length(ray.endPos - ray.startPos);
                        if(currentTDepth < tDepth)
                            tDepth = currentTDepth;
                    }

                    // Early ray termination
#if defined(RAY_TERMINATE_ON)
                    if (col.a > OPACITY_THRESHOLD) {
                        break;
                    }
#endif
                    t += raymarchInfo.stepSize;
                }

                // Write fragment output
                frag_out output;
                output.colour = col;
#if defined(DEPTHWRITE_ON)
                tDepth += (step(col.a, 0.0) * 1000.0); // Write large depth if no hit
                const float3 depthPos = ray.startPos + tDepth * (ray.endPos - ray.startPos) - float3(0.5f, 0.5f, 0.5f);
                output.depth = localToDepth(depthPos);
#endif
                return output;
            }

            // Maximum Intensity Projection mode
            frag_out frag_mip(frag_in i)
            {
                #define MAX_NUM_STEPS 512
                const int samplingRate = (int)_StepCount;

                RayInfo ray = getRayBack2Front(i.vertexLocal);
                RaymarchInfo raymarchInfo = initRaymarch(ray, samplingRate);

                float maxDensity = 0.0f;
                float3 maxDensityPos = ray.startPos;
                for (int iStep = 0; iStep < raymarchInfo.numSteps; iStep++)
                {
                    const float t = iStep * raymarchInfo.numStepsRecip;
                    const float3 currPos = lerp(ray.startPos, ray.endPos, t);
                    
#if defined(CROSS_SECTION_ON)
                    if (IsCutout(currPos))
                        continue;
#endif

                    if (currPos.z < _SliceMin || currPos.z > _SliceMax)
                        continue;
                    const float density = getDensity(currPos);
                    if (density < _Threshold) continue;
                    if (density > maxDensity && density > _MinVal && density < _MaxVal)
                    {
                        maxDensity = density;
                        maxDensityPos = currPos;
                    }
                }

                // Write fragment output
                frag_out output;
                output.colour = float4(1.0f, 1.0f, 1.0f, maxDensity); // maximum intensity
#if defined(DEPTHWRITE_ON)
                output.depth = localToDepth(maxDensityPos - float3(0.5f, 0.5f, 0.5f));
#endif
                return output;
            }

            // Surface rendering mode
            // Draws the first point (closest to camera) with a density within the user-defined thresholds.
            frag_out frag_surf(frag_in i)
            {
                #define MAX_NUM_STEPS 1024
                const int samplingRate = (int)_StepCount;

                RayInfo ray = getRayFront2Back(i.vertexLocal);
                RaymarchInfo raymarchInfo = initRaymarch(ray, samplingRate);

                // Create a small random offset in order to remove artifacts
                ray.startPos = ray.startPos + (JITTER_FACTOR * ray.direction * raymarchInfo.stepSize) * tex2D(_NoiseTex, float2(i.uv.x, i.uv.y)).r;

                half4 col = half4(0,0,0,0);
                float tDepth = 1.0;
                float t = 0.0;
                for (int iStep = 0; iStep < raymarchInfo.numSteps; iStep++)
                {
                    float3 currPos = ray.startPos + t * ray.direction;
                    if (currPos.x > 1.0f || currPos.x < 0.0f || currPos.y > 1.0f || currPos.y < 0.0f || currPos.z > 1.0f || currPos.z < 0.0f)
                        break;
                    
#if defined(CROSS_SECTION_ON)
                    if (IsCutout(currPos))
                    {
                        t += raymarchInfo.stepSize;
                        continue;
                    }
#endif

                    if (currPos.z < _SliceMin || currPos.z > _SliceMax)
                    {
                        t += raymarchInfo.stepSize;
                        continue;
                    }

                    const float density = getDensity(currPos);

#if defined(ADAPTIVE_RAYMARCHING_ON)
                    // If in empty space, take a larger step
                    if(density < _EmptySpaceThreshold)
                    {
                        t += raymarchInfo.stepSize * _EmptySpaceStepFactor;
                        iStep += (int)_EmptySpaceStepFactor - 1;
                        continue;
                    }
#endif
                    if (density < _Threshold)
                    {
                        t += raymarchInfo.stepSize;
                        continue;
                    }

#if defined(MULTIVOLUME_ISOLATE)
                    const float secondaryDensity = getSecondaryDensity(currPos);
                    if (secondaryDensity <= 0.0)
                    {
                        t += raymarchInfo.stepSize;
                        continue;
                    }
#elif defined(MULTIVOLUME_OVERLAY)
                    const float secondaryDensity = getSecondaryDensity(currPos);
                    if (secondaryDensity > 0.0)
                    {
                        half4 secondaryColour = getSecondaryTF1DColour(secondaryDensity);
                        if (secondaryColour.a > 0.0)
                        {
                            col = secondaryColour;
                            float3 gradient = getGradient(currPos);
                            float gradMag = length(gradient);
                            half3 normal = gradient / gradMag;
                            col.rgb = calculateLighting(col.rgb, normal, getLightDirection(-ray.direction), -ray.direction, 0.15);
                            col.a = 1.0;
                            tDepth = t / length(ray.endPos - ray.startPos);
                            break;
                        }
                    }
#endif
                    if (density > _MinVal && density < _MaxVal)
                    {
                        float3 gradient = getGradient(currPos);
                        float gradMag = length(gradient);
                        if (gradMag > _MinGradient)
                        {
                            half3 normal = gradient / gradMag;
                            col = getTF1DColour(density);
                            col.rgb = calculateLighting(col.rgb, normal, getLightDirection(-ray.direction), -ray.direction, 0.15);
                            col.a = 1.0f;
                            tDepth = t / length(ray.endPos - ray.startPos);
                            break;
                        }
                    }
                    t += raymarchInfo.stepSize;
                }

                // Write fragment output
                frag_out output;
                output.colour = col;
#if defined(DEPTHWRITE_ON)
                tDepth += (step(col.a, 0.0) * 1000.0); // Write large depth if no hit
                const float3 depthPos = ray.startPos + tDepth * (ray.endPos - ray.startPos) - float3(0.5f, 0.5f, 0.5f);
                output.depth = localToDepth(depthPos);
#endif
                return output;
            }

            frag_in vert(vert_in v)
            {
                return vert_main(v);
            }

            frag_out frag(frag_in i)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

#if MODE_DVR
                return frag_dvr(i);
#elif MODE_MIP
                return frag_mip(i);
#elif MODE_SURF
                return frag_surf(i);
#endif
            }

            ENDCG
        }
    }
} 