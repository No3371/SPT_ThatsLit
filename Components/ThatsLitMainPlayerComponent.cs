using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.Weather;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ThatsLit.Components
{
    public struct FrameStats
    {
        public int shinePixels, highLightPixels, highMidLightPixels, midLightPixels, midLowLightPixels, lowLightPixels, darkPixels;
        public int brighterPixels, darkerPixels;
        public float ratiohighLightPixels, ratiohighMidLightPixels, ratioMidLightPixels, ratiomidLowLightPixels, ratioLowLightPixels, ratioDarkPixels;
        public float avgLum, multiAvgLum;
        public int validPixels;
        public float score;
    }

    public class ThatsLitMainPlayerComponent : MonoBehaviour
    {
        const int RESOLUTION = 64;
        public const int POWER = 3;
        public RenderTexture rt, envRt;
        public Camera cam, envCam;
        int currentCamPos = 0;
        public Texture2D tex, debugTex, envTex, envDebugTex;
        public FrameStats frame1, frame2, frame3, frame4, frame5;
        public float multiFrameLitScore;
        public float multiFrameLitScoreSample;
        public int shinePixels, highLightPixels, highMidLightPixels, midLightPixels, midLowLightPixels, lowLightPixels, darkPixels;
        public int brighterPixels, darkerPixels;
        public float shinePixelsRatioSample, highLightPixelsRatioSample, highMidLightPixelsRatioSample, midLightPixelsRatioSample, midLowLightPixelsRatioSample, lowLightPixelsRatioSample, darkPixelsRatioSample;
        public float shineScore = 10f, highLightScore = 5f, highMidLightScore = 2f , midLightScore = 1f, midLowLightScore = 0.75f, lowLightScore = 0.4f;
        public float frameLitScore, frameLitScoreRaw0, frameLitScoreRaw1, frameLitScoreRaw2, frameLitScoreRaw3, frameLitScoreRaw4;
        public float frameLitScoreSample, frameitScoreRawSample0, frameitScoreRawSample1, frameitScoreRawSample2, frameitScoreRawSample3, frameitScoreRawSample4;
        public float brightestFrameScore, darkestFrameScore;
        public float lastBrightestSample, lastDarkestSample;
        public float lastCalcFrom, lastCalcTo;
        public float avgLum, multiAvgLum, avgLumSample, multiAvgLumSample;
        public float envLumEsti, envLumEstiFast, envLumEstiSlow, globalLumEsti = 0.05f;
        public int calced = 0, calcedLastFrame = 0;
        public int lockPos = -1;
        public int lastValidPixels = RESOLUTION * RESOLUTION;
        public RawImage display;
        public RawImage displayEnv;
        public bool disableVisionPatch;

        public float cloudinessCompensationScale = 0.5f;

        public float foliageScore;
        Collider[] collidersCache;
        public LayerMask foliageLayerMask = 1 << LayerMask.NameToLayer("Foliage") | 1 << LayerMask.NameToLayer("PlayerSpiritAura");

        float startAt, lastCheckedLights;
        public bool secondaryShining, lightOn, laserOn, lightIR, laserIR;

        public Vector3 envCamOffset = new Vector3(0, 2, 0);

        public void Awake()
        {
            Singleton<ThatsLitMainPlayerComponent>.Instance = this;
            MainPlayer = Singleton<GameWorld>.Instance.MainPlayer;

            rt = new RenderTexture(RESOLUTION, RESOLUTION, 0);
            rt.filterMode = FilterMode.Point;
            rt.Create();

            tex = new Texture2D(RESOLUTION, RESOLUTION);


            //cam = GameObject.Instantiate<Camera>(Singleton<PlayerCameraController>.Instance.Camera);
            cam = new GameObject().AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cam.transform.SetParent(MainPlayer.Transform.Original);

            cam.nearClipPlane = 0.001f;

            cam.cullingMask = LayerMaskClass.PlayerMask;
            cam.fieldOfView = 44;

            cam.targetTexture = rt;

            envRt = new RenderTexture(RESOLUTION, RESOLUTION, 0);
            envRt.filterMode = FilterMode.Point;
            envRt.Create();

            envTex = new Texture2D(RESOLUTION / 2, RESOLUTION / 2);

            envCam = new GameObject().AddComponent<Camera>();
            envCam.clearFlags = CameraClearFlags.SolidColor;
            envCam.backgroundColor = Color.white;
            envCam.transform.SetParent(MainPlayer.Transform.Original);
            envCam.transform.localPosition = Vector3.up * 3;

            envCam.nearClipPlane = 0.01f;

            envCam.cullingMask &= ~LayerMaskClass.PlayerMask;
            envCam.fieldOfView = 90;

            envCam.targetTexture = envRt;


            if (ThatsLitPlugin.DebugTexture.Value)
            {
                debugTex = new Texture2D(RESOLUTION, RESOLUTION);
                display = new GameObject().AddComponent<RawImage>();
                display.transform.SetParent(MonoBehaviourSingleton<GameUI>.Instance.RectTransform());
                display.RectTransform().sizeDelta = new Vector2(160, 160);
                display.texture = debugTex;
                display.RectTransform().anchoredPosition = new Vector2(-720, -360);

                envDebugTex = new Texture2D(RESOLUTION / 2, RESOLUTION / 2);
                displayEnv = new GameObject().AddComponent<RawImage>();
                displayEnv.transform.SetParent(MonoBehaviourSingleton<GameUI>.Instance.RectTransform());
                displayEnv.RectTransform().sizeDelta = new Vector2(160, 160);
                displayEnv.texture = envDebugTex;
                displayEnv.RectTransform().anchoredPosition = new Vector2(-560, -360);
            }

            collidersCache = new Collider[16];

            startAt = Time.time;
            globalLumEsti = 0.05f + 0.04f * GetTimeLighingFactor();
        }


        private void Update()
        {
            if (lockPos != -1) currentCamPos = lockPos;
            var camHeight = MainPlayer.IsInPronePose ? 0.45f : 2.2f;
            var targetHeight = MainPlayer.IsInPronePose ? 0.2f : 0.7f;
            var horizontalScale = MainPlayer.IsInPronePose ? 1.2f : 1;
            switch (currentCamPos++)
            {
                case 0:
                    {
                        if (MainPlayer.IsInPronePose)
                        {
                            cam.transform.localPosition = new Vector3(0, 2, 0);
                            cam.transform.LookAt(MainPlayer.Transform.Original.position);
                        }
                        else
                        {
                            cam.transform.localPosition = new Vector3(0, camHeight, 0);
                            cam.transform.LookAt(MainPlayer.Transform.Original.position);
                        }
                    break;
                }
                case 1:
                    {
                        cam.transform.localPosition = new Vector3(0.7f * horizontalScale, camHeight, 0.7f * horizontalScale);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
                case 2:
                    {
                        cam.transform.localPosition = new Vector3(0.7f * horizontalScale, camHeight, -0.7f * horizontalScale);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
                case 3:
                    {
                        if (MainPlayer.IsInPronePose)
                        {
                            cam.transform.localPosition = new Vector3(0, 2f, 0);
                            cam.transform.LookAt(MainPlayer.Transform.Original.position);
                        }
                        else
                        {
                            cam.transform.localPosition = new Vector3(0, -0.5f,  0.35f);
                            cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * 1f);
                        }
                        break;
                    }
                case 4:
                    {
                        cam.transform.localPosition = new Vector3(-0.7f * horizontalScale, camHeight, -0.7f * horizontalScale);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
                case 5:
                    {
                        cam.transform.localPosition = new Vector3(-0.7f * horizontalScale, camHeight, 0.7f * horizontalScale);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                        currentCamPos = 0;
                        break;
                    }
            }
            Vector3 bodyPos = MainPlayer.MainParts[BodyPartType.body].Position;

            envCam.transform.localPosition = envCamOffset;
            switch (currentCamPos)
            {
                case 0:
                    {
                        envCam.transform.LookAt(bodyPos + Vector3.left * 25);
                        break;
                    }
                case 1:
                    {
                        envCam.transform.LookAt(bodyPos + Vector3.right * 25);
                        break;
                    }
                case 2:
                    {
                        envCam.transform.localPosition = envCamOffset;
                        envCam.transform.LookAt(bodyPos + Vector3.up * 10);
                        break;
                    }
                case 3:
                    {
                        envCam.transform.LookAt(bodyPos + Vector3.back * 25);
                        break;
                    }
                case 4:
                    {
                        envCam.transform.LookAt(bodyPos + Vector3.right * 25);
                        break;
                    }
            }

            if (Time.time > lastCheckedLights + 0.33f)
            {
                lastCheckedLights = Time.time;
                DetermineShiningEquipments(ref secondaryShining, ref lightOn, ref laserOn, ref laserIR, ref lightIR);


                lastCheckedFoliageAndTerrain = Time.time;
                foliageScore = terrainScore = 0;

                for (int i = 0; i < collidersCache.Length; i++)
                    collidersCache[i] = null;

                ObstacleCollider c;
                //float count = Physics.OverlapSphereNonAlloc(bodyPos, 5f, collidersCache, LayerMaskClass.TriggersMask);

                //if (count > 0)
                //for (int i = 0; i < collidersCache.Length; i++)
                //{
                //    if (collidersCache[i] != null)
                //    {
                //        if (!collidersCache[i].transform.parent.gameObject.GetComponentInChildren<ObstacleCollider>()
                //            || !(collidersCache[i].transform.parent.gameObject.name.Contains("filbert")
                //            && !collidersCache[i].transform.parent.gameObject.name.Contains("fern_"))) continue;

                //        float dis = (collidersCache[i].transform.position - bodyPos).magnitude;
                //        if (dis < 0.3f) foliageScore += 1;
                //        else if (dis < 0.7f) foliageScore += 0.5f;
                //        else if (dis < 1.5f) foliageScore += 0.2f;
                //        else foliageScore += 0.1f;
                //        count++;
                //    }

                //    foliageScore /= count;
                //}

                float count = Physics.OverlapSphereNonAlloc(bodyPos, 5f, collidersCache, foliageLayerMask);

                for (int i = 0; i < count; i++)
                {
                    float dis = (collidersCache[i].transform.position - bodyPos).magnitude;
                    if (dis < 0.4f) foliageScore += 0.8f;
                    else if (dis < 0.6f) foliageScore += 0.5f;
                    else if (dis < 1f) foliageScore += 0.3f;
                    else if (dis < 2f) foliageScore += 0.15f;
                    else if (dis < 4f) foliageScore += 0.05f;
                    else foliageScore += 0.02f;
                }

                if (count > 0) foliageScore /= count;
                if (count == 1) foliageScore /= 2f;
            }
        }

        void LateUpdate ()
        {
            GetWeatherStats(out float fog, out float rain, out float cloud);
            frame5 = frame4;
            frame4 = frame3;
            frame3 = frame2;
            frame2 = frame1;
            frame1 = new FrameStats
            {
                shinePixels = shinePixels,
                highLightPixels = highLightPixels,
                highMidLightPixels = highMidLightPixels,
                midLightPixels = midLightPixels,
                midLowLightPixels = midLowLightPixels,
                lowLightPixels = lowLightPixels,
                darkPixels = darkPixels,
                avgLum = avgLum,
                multiAvgLum = multiAvgLum,
                score = frameLitScore,
                validPixels = lastValidPixels,
                darkerPixels = darkerPixels,
                ratiohighLightPixels = highLightPixels / (float)lastValidPixels,
                ratiohighMidLightPixels = highMidLightPixels / (float)lastValidPixels,
                ratioMidLightPixels = midLightPixels / (float)lastValidPixels,
                ratiomidLowLightPixels = midLowLightPixels / (float)lastValidPixels,
                ratioLowLightPixels = lowLightPixels / (float)lastValidPixels,
                ratioDarkPixels = darkPixels / (float)lastValidPixels
            }; ;
            frameLitScore = shinePixels = highLightPixels = highMidLightPixels = midLightPixels = midLowLightPixels = lowLightPixels = darkPixels = 0;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            //RenderTexture.active = envRt;
            //envTex.ReadPixels(new Rect(0, 0, envRt.width, envRt.height), 0, 0);
            //envTex.Apply();
            //RenderTexture.active = null;

            if (debugTex != null && Time.frameCount % 61 == 0)Graphics.CopyTexture(tex, debugTex);
            if (debugTex != null && Time.frameCount % 61 == 0) Graphics.CopyTexture(envTex, envDebugTex);

            var validPixels = 0;
            multiAvgLum = 0f;
            avgLum = 0.0f;
            //for (int x = 0; x < RESOLUTION; x++)
            //{
            //    for (int y = 0; y < RESOLUTION; y++)
            //    {
            //        var c = tex.GetPixel(x, y);
            //        if (c == Color.white)
            //        {
            //            continue;
            //        }
            //        var lum = (c.r + c.g + c.b) / 3f;
            //        avgLum += lum;
            //        if (lum > 0.75f) shinePixels += 1;
            //        else if (lum > 0.5f) highLightPixels += 1;
            //        else if (lum > 0.25f) highMidLightPixels += 1;
            //        else if (lum > 0.13f) midLightPixels += 1;
            //        else if (lum > 0.055f) midLowLightPixels += 1;
            //        else if (lum > 0.015f) lowLightPixels += 1;
            //        else darkPixels += 1;
            //        validPixels++;
            //    }
            //}

            CountPixels(tex, ref shinePixels, ref highLightPixels, ref highMidLightPixels, ref midLightPixels, ref midLowLightPixels, ref lowLightPixels, ref darkPixels, ref avgLum, ref validPixels);

            void CountPixels (Texture2D tex,  ref int shine, ref int high, ref int highMid, ref int mid, ref int midLow, ref int low, ref int dark, ref float lum, ref int valid)
            {
                for (int x = 0; x < RESOLUTION; x++)
                {
                    for (int y = 0; y < RESOLUTION; y++)
                    {
                        var c = tex.GetPixel(x, y);
                        if (c == Color.white)
                        {
                            continue;
                        }
                        var pLum = (c.r + c.g + c.b) / 3f;
                        lum += pLum;

                        float v = GetTimeLighingFactor();
                        if (v < 0) // Night
                        {
                            if (pLum > 0.75f) shine += 1;
                            else if (pLum > 0.5f) high += 1;
                            else if (pLum > 0.25f) highMid += 1;
                            else if (pLum > 0.13f) mid += 1;
                            else if (pLum > 0.055f - 0.01f * v) midLow += 1;
                            else if (pLum > 0.015f - 0.005f * v) low += 1;
                            else dark += 1;
                        }
                        else
                        {
                            if (pLum > 0.75f + 0.05f * v) shine += 1;
                            else if (pLum > 0.5f + 0.03f * v) high += 1;
                            else if (pLum > 0.25f + 0.02f * v) highMid += 1;
                            else if (pLum > 0.13f - 0.02f * v) mid += 1;
                            else if (pLum > 0.055f - 0.01f * v) midLow += 1;
                            else if (pLum > 0.015f - cloud * 0.07f * (1 - v) / 2f) low += 1;
                            else dark += 1;
                        }

                        valid++;
                    }
                }
            }

            if (validPixels == 0) validPixels = RESOLUTION * RESOLUTION; // Not sure if the RenderTexture will be empty (fully white) or not at the very begining

            brighterPixels = shinePixels + highLightPixels + highMidLightPixels + midLightPixels / 2;
            var litPixels = validPixels - darkPixels;
            var darkerLitPixels = midLightPixels / 2 + midLowLightPixels + lowLightPixels;
            darkerPixels = validPixels - brighterPixels;

            var modifiedMidScore = midLightScore;
            var modifiedMidLowScore = midLowLightScore;
            var modifiedLowScore = lowLightScore;

            if (GetTimeLighingFactor() < 0)
            {
                // Nights looks overally brighter when the weather is clear and vice versa, try to match the visual (cloudiness seems to be -1 (clear) ~ 1.5)
                modifiedMidLowScore *= 1 + cloud * GetTimeLighingFactor() / 4f - GetTimeLighingFactor() / 5f; // The cloudiness is scaled by darkness at the time
                modifiedLowScore *= 1 + cloud * GetTimeLighingFactor() / 4f - GetTimeLighingFactor() / 5f;
                // cloud = 0.4, time = -1 (night) => 0.8x (cloudy night)
                // cloud = -0.4, time = -1 (night) => 1.2x (clear night)
                // cloud = -1, time = -1 (night) => 1.5x
            }
            else
            {
                // Days looks dimmer when the weather is cloudy and vice versa, try to match the visual
                // modifiedMidLowScore *= 1 + Mathf.Abs(cloud * GetTimeLighingFactor() / 2f);
                modifiedLowScore *= 1 - cloud * GetTimeLighingFactor() / 6f + GetTimeLighingFactor() / 6f;
                // The captured character is way dimmer when cloudy...
                // cloud = 0.4, time = 1 (day) => 0.8x (cloudy day)

                // cloud = -0.4, time = 1 (day) => 1.2x (clear day)
            }


            frameLitScore = shinePixels * shineScore
                          + highLightPixels * highLightScore
                          + highMidLightPixels * highMidLightScore
                          + midLightPixels * modifiedMidScore
                          + midLowLightPixels * modifiedMidLowScore
                          + lowLightPixels * modifiedLowScore;
            avgLum /= (float)validPixels;
            lastValidPixels = validPixels;

            var lowAndDarkRatio = (lowLightPixels + darkPixels) / (float) validPixels;
            var lowLightRatio = lowLightPixels / (float) validPixels;
            var lowLightRatioLit = lowLightPixels / (float) litPixels;


            float avgLowLightPixelsRatioMultiFrames = lowLightPixels / (float)validPixels + frame1.ratioLowLightPixels + frame2.ratioLowLightPixels + frame3.ratioLowLightPixels + frame4.ratioLowLightPixels + frame5.ratioLowLightPixels;
            float avgDarkPixelsRatioMultiFrames = darkPixels / (float)validPixels + frame1.ratioDarkPixels + frame2.ratioDarkPixels + frame3.ratioDarkPixels + frame4.ratioDarkPixels + frame5.ratioDarkPixels;
            float avgDarkerPixelsRatioMultiFrames = darkerPixels / (float)validPixels + frame1.darkerPixels / (float)frame1.validPixels + frame2.darkerPixels / (float)frame2.validPixels + frame3.darkerPixels / (float)frame3.validPixels + frame4.darkerPixels / (float)frame4.validPixels + frame5.darkerPixels / (float)frame5.validPixels;
            float avgLumMultiFrames = avgLum + frame1.avgLum + frame2.avgLum + frame3.avgLum + frame4.avgLum + frame5.avgLum;
            avgLowLightPixelsRatioMultiFrames /= 6f;
            avgDarkPixelsRatioMultiFrames /= 6f;
            avgDarkerPixelsRatioMultiFrames /= 6f;
            avgLumMultiFrames /= 6f;
            multiAvgLum = avgLumMultiFrames;

            // Slowly moving towards current ratio or low and dark pixels
            //movingLowLightAndDarkRatio = Mathf.Lerp(movingLowLightAndDarkRatio, (avgDarkPixelsRatioMultiFrames + avgLowLightPixelsRatioMultiFrames), 1 / 6f);

            envLumEstiFast = Mathf.Lerp(envLumEstiFast, avgLumMultiFrames, Time.deltaTime);
            envLumEsti = Mathf.Lerp(envLumEsti, avgLumMultiFrames, Time.deltaTime / 3f);
            envLumEstiSlow = Mathf.Lerp(envLumEstiSlow, avgLumMultiFrames, Time.deltaTime / 10f);
            if (Time.time - startAt > 20f)
                globalLumEsti = Mathf.Lerp(globalLumEsti, avgLumMultiFrames, Time.deltaTime / (1 + Mathf.Min((Time.time + 20 - startAt) * 2f, 300f)));


            //// When entering dark places in bright env, we need to prevent the lights in the dark places not getting suppressed
            //var suppressScale = (envLumEsti - globalLumEsti) / 0.1f;


            frameLitScore /= (float) validPixels;
            // Transform to -1 ~ 1
            frameLitScore = Mathf.Clamp01(frameLitScore);
            frameLitScore -= 0.5f;
            frameLitScore *= 2f;

            frameLitScoreRaw0 = frameLitScore;
            // ↑ 0 ~ 1
            // ↓ -1 ~ 1

            // Suppress highlight score for likely bright env (daylight)
            if (globalLumEsti > 0.05f && frameLitScore > 0)
            {
                var daylightSurpressFactor = (globalLumEsti - 0.05f) / 0.15f;

                // If the avg lum is high but low and dark pixels are few than it means it's so fucking bright that most pixels are midLow+
                // So the fewer the low and dark pixels (50% base), the stronger the suppression
                //daylightSurpressFactor *= 1 + Mathf.Clamp01(0.5f - (lowLightPixels + darkPixels) / validPixels) / 1f; // 50% low+dark => 1x, 0% low+dark => 1.5x

                daylightSurpressFactor = Mathf.Clamp01(daylightSurpressFactor);
                daylightSurpressFactor *= daylightSurpressFactor;
                if (daylightSurpressFactor > 0)
                {
                    //frameLitScore -= highLightPixels * highLightScore;
                    //frameLitScore -= highMidLightPixels * highMidLightScore * 2 / 3;
                    //frameLitScore -= midLowLightPixels * midLowLightScore * 1 / 5 * daylightSurpressFactor;
                    frameLitScore /= 1 + daylightSurpressFactor / 2f;
                }
            }

            frameLitScoreRaw1 = frameLitScore;

            // Raise score for flashing lights especially in darker env
            var recentChangeFactor = Mathf.Clamp01(Mathf.Abs(avgLumMultiFrames - envLumEstiFast) / 0.2f);
            frameLitScore += recentChangeFactor * (1 - Mathf.Clamp01(envLumEstiSlow / 0.1f));
            frameLitScore = Mathf.Clamp(frameLitScore, -1, 1);

            // Reduce the score when the lum is stable
            recentChangeFactor = Mathf.Clamp01(Mathf.Abs(avgLumMultiFrames - envLumEsti) / 0.2f); // (avgLumMultiFrames - envLumEstiSlow) is always approaching zero when the environment lighting is stable
            if (frameLitScore > 0f) frameLitScore /= 1 + 0.3f * (1 - recentChangeFactor); // The bigger the difference, the more it should be suppressed
            else if (frameLitScore < 0f) frameLitScore *= 1 + 0.1f * (1 - recentChangeFactor);

            recentChangeFactor = Mathf.Clamp01(Mathf.Abs(avgLumMultiFrames - envLumEstiSlow) / 0.2f); // (avgLumMultiFrames - envLumEstiSlow) is always approaching zero when the environment lighting is stable
            if (frameLitScore > 0f) frameLitScore /= 1 + 0.1f * (1 - recentChangeFactor); // The bigger the difference, the more it should be suppressed
            else if (frameLitScore < 0f) frameLitScore *= 1 + 0.1f * (1 - recentChangeFactor);

            recentChangeFactor = Mathf.Clamp01(Mathf.Abs(avgLumMultiFrames - globalLumEsti) / 0.1f);
            if (frameLitScore > 0f) frameLitScore /= 1 + 0.1f * (1 - recentChangeFactor);
            else if (frameLitScore < 0f) frameLitScore *= 1 + 0.05f * (1 - recentChangeFactor);

            //// Overally lit but mostly low light pixels, probably in the day light or cloudy days and not receiving any additional lights
            //// In this case the score will be a bit too low (negative)
            //// Let's bring it closer to neutral
            //if (frameLitScore < 0.1f && lowAndDarkRatio > 0.5f && lowLightRatioLit > 0.75f && darkPixels / validPixels < 0.5f)
            //{
            //    frameLitScore = Mathf.Lerp(frameLitScore, 0.1f, (1 - darkPixels / validPixels));
            //}
            //// Overally lit and not mostyly just low light pixels, probably in the day light
            //// In this case the score will be a bit too high
            //// Let's bring it closer to 0.1 (neutral)
            //if (frameLitScore > 0.1f && darkerPixels > validPixels * 0.8f && lowAndDarkRatio < 0.5f)
            //{
            //    frameLitScore = Mathf.Lerp(frameLitScore, 0.1f, 0.5f - lowAndDarkRatio);
            //}

            frameLitScore = Mathf.Clamp(frameLitScore , - 1, 1);

            frameLitScoreRaw2 = frameLitScore;

            brightestFrameScore = frameLitScore;
            if (frame1.score > brightestFrameScore) brightestFrameScore = frame1.score;
            if (frame2.score > brightestFrameScore) brightestFrameScore = frame2.score;
            if (frame3.score > brightestFrameScore) brightestFrameScore = frame3.score;
            if (frame4.score > brightestFrameScore) brightestFrameScore = frame4.score;
            if (frame5.score > brightestFrameScore) brightestFrameScore = frame5.score;
            darkestFrameScore = frameLitScore;
            if (frame1.score < darkestFrameScore) darkestFrameScore = frame1.score;
            if (frame2.score < darkestFrameScore) darkestFrameScore = frame2.score;
            if (frame3.score < darkestFrameScore) darkestFrameScore = frame3.score;
            if (frame4.score < darkestFrameScore) darkestFrameScore = frame4.score;
            if (frame5.score < darkestFrameScore) darkestFrameScore = frame5.score;

            // Max contrast between all frames (sides)
            float contrast = brightestFrameScore - darkestFrameScore;
            if (contrast < 0.3f) // Low contrast, enhance darkness
            {
                if (frameLitScore < 0 && (darkerPixels > 0.75f * validPixels || lowAndDarkRatio > 0.8f))
                {
                    var enhacement = 2 * contrast * contrast;
                    enhacement *= (1 - (brighterPixels + midLightPixels / 2) / validPixels); // Any percentage of pixels brighter than mid scales the effect down
                    frameLitScore *= (1 + enhacement);
                }
            }

            frameLitScore = Mathf.Clamp(frameLitScore, -1, 1);


            //The average score of other frames(sides)
            var avgScorePrevFrames = (frame1.score + frame2.score + frame3.score + frame4.score + frame5.score) / 5f;
            // The contrast between the brightest frame and average
            var avgContrastFactor = frameLitScore - avgScorePrevFrames; // could be up to 2
            if (avgContrastFactor > 0) // Brighter than avg
            {
                // Extra score for higher contrast (Easier to notice)
                avgContrastFactor /= 2f; // Compress to 0 ~ 1
                frameLitScore += avgContrastFactor / 10f;
                avgContrastFactor = Mathf.Pow(1.1f * avgContrastFactor, 2); // Curve
                frameLitScore = Mathf.Lerp(frameLitScore, brightestFrameScore, Mathf.Clamp(avgContrastFactor, 0, 1));
            }
            //Darker than avg? Doesn't matter

            frameLitScoreRaw3 = frameLitScore;

            //frameLitScore = (frameLitScore + brightest) / 2f;

            // Contrast this frame (side), makes the player slightly more visible
            var frameContrastFactor = Mathf.Pow(((lowLightPixels + darkPixels) / validPixels) * 0.5f, 2) * Mathf.Pow(brighterPixels / validPixels, 2);
            if (frameLitScore < 0) frameLitScore /= 1 + frameContrastFactor / 2f; // low + dark tends to be pretty high in already dark env
            else frameLitScore *= 1 + frameContrastFactor / 3f;


            frameLitScoreRaw4 = frameLitScore;

            //if (brighterPixels / darkerPixels > 0.05f && darkerPixels > validPixels * 0.2f && lowLightPixels > validPixels * 0.2f)
            //{
            //    frameLitScore *= (1 + brighterPixels / darkerPixels);
            //    frameLitScore = Mathf.Lerp(frameLitScore, brightest, Mathf.Pow(brighterPixels / darkerPixels, 2));
            //}
            //if (highMidLightPixels / darkerPixels > 0 && darkerPixels > validPixels * 0.2f && lowLightPixels > validPixels * 0.2f)
            //{
            //    frameLitScore *= (1 + highMidLightPixels * 2 / darkerPixels);
            //    frameLitScore = Mathf.Lerp(frameLitScore, brightest, Mathf.Pow(highMidLightPixels * 3 / darkerPixels, 2));
            //}
            //if (highLightPixels / darkerPixels > 0 && darkerPixels > validPixels * 0.2f && lowLightPixels > validPixels * 0.2f)
            //{
            //    frameLitScore *= (1 + highLightPixels * 4 / darkerPixels);
            //    frameLitScore = Mathf.Lerp(frameLitScore, brightest, Mathf.Pow(highLightPixels * 10 / darkerPixels, 2));
            //}


            if (frameLitScore < 0)
            {
                if (MainPlayer.AIData.GetFlare) frameLitScore /= 2f;

                if (laserOn && !laserIR && !lightOn)
                {
                    if (secondaryShining) frameLitScore /= 1.1f;
                    else frameLitScore /= 1.5f;
                }
                else if (lightOn && !lightIR)
                {
                    if (secondaryShining) frameLitScore /= 1.3f;
                    else frameLitScore /= 2f;
                }
            }

            //Cloudy?
            //if (cloud > 0 && frameLitScore < 0)
            //{
            //    if (GetTimeLighingFactor() > 0)
            //        frameLitScore = Mathf.Lerp(frameLitScore, -1, cloud * cloudinessCompensationScale * 0.5f * GetTimeLighingFactor());
            //    else
            //        frameLitScore = Mathf.Lerp(frameLitScore, 0, cloud * cloudinessCompensationScale);
            //}

            frameLitScore = Mathf.Clamp(frameLitScore, -1, 1);
            
            multiFrameLitScore = (brightestFrameScore * 2f
                                + frameLitScore
                                + frame1.score
                                + frame2.score
                                + frame3.score
                                + frame4.score
                                + frame5.score
                                - darkestFrameScore) / 7f;

            // In bright map, move toward 0
            multiFrameLitScore = Mathf.Lerp(multiFrameLitScore, 0, Mathf.Clamp01((globalLumEsti - 0.09f) / 0.05f) * 0.5f);


            multiFrameLitScore += ThatsLitPlugin.ScoreOffset.Value;
        }

        private void OnDestroy()
        {
            if (display) GameObject.Destroy(display);
            GameObject.Destroy(cam);
            rt.Release();

        }

        private void OnGUI()
        {
            if (!ThatsLitPlugin.DebugInfo.Value && ThatsLitPlugin.LightingInfo.Value)
            {
                DrawAsymetricMeter((int)(multiFrameLitScore / 0.0999f));
                DrawAsymetricMeter((int)(Mathf.Pow(multiFrameLitScore, POWER) / 0.0999f));
            }
            if (!ThatsLitPlugin.DebugInfo.Value) return;
            if (Time.frameCount % 41 == 0)
            {
                shinePixelsRatioSample = shinePixels / (float)lastValidPixels;
                highLightPixelsRatioSample = highLightPixels / (float) lastValidPixels;
                highMidLightPixelsRatioSample = highMidLightPixels / (float)lastValidPixels;
                midLightPixelsRatioSample = midLightPixels / (float)lastValidPixels;
                midLowLightPixelsRatioSample = midLowLightPixels / (float)lastValidPixels;
                lowLightPixelsRatioSample = lowLightPixels / (float)lastValidPixels;
                darkPixelsRatioSample = darkPixels / (float)lastValidPixels;

                frameLitScoreSample = frameLitScore;
                frameitScoreRawSample0 = frameLitScoreRaw0;
                frameitScoreRawSample1 = frameLitScoreRaw1;
                frameitScoreRawSample2 = frameLitScoreRaw2;
                frameitScoreRawSample3 = frameLitScoreRaw3;
                frameitScoreRawSample4 = frameLitScoreRaw4;
                multiFrameLitScoreSample = multiFrameLitScore;
                lastDarkestSample = darkestFrameScore;
                lastBrightestSample = brightestFrameScore;
                avgLumSample = avgLum;
                multiAvgLumSample = multiAvgLum;
            }
            GUILayout.Label(string.Format("PIXELS: {0:000} - {1:000} - {2:000} - {3:0000} - {4:0000} - {5:0000} - {6:0000}", shinePixels, highLightPixels, highMidLightPixels, midLightPixels, midLowLightPixels, lowLightPixels, darkPixels));
            GUILayout.Label(string.Format("PIXELS: {0:000}% - {1:000}% - {2:000}% - {3:000}% - {4:000}% - {5:000}% - {6:000}% (Sample)", shinePixelsRatioSample * 100, highLightPixelsRatioSample * 100, highMidLightPixelsRatioSample * 100, midLightPixelsRatioSample * 100, midLowLightPixelsRatioSample * 100, lowLightPixelsRatioSample * 100, darkPixelsRatioSample * 100));
            
            GUILayout.Label(string.Format("SCORE : {0:＋0.00;－0.00;+0.00} -> {1:＋0.00;－0.00;+0.00} -> {2:＋0.00;－0.00;+0.00} -> {3:＋0.00;－0.00;+0.00} -> {4:＋0.00;－0.00;+0.00} -> {5:＋0.00;－0.00;+0.00} (FRAME) ", frameLitScoreRaw0, frameLitScoreRaw1, frameLitScoreRaw2, frameLitScoreRaw3, frameLitScoreRaw4, frameLitScore));
            GUILayout.Label(string.Format("SCORE : {0:＋0.00;－0.00;+0.00} -> {1:＋0.00;－0.00;+0.00} -> {2:＋0.00;－0.00;+0.00} -> {3:＋0.00;－0.00;+0.00} -> {4:＋0.00;－0.00;+0.00} -> {5:＋0.00;－0.00;+0.00} (FRAME) (SAMPLE)", frameitScoreRawSample0, frameitScoreRawSample1, frameitScoreRawSample2, frameitScoreRawSample3, frameitScoreRawSample4, frameLitScoreSample));
            DrawAsymetricMeter((int)(frameLitScore / 0.0999f));

            GUILayout.Label(string.Format("SCORE : {0:＋0.00;－0.00;+0.00} -> {1:＋000.0;－000.0;+000.0}% (MULTI)", multiFrameLitScore, Mathf.Pow(multiFrameLitScore, POWER) * 100));
            GUILayout.Label(string.Format("SCORE : {0:＋0.00;－0.00;+0.00} -> {1:＋000.0;－000.0;+000.0}% (MULTI) (SAMPLE)", multiFrameLitScoreSample, Mathf.Pow(multiFrameLitScoreSample, POWER) * 100));
            DrawAsymetricMeter((int)(multiFrameLitScore / 0.0999f));
            // Factor
            DrawAsymetricMeter((int)(Mathf.Pow(multiFrameLitScore, POWER) / 0.0999f));
            GUILayout.Label(string.Format("CONTRA: {0:＋0.00;－0.00} <-> {1:＋0.00;－0.00} ({2:0.00}) (SAMPLE)", lastDarkestSample, lastBrightestSample, lastBrightestSample - lastDarkestSample));
            GUILayout.Label(string.Format("AVGLUM: {0:＋0.000;－0.000} (SAMPLE) / {1:＋0.000;－0.000} (MULTI)", avgLumSample, multiAvgLum));
            GUILayout.Label(string.Format("ENVLUM: {0:＋0.000;－0.000} (1s) {1:＋0.000;－0.000} (3s) / {2:＋0.000;－0.000} (10s) / {3:＋0.000;－0.000} (5m)", envLumEstiFast, envLumEsti, envLumEstiSlow, globalLumEsti));
            GUILayout.Label(string.Format("IMPACT: {0:0.00} -> {1:0.00} (SAMPLE)", lastCalcFrom, lastCalcTo));
            //GUILayout.Label(text: "PIXELS:");
            //GUILayout.Label(lastValidPixels.ToString());
            GUILayout.Label(string.Format("AFFECTED: {0} (+{1})", calced, calcedLastFrame));

            GUILayout.Label(string.Format("TERRAIN: {0:0.000} / FOLIAGE: {1:0.000}", terrainScore, foliageScore));
            GUILayout.Label(string.Format("FOG: {0:0.000} / RAIN: {1:0.000} / CLOUD: {2:0.000} / {3} -> TIME_LIGHT: {4:0.00}", WeatherController.Instance?.WeatherCurve?.Fog ?? 0, WeatherController.Instance?.WeatherCurve?.Rain ?? 0, WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0, GetInGameDayTime(), GetTimeLighingFactor()));
            GUILayout.Label(string.Format("LIGHT: [{0}] / LASER: [{1}]", lightOn? lightIR? "I" : "V" : "  ", laserOn? laserIR? "I" : "V" : "  "));

        }

        void DrawAsymetricMeter (int level)
        {
            switch (level)
            {
                case -11:
                    GUILayout.Label("＋＋＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -10:
                    GUILayout.Label("＋＋＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -9:
                    GUILayout.Label("－＋＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -8:
                    GUILayout.Label("－－＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -7:
                    GUILayout.Label("－－－＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -6:
                    GUILayout.Label("－－－－＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -5:
                    GUILayout.Label("－－－－－＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -4:
                    GUILayout.Label("－－－－－－＋＋＋＋|－－－－－－－－－－");
                    break;
                case -3:
                    GUILayout.Label("－－－－－－－＋＋＋|－－－－－－－－－－");
                    break;
                case -2:
                    GUILayout.Label("－－－－－－－－＋＋|－－－－－－－－－－");
                    break;
                case -1:
                    GUILayout.Label("－－－－－－－－－＋|－－－－－－－－－－");
                    break;
                case 0:
                    GUILayout.Label("－－－－－－－－－－|－－－－－－－－－－");
                    break;
                case 1:
                    GUILayout.Label("－－－－－－－－－－|＋－－－－－－－－－");
                    break;
                case 2:
                    GUILayout.Label("－－－－－－－－－－|＋＋－－－－－－－－");
                    break;
                case 3:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋－－－－－－－");
                    break;
                case 4:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋－－－－－－");
                    break;
                case 5:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋－－－－－");
                    break;
                case 6:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋－－－－");
                    break;
                case 7:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋－－－");
                    break;
                case 8:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋－－");
                    break;
                case 9:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋＋－");
                    break;
                case 10:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋＋＋");
                    break;
                case 11:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋＋＋");
                    break;
            }
        }

        public Player MainPlayer { get; private set; }

        float GetInGameDayTime ()
        {
            if (Singleton<GameWorld>.Instance?.GameDateTime == null) return 19f;

            var GameDateTime = Singleton<GameWorld>.Instance.GameDateTime.Calculate();

            float minutes = GameDateTime.Minute / 59f;
            return GameDateTime.Hour + minutes;
        }

        //// 20 ~ 22.5 => 0 ~ 2 => 3 ~ 5 => 5 ~ 7
        // SUN- => MOON+ => MOON- => SUN+
        // DARK (-1) <-> BRIGHT (1)
        float GetTimeLighingFactor ()
        {
            var time = GetInGameDayTime();
            if (time >= 20 && time < 24)
                return -Mathf.Clamp01((time - 20f) / 2.5f);
            else if (time >= 0 && time < 3)
                return -0.5f - Mathf.Clamp01((2f - time) / 2f) * 0.5f; // 0:00 -> 100%, 2:00 -> 50%, 3:00 -> 50%
            else if (time >= 3 && time < 5)
                return -0.5f - Mathf.Clamp01((time - 3f) / 2f) * 0.5f; // 3:00 -> 50%, 5:00 -> 100%
            else if (time >= 5 && time < 7)
                return -Mathf.Clamp01((7f - time) / 2f); // 5:00 -> 100%, 5:00 -> 0%
            else if (time >= 7 && time < 13)
                return 1f - Mathf.Clamp01((13f - time) / 6f);
            else if (time >= 13 && time < 20)
                return Mathf.Clamp01((20f - time) / 7f);
            else return 0;
        }

        void GetWeatherStats (out float fog, out float rain, out float cloud)
        {
            if (WeatherController.Instance?.WeatherCurve == null)
            {
                fog = rain = cloud = 0;
               return;
            }

            fog = WeatherController.Instance.WeatherCurve.Fog;
            rain = WeatherController.Instance.WeatherCurve.Rain;
            cloud = WeatherController.Instance.WeatherCurve.Cloudiness;
        }

        void DetermineShiningEquipments (ref bool secondary, ref bool light, ref bool laser, ref bool laserIsIR, ref bool lightIsIR)
        {
            secondary = light = laser = laserIsIR = lightIsIR = false;
            IEnumerable<GClass2550> activeLights;
            if (MainPlayer?.ActiveSlot?.ContainedItem != null)
            {

                activeLights = (MainPlayer.ActiveSlot.ContainedItem as Weapon)?.AllSlots
                    .Select<Slot, Item>((Func<Slot, Item>)(x => x.ContainedItem))
                    .GetComponents<LightComponent>().Where(c => c.IsActive).Select(l => l.Item as GClass2550);

                if (activeLights != null)
                foreach (var i in activeLights)
                {
                    if (i == null) continue;
                    MapComponentsModes(i, ref light, ref laser, ref laserIsIR, ref lightIsIR);
                    if (light || laser) return;
                }
            }

            var inv = (InventoryControllerClass) MainPlayer?.ActiveSlot?.ContainedItem?.Owner;
            var helmet = inv?.Inventory?.Equipment?.GetSlot(EquipmentSlot.Headwear)?.ContainedItem as GClass2537;

            if (helmet != null)
            {
                activeLights = helmet.AllSlots
                    .Select<Slot, Item>((Func<Slot, Item>)(x => x.ContainedItem))
                    .GetComponents<LightComponent>().Where(c => c.IsActive).Select(l => l.Item as GClass2550);

                foreach (var i in activeLights)
                {
                    if (i == null) continue;
                    light = laser = laserIsIR = lightIsIR = false;
                    MapComponentsModes(i, ref light, ref laser, ref laserIsIR, ref lightIsIR);
                    if (light || laser) return;
                }
            }

            secondary = true;

            var secondaryWeapons = inv?.Inventory?.GetItemsInSlots(new[] { EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster });

            if (secondaryWeapons != null)
            foreach (Weapon w in secondaryWeapons)
            {
                activeLights = w?.AllSlots
                    .Select<Slot, Item>((Func<Slot, Item>)(x => x.ContainedItem))
                    .GetComponents<LightComponent>().Where(c => c.IsActive).Select(l => l.Item as GClass2550);

                if (w != null)
                foreach (var i in activeLights)
                {
                    if (i == null) continue;
                    light = laser = laserIsIR = lightIsIR = false;
                    MapComponentsModes(i, ref light, ref laser, ref laserIsIR, ref lightIsIR);
                    if (light || laser) return;
                }
            }
            // GClass2550 544909bb4bdc2d6f028b4577 x item tactical_all_insight_anpeq15 2457 / V + IR + IRL / MODES: 4  V -> IR -> IRL -> IR+IRL
            // 560d657b4bdc2da74d8b4572 tactical_all_zenit_2p_kleh_vis_laser MODES: 3, F -> F+V -> V
            // GClass2550 56def37dd2720bec348b456a item tactical_all_surefire_x400_vis_laser 2457 F + V MDOES: 3: F -> F + V -> V
            // 57fd23e32459772d0805bcf1 item tactical_all_holosun_ls321 2457 V + IR + IRL MDOES 4: V -> IR -> IRL -> IRL + IR
            // 55818b164bdc2ddc698b456c tactical_all_zenit_2irs_kleh_lam MODES: 3 IRL -> IRL+IR -> IR
            // 5a7b483fe899ef0016170d15 tactical_all_surefire_xc1 MODES: 1
            // 5a800961159bd4315e3a1657 tactical_all_glock_gl_21_vis_lam MODES 3
            // 5b07dd285acfc4001754240d tactical_all_steiner_las_tac_2 Modes 1

            // "_id": "5b3a337e5acfc4704b4a19a0", "_name": "tactical_all_zenit_2u_kleh", 1
            //"_id": "5c06595c0db834001a66af6c", "_name": "tactical_all_insight_la5", 4, V -> IR -> IRL -> IRL+IR
            //"_id": "5c079ed60db834001a66b372", "_name": "tactical_tt_dlp_tactical_precision_laser_sight", 1
            //"_id": "5c5952732e2216398b5abda2", "_name": "tactical_all_zenit_perst_3", 4
            //"_id": "5cc9c20cd7f00c001336c65d", "_name": "tactical_all_ncstar_tactical_blue_laser", 1
            //"_id": "5d10b49bd7ad1a1a560708b0", "_name": "tactical_all_insight_anpeq2", 2
            //"_id": "5d2369418abbc306c62e0c80", "_name": "tactical_all_steiner_9021_dbal_pl", 6 / F -> V -> F+V -> IRF -> IR -> IRF+IR
            //"_id": "61605d88ffa6e502ac5e7eeb", "_name": "tactical_all_wilcox_raptar_es", 5 / RF -> V -> IR -> IRL -> IRL+IR
            //"_id": "626becf9582c3e319310b837", "_name": "tactical_all_insight_wmx200", 2
            //"_id": "6272370ee4013c5d7e31f418", "_name": "tactical_all_olight_baldr_pro", 3
            //"_id": "6272379924e29f06af4d5ecb", "_name": "tactical_all_olight_baldr_pro_tan", 3


            //"_id": "57d17c5e2459775a5c57d17d", "_name": "flashlight_ultrafire_WF-501B", 1 (2) (different slot)
            //"_id": "59d790f486f77403cb06aec6", "_name": "flashlight_armytek_predator_pro_v3_xhp35_hi", 1(2)

        }
        void MapComponentsModes(GClass2550 comp, ref bool light, ref bool laser, ref bool laserIsIR, ref bool lightIsIR)
        {
            switch (comp.TemplateId)
            {
                case "544909bb4bdc2d6f028b4577": // tactical_all_insight_anpeq15
                case "57fd23e32459772d0805bcf1": // tactical_all_holosun_ls321
                case "5c06595c0db834001a66af6c": // tactical_all_insight_la5
                case "5c5952732e2216398b5abda2": // tactical_all_zenit_perst_3
                    switch (comp.Light.SelectedMode)
                    {
                        case 0:
                            laser = true;
                            break;
                        case 1:
                            laser = laserIsIR = true;
                            break;
                        case 2:
                            light = lightIsIR = true;
                            break;
                        case 3:
                            laser = laserIsIR = light = lightIsIR = true;
                            break;
                    }
                    break;
                case "61605d88ffa6e502ac5e7eeb": // tactical_all_wilcox_raptar_es
                    switch (comp.Light.SelectedMode)
                    {
                        case 1:
                            laser = true;
                            break;
                        case 2:
                            laser = laserIsIR = true;
                            break;
                        case 3:
                            light = lightIsIR = true;
                            break;
                        case 4:
                            laser = laserIsIR = light = lightIsIR = true;
                            break;
                    }
                    break;
                case "560d657b4bdc2da74d8b4572": // tactical_all_zenit_2p_kleh_vis_laser
                case "56def37dd2720bec348b456a": // tactical_all_surefire_x400_vis_laser
                case "5a800961159bd4315e3a1657": // tactical_all_glock_gl_21_vis_lam
                case "6272379924e29f06af4d5ecb": // tactical_all_olight_baldr_pro_tan
                case "6272370ee4013c5d7e31f418": // tactical_all_olight_baldr_pro
                    switch (comp.Light.SelectedMode)
                    {
                        case 0:
                            light = true;
                            break;
                        case 1:
                            laser = light = true;
                            break;
                        case 2:
                            laser = true;
                            break;
                    }
                    break;
                case "55818b164bdc2ddc698b456c": // tactical_all_zenit_2irs_kleh_lam
                    switch (comp.Light.SelectedMode)
                    {
                        case 0:
                            light = lightIsIR = true;
                            break;
                        case 1:
                            laser = laserIsIR = light = lightIsIR = true;
                            break;
                        case 2:
                            laser = laserIsIR = true;
                            break;
                    }
                    break;
                case "5a7b483fe899ef0016170d15": // tactical_all_surefire_xc1
                case "5b3a337e5acfc4704b4a19a0": // tactical_all_zenit_2u_kleh
                case "59d790f486f77403cb06aec6": // flashlight_armytek_predator_pro_v3_xhp35_hi
                case "57d17c5e2459775a5c57d17d": // flashlight_ultrafire_WF
                    light = true;
                    break;
                case "5b07dd285acfc4001754240d": // tactical_all_steiner_las_tac_2
                case "5c079ed60db834001a66b372": // tactical_tt_dlp_tactical_precision_laser_sight
                case "5cc9c20cd7f00c001336c65d": // tactical_all_ncstar_tactical_blue_laser
                    laser = true;
                    break;
                case "5d10b49bd7ad1a1a560708b0": // tactical_all_insight_anpeq2
                    switch (comp.Light.SelectedMode)
                    {
                        case 0:
                            laser = laserIsIR = true;
                            break;
                        case 1:
                            laser = laserIsIR = light = lightIsIR = true;
                            break;
                        case 2:
                            break;
                    }
                    break;
                case "5d2369418abbc306c62e0c80": // tactical_all_steiner_9021_dbal_pl
                    switch (comp.Light.SelectedMode)
                    {
                        case 0:
                            light = true;
                            break;
                        case 1:
                            laser = true;
                            break;
                        case 2:
                            laser = light = true;
                            break;
                        case 3:
                            light = lightIsIR = true;
                            break;
                        case 4:
                            laser = laserIsIR = true;
                            break;
                        case 5:
                            light = lightIsIR = laser = laserIsIR = true;
                            break;
                    }
                    break;
                case "626becf9582c3e319310b837": // tactical_all_insight_wmx200
                    switch (comp.Light.SelectedMode)
                    {
                        case 0:
                            light = true;
                            break;
                        case 1:
                            light = lightIsIR = true;
                            break;
                    }
                    break;
            }
        }
    }
}