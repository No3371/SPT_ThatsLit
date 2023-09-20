using EFT;
using UnityEngine;

namespace ThatsLit.Components
{
    public class ScoreFormula
    {
        public float shineScore = 10f, highLightScore = 5f, highMidLightScore = 2f, midLightScore = 1f, midLowLightScore = 0.75f, lowLightScore = 0.4f, darkScore = 0.01f;
        public void Calculate (Texture2D tex, float cloud, float fog, float rain, Player MainPlayer, float time, LocationSettingsClass.Location location)
        {

        }

        internal virtual void GetThresholds(float tlf, out float thresholdShine, out float thresholdHigh, out float thresholdHighMid, out float thresholdMid, out float thresholdMidLow, out float thresholdLow)
        {
            thresholdShine = 0.8f;
            thresholdHigh = 0.5f;
            thresholdHighMid = 0.25f;
            thresholdMid = 0.13f;
            thresholdMidLow = 0.055f;
            thresholdLow = 0.015f;
        }
        void CountPixels(Texture2D tex, float time, ref int shine, ref int high, ref int highMid, ref int mid, ref int midLow, ref int low, ref int dark, ref float lum, ref int valid)
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
        float GetAmbienceLightingFactor(float time, LocationSettingsClass.Location location)
        {
            if (time >= 20 && time < 24)
                return -Mathf.Clamp01((time - 20f) / 2.25f);
            else if (time >= 0 && time < 3)
                return GetBrightestNightHourFactor() - Mathf.Clamp01((2f - time) / 2f) * (GetBrightestNightHourFactor() + 1f); // 0:00 -> 100%, 1:00 -> 75%, 2:00 -> 25%, 3:00 -> 25%
            else if (time >= 3 && time < 5)
                return GetBrightestNightHourFactor() - Mathf.Clamp01((time - 3f) / 2f) * (GetBrightestNightHourFactor() + 1f); // 3:00 -> 25%, 5:00 -> 100%
            else if (time >= 5 && time < 8)
                return -Mathf.Clamp01((8f - time) / 3f); // 5:00 -> 100%, 8:00 -> 0%
            else if (time >= 7 && time < 16)
                return 1f - Mathf.Clamp01((12f - time) / 6f);
            else if (time >= 16 && time < 20)
                return Mathf.Clamp01((20f - time) / 7f);
            else return 0;

            // Maybe should be done with score profile
            float GetBrightestNightHourFactor()
            {
                switch (location.Name)
                {
                    case "Woods":
                        return -0.35f; // Moon is dimmer in Woods?
                    default:
                        return -0.25f;
                }
            }
        }
    }
}