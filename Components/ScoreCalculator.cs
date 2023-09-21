using EFT;
using UnityEngine;

namespace ThatsLit.Components
{
    public class ScoreCalculator
    {
        public float shineScore = 10f, highLightScore = 5f, highMidLightScore = 2f, midLightScore = 1f, midLowLightScore = 0.75f, lowLightScore = 0.4f, darkScore = 0.01f;
        internal float envLumEsti, envLumEstiFast, envLumEstiSlow, globalLumEsti = 0.05f;
        public FrameStats frame1, frame2, frame3, frame4, frame5;
        public void Calculate (Texture2D tex, float cloud, float fog, float rain, Player MainPlayer, float time, LocationSettingsClass.Location location)
        {

        }

        protected virtual void UpdateEnvLumEstimation (float avgLumThisFrame)
        {
            float avgLumMultiFrames = avgLumThisFrame + frame1.avgLumMultiFrames + frame2.avgLumMultiFrames + frame3.avgLumMultiFrames + frame4.avgLumMultiFrames + frame5.avgLumMultiFrames;
            envLumEstiFast = Mathf.Lerp(envLumEstiFast, avgLumMultiFrames, Time.deltaTime);
            envLumEsti = Mathf.Lerp(envLumEsti, avgLumMultiFrames, Time.deltaTime / 3f);
            envLumEstiSlow = Mathf.Lerp(envLumEstiSlow, avgLumMultiFrames, Time.deltaTime / 10f);
        }

        protected virtual void GetThresholds(float tlf, out float thresholdShine, out float thresholdHigh, out float thresholdHighMid, out float thresholdMid, out float thresholdMidLow, out float thresholdLow)
        {
            thresholdShine = 0.8f;
            thresholdHigh = 0.5f;
            thresholdHighMid = 0.25f;
            thresholdMid = 0.13f;
            thresholdMidLow = 0.055f;
            thresholdLow = 0.015f;
        }
        protected virtual void CountPixels(Texture2D tex, float time, ref int shine, ref int high, ref int highMid, ref int mid, ref int midLow, ref int low, ref int dark, ref float lum, ref int valid)
        {
            for (int x = 0; x < tex.width; x++)
            {
                for (int y = 0; y < tex.height; y++)
                {
                    var c = tex.GetPixel(x, y);
                    if (c == Color.white)
                    {
                        continue;
                    }
                    var pLum = (c.r + c.g + c.b) / 3f;
                    lum += pLum;

                    float thresholdShine, thresholdHigh, thresholdHighMid, thresholdMid, thresholdMidLow, thresholdLow;
                    GetThresholds(time, out thresholdShine, out thresholdHigh, out thresholdHighMid, out thresholdMid, out thresholdMidLow, out thresholdLow);
                    if (pLum > thresholdShine) shine += 1;
                    else if (pLum > thresholdHigh) high += 1;
                    else if (pLum > thresholdHighMid) highMid += 1;
                    else if (pLum > thresholdMid) mid += 1;
                    else if (pLum > thresholdMidLow) midLow += 1;
                    else if (pLum > thresholdLow) low += 1;
                    else dark += 1;

                    valid++;
                }
            }
        }

        // The visual brightness during the darkest hours with cloudiness 1... This is the base brightness of the map without any interference (e.g. moon light)
        // Moonlight only affect visual brightness when c < 1
        protected virtual float GetMapBaseAmbientBrightnessScore (string locationId, float time)
        {
            if (time >= 20 && time < 24)
                return -Mathf.Clamp01((time - 20f) / 2.25f);
            else if (time >= 0 && time < 3)
                return -0.25f - Mathf.Clamp01((2f - time) / 2f) * 0.75f; // 0:00 -> 100%, 1:00 -> 75%, 2:00 -> 25%, 3:00 -> 25%
            else if (time >= 3 && time < 5)
                return -0.25f - Mathf.Clamp01((time - 3f) / 2f) * 0.75f; // 3:00 -> 25%, 5:00 -> 100%
            else if (time >= 5 && time < 8)
                return -Mathf.Clamp01((8f - time) / 3f); // 5:00 -> 100%, 8:00 -> 0%
            else if (time >= 7 && time < 16)
                return 1f - Mathf.Clamp01((12f - time) / 6f);
            else if (time >= 16 && time < 20)
                return Mathf.Clamp01((20f - time) / 7f);
            else return 0;
            switch (locationId)
            {
                case "Lighthouse":
                    return -0.92f;
                case "Woods":
                    return 0f;
            }
        }

        // Fog determine visual brightness of further envrionment, unused
        // The increased visual brightness when moon is up (0~5) when c < 1
        // cloudiness blocks moon light
        protected virtual float CalculateMapMoonLight(string locationId, float time, float cloudiness)
        {
            switch (locationId)
            {
                case "Lighthouse":
                    return 1.5f;
                default:
                    return 1f;
            }
        }

        // The increased visual brightness when sun is up (5~22) hours when c < 1
        // cloudiness blocks sun light
        protected virtual float CalculateMapSunLight(string locationId, float time, float cloudiness)
        {
            switch (locationId)
            {
                case "Lighthouse":
                    return 1f;
                default:
                    return 1f;
            }
        }

        protected virtual float BaseAmbienceScore { get;  }
    }

    public class LighthouseScoreCalculator : ScoreCalculator
    {
        protected override float BaseAmbienceScore => -0.92f;
    }
}