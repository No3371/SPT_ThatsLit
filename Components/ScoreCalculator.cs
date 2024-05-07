using System;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

// === SCORE CALIBRATION TARGET EXAMPLE===
// Base on DARKEST HOUR + MAX CLOUDINESS
// CLOUDINESS 1.0 -> SCORE -0.2~0.2f (SUN) MapMinBaseAmbience~0.5 (MOON)
// CLOUDINESS -1.5 -> SCORE 0.05~0.5f (SUN) -0.3~0 (MOON)
// CLOUDINESS 0 -> SCORE 0~0.4f (SUN) -0.6~-0.3 (MOON)
// ===================

namespace ThatsLit.Components
{
    public class ScoreCalculator
    {
        readonly int RESOLUTION = 32 * ThatsLitPlugin.ResLevel.Value;
        internal float lum3s, lum1s, lum10s;
        public FrameStats frame0, frame1, frame2, frame3, frame4, frame5;
        public bool vLight, vLaser, irLight, irLaser, vLightSub, vLaserSub, irLightSub, irLaserSub;
        float scoreRawBase, scoreRaw0, scoreRaw1, scoreRaw2, scoreRaw3, scoreRaw4;
        float foliageBonusSmooth, detailBonusSmooth;
        internal static System.Diagnostics.Stopwatch _benchmarkSW;
        public ScoreCalculator()
        {
            // ThatsLitPlugin.DevMode.Value = false;
            // ThatsLitPlugin.DevMode.SettingChanged += (ev, args) =>
            // {
            //     if (ThatsLitPlugin.DevMode.Value)
            //     {
            //         ThatsLitPlugin.OverrideMaxAmbienceLum.Value = MaxAmbienceLum;
            //         ThatsLitPlugin.OverrideMinAmbienceLum.Value = MinAmbienceLum;
            //         ThatsLitPlugin.OverrideMaxBaseAmbienceScore.Value = MaxBaseAmbienceScore;
            //         ThatsLitPlugin.OverrideMinBaseAmbienceScore.Value = MinBaseAmbienceScore;
            //         ThatsLitPlugin.OverrideMaxSunLightScore.Value = MaxSunlightScore;
            //         ThatsLitPlugin.OverrideMaxMoonLightScore.Value = MaxMoonlightScore;
            //         ThatsLitPlugin.OverridePixelLumScoreScale.Value = PixelLumScoreScale;
            //         ThatsLitPlugin.OverrideThreshold0.Value = ThresholdShine;
            //         ThatsLitPlugin.OverrideThreshold1.Value = ThresholdHigh;
            //         ThatsLitPlugin.OverrideThreshold2.Value = ThresholdHighMid;
            //         ThatsLitPlugin.OverrideThreshold3.Value = ThresholdMid;
            //         ThatsLitPlugin.OverrideThreshold4.Value = ThresholdMidLow;
            //         ThatsLitPlugin.OverrideThreshold5.Value = ThresholdLow;
            //         ThatsLitPlugin.OverrideScore0.Value = ScoreShine;
            //         ThatsLitPlugin.OverrideScore1.Value = ScoreHigh;
            //         ThatsLitPlugin.OverrideScore2.Value = ScoreHighMid;
            //         ThatsLitPlugin.OverrideScore3.Value = ScoreMid;
            //         ThatsLitPlugin.OverrideScore4.Value = ScoreMidLow;
            //         ThatsLitPlugin.OverrideScore5.Value = ScoreLow;
            //         ThatsLitPlugin.OverrideScore5.Value = ScoreDark;
            //     }
            // };
        }
        public void PreCalculate (Unity.Collections.NativeArray<Color32> tex, float time)
        {
            GetThresholds(time, out float thS, out float thH, out float thHM, out float thM, out float thML, out float thL);
            StartCountPixels(tex, thS, thH, thHM, thM, thML, thL);
        }
        
        public float CalculateMultiFrameScore (Unity.Collections.NativeArray<Color32> tex, float cloud, float fog, float rain, ThatsLitMainPlayerComponent player, float time, string locationId)
        {
#region BENCHMARK
            if (ThatsLitPlugin.EnableBenchmark.Value && ThatsLitPlugin.DebugInfo.Value)
            {
                if (_benchmarkSW == null) _benchmarkSW = new System.Diagnostics.Stopwatch();
                if (_benchmarkSW.IsRunning)
                {
                    string message = $"[That's Lit] Benchmark stopwatch is not stopped! (ScoreCalc)";
                    NotificationManagerClass.DisplayWarningNotification(message);
                    Logger.LogWarning(message);
                }
                _benchmarkSW.Start();
            }
            else if (_benchmarkSW != null)
                _benchmarkSW = null;
#endregion

            float minAmbienceLum = MinAmbienceLum;
            // if (ThatsLitPlugin.DevMode.Value)
            //     minAmbienceLum = ThatsLitPlugin.OverrideMinAmbienceLum.Value;
            float maxAmbienceLum = MaxAmbienceLum;
            // if (ThatsLitPlugin.DevMode.Value)
            //     maxAmbienceLum = ThatsLitPlugin.OverrideMaxAmbienceLum.Value;
            float lumScoreScale = PixelLumScoreScale;
            // if (ThatsLitPlugin.DevMode.Value)
            //     lumScoreScale = ThatsLitPlugin.OverridePixelLumScoreScale.Value;

            frame5 = frame4;
            frame4 = frame3;
            frame3 = frame2;
            frame2 = frame1;
            frame1 = frame0;
            FrameStats thisFrame = default;
            CompleteCountPixels(out thisFrame.pxS, out thisFrame.pxH, out thisFrame.pxHM, out thisFrame.pxM, out thisFrame.pxML, out thisFrame.pxL, out thisFrame.pxD, out float lum, out float lumNonDark, out thisFrame.pixels);
            if (thisFrame.pixels == 0) thisFrame.pixels = RESOLUTION * RESOLUTION;
            thisFrame.avgLum = lum / (float)thisFrame.pixels;
            thisFrame.avgLumNonDark = lum / (float) (thisFrame.pixels - thisFrame.pxD);
            thisFrame.avgLumMultiFrames = (thisFrame.avgLum + frame1.avgLum + frame2.avgLum + frame3.avgLum + frame4.avgLum + frame5.avgLum) / 6f;
            UpdateLumTrackers(thisFrame.avgLumMultiFrames);
            
            float insideTime = Time.time - player.lastOutside;
            if (insideTime < 0) insideTime = 0;
            float outside1s = Mathf.Clamp01(1f - insideTime);

            float ambienceShadowFactor = player.AmbienceShadowFactor;

            var baseAmbienceScore = CalculateBaseAmbienceScore(locationId, time);

            baseAmbienceScore += (MinBaseAmbienceScore - baseAmbienceScore) * (player.overheadHaxRating / 10f) * 0.15f;


            // =====
            // Handle bunker environment
            // =====
            float bunkerTimeFactor = Mathf.Pow(player.bunkerTimeClamped / 10f, 2);
            // var bunkerOffset = (baseAmbienceScore - MinBaseAmbienceScore) * (0.5f * bunkerTimeFactor + 0.15f * Mathf.Clamp01((insideTime - 2f) / 3f)); 
            // baseAmbienceScore -= bunkerOffset;

            // Bunker: Bunker is overall moderately lit
            baseAmbienceScore = Mathf.Lerp(baseAmbienceScore, BunkerBaseAmbienceTarget, bunkerTimeFactor * Mathf.Clamp01(insideTime / 10) * 0.65f);
            if (Time.frameCount % 47 == 0) scoreRawBase = baseAmbienceScore;

            var ambienceScore = baseAmbienceScore;
            float insideCoef2to9s = Mathf.Clamp01((insideTime - 2) / 7f); // 0 ~ 2 sec => 0%, 9 sec => 100%
            var locCloudiness = Mathf.Lerp(cloud, 1.3f, bunkerTimeFactor); // So it's always considered "cloudy" in bunker
            ambienceScore += Mathf.Clamp01((locCloudiness - 1f) / -2f) * NonCloudinessBaseAmbienceScoreImpact; // Weather offset
            moonLightScore = CalculateMoonLight(locationId, time, locCloudiness);
            sunLightScore = CalculateSunLight(locationId, time, locCloudiness);
            ambienceScore += (moonLightScore + sunLightScore) * (1f - ambienceShadowFactor);
            
            if (player.isWinterCache && insideTime < 2f) // Debuff from winter environment when outside
            {
                ambienceScore *= 1 + Mathf.Clamp01((sunLightScore + moonLightScore - 0.1f) / 0.2f) * 0.1f;
            }

            // Strengthen darkness when around foliage and grasses
            float foliageBonus = 0;
            if (player.foliageCount > 4)
            {
                foliageBonus += moonLightScore * 0.02f * Mathf.Min(10, player.foliageCount) * Mathf.Clamp01(player.foliageScore / 1f);
                foliageBonus += sunLightScore * 0.02f * Mathf.Min(10, player.foliageCount) * Mathf.Clamp01(player.foliageScore / 1f);
            }
            else
            {
                foliageBonus += moonLightScore * 0.02f * player.foliageCount * Mathf.Clamp01(player.foliageScore / 1f);
            }

            if (foliageBonus > foliageBonusSmooth) foliageBonusSmooth = Mathf.Lerp(foliageBonusSmooth, foliageBonus, Time.fixedDeltaTime);
            else if (foliageBonus < foliageBonusSmooth) foliageBonusSmooth = Mathf.Lerp(foliageBonusSmooth, foliageBonus, 0.25f);

            if (player.recentDetailCount3x3 > 15) detailBonusSmooth = Mathf.Clamp01( + Time.fixedDeltaTime * Mathf.Clamp01((player.recentDetailCount3x3 - 15f) / 75f));
            else detailBonusSmooth = Mathf.Lerp(detailBonusSmooth, 0, 0.3f);

            foliageBonusSmooth *= player.isWinterCache? 0.2f : 1f; // Debuff from winter environment
            detailBonusSmooth *= player.isWinterCache? 0.5f : 1f; // Debuff from winter environment
            ambienceScore -= foliageBonusSmooth * moonLightScore / 2f * outside1s;
            ambienceScore -= detailBonusSmooth * moonLightScore / 2f * ambienceShadowFactor * outside1s; // Requires not lit by sun/moon
            ambienceScore = Mathf.Clamp(ambienceScore, MinBaseAmbienceScore, 1f);
            if (Time.frameCount % 47 == 0) scoreRaw0 = ambienceScore;


            //float score = CalculateTotalPixelScore(time, thisFrame.pxS, thisFrame.pxH, thisFrame.pxHM, thisFrame.pxM, thisFrame.pxML, thisFrame.pxL, thisFrame.pxD);
            //score /= (float)thisFrame.pixels;
            float lowAmbienceScoreFactor = Mathf.Max(0.5f - ambienceScore, 0) / 1.5f;
            float lowAmbienceScoreFactorSqr = lowAmbienceScoreFactor * lowAmbienceScoreFactor;
            float lumScore = (thisFrame.avgLum - minAmbienceLum) * lumScoreScale * (1+2f*lowAmbienceScoreFactorSqr) * (0.1f + lowAmbienceScoreFactor);
            float hightLightedPixelFactor = 0.9f * thisFrame.RatioShinePixels + 0.75f * thisFrame.RatioHighPixels + 0.4f * thisFrame.RatioHighMidPixels + 0.15f * thisFrame.RatioMidPixels;
            lumScore *= 1 + hightLightedPixelFactor;
            lumScore = Mathf.Clamp(lumScore, 0, 2);
            if (Time.frameCount % 47 == 0) scoreRaw1 = lumScore + ambienceScore;

            //var topScoreMultiFrames = FindHighestScoreRecentFrame(true, score);
            //var bottomScoreMultiFrames = FindLowestScoreRecentFrame(true, score);
            var topAvgLumMultiFrames = FindHighestAvgLumRecentFrame(true, thisFrame.avgLum);
            var bottomAvgLumMultiFrames = FindLowestAvgLumRecentFrame(true, thisFrame.avgLum);
            //var contrastMultiFrames = topScoreMultiFrames - bottomScoreMultiFrames; // a.k.a all sides contrast

            //if (contrastMultiFrames < 0.3f) // Low contrast, enhance darkness
            //{
            //    if (score < 0 && (thisFrame.DarkerPixels > 0.75f * thisFrame.pixels || thisFrame.RatioLowAndDarkPixels > 0.8f))
            //    {
            //        var enhacement = 2 * contrastMultiFrames * contrastMultiFrames;
            //        enhacement *= (1 - (thisFrame.BrighterPixels + thisFrame.pxM / 2) / thisFrame.pixels); // Any percentage of pixels brighter than mid scales the effect down
            //        score *= (1 + enhacement);
            //    }
            //}
            //if (lumContrastMultiFrames < 0.2f) // Low contrast, enhance darkness
            //{
            //    var enhacement = 5 * lumContrastMultiFrames * lumContrastMultiFrames;
            //    enhacement *= (1 - (thisFrame.BrighterPixels + thisFrame.pxM / 2) / thisFrame.pixels); // Any percentage of pixels brighter than mid scales the effect down
            //    lumScore -= 0.1f * (1 + enhacement);
            //}


            lumScore += CalculateChangingLumModifier(thisFrame.avgLumMultiFrames, lum1s, lum3s, ambienceScore);
            if (Time.frameCount % 47 == 0) scoreRaw2 = lumScore + ambienceScore;

            // Extra score for multi frames(sides) contrast in darkness
            // For exmaple, lighting rods on the floor contributes not much to the score but should make one much more visible
            var avgLumContrast = topAvgLumMultiFrames - bottomAvgLumMultiFrames; // a.k.a all sides contrast
            avgLumContrast -= 0.01f;
            avgLumContrast = Mathf.Clamp01(avgLumContrast);
            var compensationTarget = avgLumContrast * avgLumContrast + lowAmbienceScoreFactor * 0.5f; // 0.1 -> 0.01, 0.5 -> 0.25
            compensationTarget *= 1 + hightLightedPixelFactor * lowAmbienceScoreFactor;
            var expectedFinalScore = lumScore + ambienceScore;
            var compensation = Mathf.Clamp(compensationTarget - expectedFinalScore, 0, 2); // contrast:0.1 -> final toward 0.1, contrast:0.5 -> final toward 0.25
            lumScore += compensation * Mathf.Clamp01(avgLumContrast * 10f) * lowAmbienceScoreFactor; // amb-1 => 1f, amb-0.5 => *0.75f, amb0 => 5f (not needed)
            if (Time.frameCount % 47 == 0) scoreRaw3 = lumScore + ambienceScore;

            //The average score of other frames(sides)
            //var avgScorePrevFrames = (frame1.score + frame2.score + frame3.score + frame4.score + frame5.score) / 5f;
            //// The contrast between the brightest frame and average
            //var avgContrastFactor = score - avgScorePrevFrames; // could be up to 2
            //if (avgContrastFactor > 0) // Brighter than avg
            //{
            //    // Extra score for higher contrast (Easier to notice)
            //    avgContrastFactor /= 2f; // Compress to 0 ~ 1
            //    score += avgContrastFactor / 10f;
            //    avgContrastFactor = Mathf.Pow(1.1f * avgContrastFactor, 2); // Curve
            //    score = Mathf.Lerp(score, topScoreMultiFrames, Mathf.Clamp(avgContrastFactor, 0, 1));
            //}

            if (vLight || vLaser || vLightSub || vLaserSub)
            {
                expectedFinalScore = lumScore + ambienceScore;
                if (vLight) compensationTarget = 0.4f;
                else if (vLaser) compensationTarget = 0.2f;
                else if (vLightSub) compensationTarget = 0f;
                else if (vLaserSub) compensationTarget = 0f;
                compensation = Mathf.Clamp(compensationTarget - expectedFinalScore, 0, 2);
                lumScore += compensation * (lowAmbienceScoreFactor + 0.1f);
            }
            if (Time.frameCount % 47 == 0) scoreRaw4 = lumScore + ambienceScore;


            litScoreFactor = Mathf.Pow(Mathf.Clamp(lumScore, 0, 2f) / 2f, 2); // positive
            litScoreFactor /= 1 + Mathf.Max(ambienceScore, 0);
            litScoreFactor = Mathf.Max(litScoreFactor, 0);
            lumScore -= lumScore * 0.25f * Mathf.Clamp01(ambienceScore + 0.05f); // When ambience is already above -0.05, reduce lumScore contribution
            lumScore += ambienceScore;
            lumScore = Mathf.Clamp(lumScore, -1, 1);

            thisFrame.score = lumScore;
            thisFrame.ambienceScore = ambienceScore;
            thisFrame.baseAmbienceScore = baseAmbienceScore;

            var topScoreMultiFrames = FindHighestScoreRecentFrame(true, lumScore);
            var bottomScoreMultiFrames = FindLowestScoreRecentFrame(true, lumScore);
            thisFrame.multiFrameLitScore = (topScoreMultiFrames * 2f
                                + thisFrame.score
                                + frame1.score
                                + frame2.score
                                + frame3.score
                                + frame4.score
                                + frame5.score
                                - bottomScoreMultiFrames * 2) / 6f;

            frame0 = thisFrame;

#region BENCHMARK
            _benchmarkSW?.Stop();
#endregion
            return thisFrame.multiFrameLitScore;

        }

        /// <summary>
        /// The percentage of score provided by observed brightness (from 3d lightings)
        /// </summary>
        internal float litScoreFactor;
        float shinePixelsRatioSample, highLightPixelsRatioSample, highMidLightPixelsRatioSample, midLightPixelsRatioSample, midLowLightPixelsRatioSample, lowLightPixelsRatioSample, darkPixelsRatioSample;
        float sunLightScore, moonLightScore;
        string infoCache;

        internal virtual void CalledOnGUI (bool layout = false)
        {
            // Utility.GUILayoutDrawAsymetricMeter((int)(baseAmbienceScore / 0.0999f));
            // Utility.GUILayoutDrawAsymetricMeter((int)(ambienceScore / 0.0999f));
            // Utility.GUILayoutDrawAsymetricMeter((int)(frame0.multiFrameLitScore / 0.0999f));
            if (Time.frameCount % 41 == 0)
            {
                shinePixelsRatioSample = (frame0.RatioShinePixels + frame1.RatioShinePixels + frame2.RatioShinePixels + frame3.RatioShinePixels + frame4.RatioShinePixels + frame5.RatioShinePixels) / 6f;
                highLightPixelsRatioSample = (frame0.RatioHighPixels + frame1.RatioHighPixels + frame2.RatioHighPixels + frame3.RatioHighPixels + frame4.RatioHighPixels + frame5.RatioHighPixels) / 6f;
                highMidLightPixelsRatioSample = (frame0.RatioHighMidPixels + frame1.RatioHighMidPixels + frame2.RatioHighMidPixels + frame3.RatioHighMidPixels + frame4.RatioHighMidPixels + frame5.RatioHighMidPixels) / 6f;
                midLightPixelsRatioSample = (frame0.RatioMidPixels + frame1.RatioMidPixels + frame2.RatioMidPixels + frame3.RatioMidPixels + frame4.RatioMidPixels + frame5.RatioMidPixels) / 6f;
                midLowLightPixelsRatioSample = (frame0.RatioMidLowPixels + frame1.RatioMidLowPixels + frame2.RatioMidLowPixels + frame3.RatioMidLowPixels + frame4.RatioMidLowPixels + frame5.RatioMidLowPixels) / 6f;
                lowLightPixelsRatioSample = (frame0.RatioLowPixels + frame1.RatioLowPixels + frame2.RatioLowPixels + frame3.RatioLowPixels + frame4.RatioLowPixels + frame5.RatioLowPixels) / 6f;
                darkPixelsRatioSample = (frame0.RatioDarkPixels + frame1.RatioDarkPixels + frame2.RatioDarkPixels + frame3.RatioDarkPixels + frame4.RatioDarkPixels + frame5.RatioDarkPixels) / 6f;
            }
            if (layout) infoCache = $"  PIXELS: {shinePixelsRatioSample * 100:000}% - {highLightPixelsRatioSample * 100:000}% - {highMidLightPixelsRatioSample * 100:000}% - { midLightPixelsRatioSample * 100:000}% - {midLowLightPixelsRatioSample * 100:000}% - {lowLightPixelsRatioSample * 100:000}% | {darkPixelsRatioSample * 100:000}% (AVG Sample)\n  AvgLumMF: {frame0.avgLumMultiFrames:0.000} / {GetMinAmbianceLum():0.000} ~ {GetMaxAmbianceLum():0.000} ({GetAmbianceLumRange():0.000})\n   Sun: {sunLightScore:0.000}/{GetMaxSunlightScore():0.000}, Moon: {moonLightScore:0.000}/{GetMaxMoonlightScore():0.000}\n  SCORE : {scoreRawBase:＋0.00;－0.00;+0.00} -> {scoreRaw0:＋0.00;－0.00;+0.00} -> {scoreRaw1:＋0.00;－0.00;+0.00} -> {scoreRaw2:＋0.00;－0.00;+0.00} -> {scoreRaw3:＋0.00;－0.00;+0.00} -> {scoreRaw4:＋0.00;－0.00;+0.00} (SAMPLE)";            
            GUILayout.Label(infoCache);
            // GUILayout.Label(string.Format("  PIXELS: {0:000}% - {1:000}% - {2:000}% - {3:000}% - {4:000}% - {5:000}% | {6:000}% (AVG Sample)", shinePixelsRatioSample * 100, highLightPixelsRatioSample * 100, highMidLightPixelsRatioSample * 100, midLightPixelsRatioSample * 100, midLowLightPixelsRatioSample * 100, lowLightPixelsRatioSample * 100, darkPixelsRatioSample * 100));
            // GUILayout.Label(string.Format("  AvgLumMF: {0:0.000} / {1:0.000} ~ {2:0.000} ({3:0.000})", frame0.avgLumMultiFrames, GetMinAmbianceLum(), GetMaxAmbianceLum(), GetAmbianceLumRange()));
            // GUILayout.Label(string.Format("  Sun: {0:0.000}/{1:0.000}, Moon: {2:0.000}/{3:0.000}", sunLightScore, GetMaxSunlightScore(), moonLightScore, GetMaxMoonlightScore()));
            // GUILayout.Label(string.Format("  SCORE : {0:＋0.00;－0.00;+0.00} -> {1:＋0.00;－0.00;+0.00} -> {2:＋0.00;－0.00;+0.00} -> {3:＋0.00;－0.00;+0.00} (SAMPLE)", scoreRaw1, scoreRaw2, scoreRaw3, scoreRaw4));
            
            Utility.GUILayoutDrawAsymetricMeter((int)(frame0.score / 0.0999f));
        }

        protected virtual float FinalTransformScore (float score)
        {
            return score;
        }

        protected virtual float CalculateChangingLumModifier(float avgLumMultiFrames, float lum1s, float lum3s, float ambienceScore)
        {
            var recentChange = Mathf.Clamp(Mathf.Abs(avgLumMultiFrames - lum1s), 0, 0.05f) * 10f * (Mathf.Clamp01(-ambienceScore) + 0.2f); // When ambience score is -1 ~ 0
            recentChange += Mathf.Clamp(Mathf.Abs(avgLumMultiFrames - lum3s), 0, 0.025f) * 3f * (Mathf.Clamp01(-ambienceScore) + 0.1f); // When ambience score is -1 ~ 0
            return recentChange;
        }

        protected virtual float CalculateStaticLumModifier(float score, float avgLumMultiFrames, float envLum, float envLumSlow)
        {
            var recentChangeFactor = Mathf.Clamp01(Mathf.Abs(avgLumMultiFrames - lum3s) / 0.2f); // (avgLumMultiFrames - envLumEstiSlow) is always approaching zero when the environment lighting is stable
            if (score > 0f) score /= 1 + 0.3f * (1 - recentChangeFactor); // The bigger the difference, the more it should be suppressed
            else if (score < 0f) score *= 1 + 0.1f * (1 - recentChangeFactor);

            recentChangeFactor = Mathf.Clamp01(Mathf.Abs(avgLumMultiFrames - lum3s) / 0.2f); // (avgLumMultiFrames - envLumEstiSlow) is always approaching zero when the environment lighting is stable
            if (score > 0f) score /= 1 + 0.1f * (1 - recentChangeFactor); // The bigger the difference, the more it should be suppressed
            else if (score < 0f) score *= 1 + 0.1f * (1 - recentChangeFactor);
            return score;
        }

        float FindHighestAvgLumRecentFrame(bool includeThis, float thisframe)
        {
            float avgLum = includeThis ? thisframe : frame1.avgLum;
            if (frame1.avgLum > avgLum) avgLum = frame1.avgLum;
            if (frame2.avgLum > avgLum) avgLum = frame2.avgLum;
            if (frame3.avgLum > avgLum) avgLum = frame3.avgLum;
            if (frame4.avgLum > avgLum) avgLum = frame4.avgLum;
            if (frame5.avgLum > avgLum) avgLum = frame5.avgLum;
            return avgLum;
        }

        float FindLowestAvgLumRecentFrame(bool includeThis, float calculating)
        {
            float avgLum = includeThis ? calculating : frame1.avgLum;
            if (frame1.avgLum < avgLum) avgLum = frame1.avgLum;
            if (frame2.avgLum < avgLum) avgLum = frame2.avgLum;
            if (frame3.avgLum < avgLum) avgLum = frame3.avgLum;
            if (frame4.avgLum < avgLum) avgLum = frame4.avgLum;
            if (frame5.avgLum < avgLum) avgLum = frame5.avgLum;
            return avgLum;
        }

        float FindHighestMFAvgLumRecentFrame(bool includeThis, float thisframe)
        {
            float mfAvgLum = includeThis ? thisframe : frame1.avgLumMultiFrames;
            if (frame1.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame1.avgLumMultiFrames;
            if (frame2.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame2.avgLumMultiFrames;
            if (frame3.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame3.avgLumMultiFrames;
            if (frame4.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame4.avgLumMultiFrames;
            if (frame5.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame5.avgLumMultiFrames;
            return mfAvgLum;
        }

        float FindLowestMFAvgLumRecentFrame(bool includeThis, float calculating)
        {
            float mfAvgLum = includeThis ? calculating : frame1.avgLumMultiFrames;
            if (frame1.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame1.avgLumMultiFrames;
            if (frame2.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame2.avgLumMultiFrames;
            if (frame3.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame3.avgLumMultiFrames;
            if (frame4.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame4.avgLumMultiFrames;
            if (frame5.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame5.avgLumMultiFrames;
            return mfAvgLum;
        }

        float FindHighestScoreRecentFrame (bool includeThis, float calculating)
        {
            float score = includeThis ? calculating : frame1.score;
            if (frame1.score > score) score = frame1.score;
            if (frame2.score > score) score = frame2.score;
            if (frame3.score > score) score = frame3.score;
            if (frame4.score > score) score = frame4.score;
            if (frame5.score > score) score = frame5.score;
            return score;
        }

        float FindLowestScoreRecentFrame(bool includeThis, float calculating)
        {
            float score = includeThis ? calculating : frame1.score;
            if (frame1.score < score) score = frame1.score;
            if (frame2.score < score) score = frame2.score;
            if (frame3.score < score) score = frame3.score;
            if (frame4.score < score) score = frame4.score;
            if (frame5.score < score) score = frame5.score;
            return score;
        }

        protected virtual void UpdateLumTrackers (float avgLumMultiFrames)
        {
            lum1s = Mathf.Lerp(lum1s, avgLumMultiFrames, Time.deltaTime);
            lum3s = Mathf.Lerp(lum3s, avgLumMultiFrames, Time.deltaTime / 3f);
            lum10s = Mathf.Lerp(lum10s, avgLumMultiFrames, Time.deltaTime / 10f);
        }
        protected virtual void GetThresholds(float tlf, out float thresholdShine, out float thresholdHigh, out float thresholdHighMid, out float thresholdMid, out float thresholdMidLow, out float thresholdLow)
        {
            // if (ThatsLitPlugin.DevMode.Value)
            // {
            //     thresholdShine = ThatsLitPlugin.OverrideThreshold0.Value;
            //     thresholdHigh = ThatsLitPlugin.OverrideThreshold1.Value;
            //     thresholdHighMid = ThatsLitPlugin.OverrideThreshold2.Value;
            //     thresholdMid = ThatsLitPlugin.OverrideThreshold3.Value;
            //     thresholdMidLow = ThatsLitPlugin.OverrideThreshold4.Value;
            //     thresholdLow = ThatsLitPlugin.OverrideThreshold5.Value;
            //     return;
            // }
            thresholdShine = 0.64f;
            thresholdHigh = 0.32f;
            thresholdHighMid = 0.16f;
            thresholdMid = 0.08f;
            thresholdMidLow = 0.04f;
            thresholdLow = 0.02f;
        }
        protected virtual void GetPixelScores(float tlf, out float scoreShine, out float scoreHigh, out float scoreHighMid, out float scoreMid, out float scoreMidLow, out float scoreLow, out float scoreDark)
        {
            // if (ThatsLitPlugin.DevMode.Value)
            // {
            //     scoreShine = ThatsLitPlugin.OverrideScore0.Value;
            //     scoreHigh = ThatsLitPlugin.OverrideScore1.Value;
            //     scoreHighMid = ThatsLitPlugin.OverrideScore2.Value;
            //     scoreMid = ThatsLitPlugin.OverrideScore3.Value;
            //     scoreMidLow = ThatsLitPlugin.OverrideScore4.Value;
            //     scoreLow = ThatsLitPlugin.OverrideScore5.Value;
            //     scoreDark = ThatsLitPlugin.OverrideScore6.Value;
            //     return;
            // }
            scoreShine = ScoreShine;
            scoreHigh = ScoreHigh;
            scoreHighMid = ScoreHighMid;
            scoreMid = ScoreMid;
            scoreMidLow = ScoreMidLow;
            scoreLow = ScoreLow;
            scoreDark = ScoreDark;
        }

        protected virtual float CalculateTotalPixelScore (float time, int pxS, int pxH, int pxHM, int pxM, int pxML, int pxL, int pxD)
        {
            GetPixelScores(time, out float sS, out float sH, out float sHM, out float sM, out float sML, out float sL, out float sD);
            return (pxS * sS
                 + pxH * sH
                 + pxHM * sHM
                 + pxM * sM
                 + pxML * sML
                 + pxL * sL
                 + pxD * sD);
        }

        struct CountPixelsJob : IJobParallelFor
        {
            [Unity.Collections.LowLevel.Unsafe.NativeSetThreadIndex]
            public int threadIndex;
            [ReadOnly]
            public NativeArray<Color32> tex;
            [WriteOnly]
            [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction]
            public NativeArray<int> counted; // pxS, pxH, pxHM, pxM, pxML, pxL, pxD, valid
            [WriteOnly]
            [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction]
            public NativeArray<float> lum; // lum, lumNonDark
            [ReadOnly]
            public NativeArray<float> thresholds; // thresholdShine, thresholdHigh, thresholdHighMid, thresholdMid, thresholdMidLow, thresholdLow

            public void Execute(int index)
            {
                Color32 c = tex[index];
                if (c == Color.white || c.a <= 0.5f)
                    return;

                var pxLum = (c.r + c.g + c.b) / 765f;

                int threadIndexOffset = threadIndex * 8;
                lum[threadIndexOffset + 0] += pxLum;
                if (pxLum < thresholds[5])
                {
                    counted[threadIndexOffset + 6] += 1;
                    lum[threadIndexOffset + 1] += pxLum;
                }
                else if (pxLum >= thresholds[0]) counted[threadIndexOffset + 0] += 1;
                else if (pxLum >= thresholds[1]) counted[threadIndexOffset + 1] += 1;
                else if (pxLum >= thresholds[2]) counted[threadIndexOffset + 2] += 1;
                else if (pxLum >= thresholds[3]) counted[threadIndexOffset + 3] += 1;
                else if (pxLum >= thresholds[4]) counted[threadIndexOffset + 4] += 1;
                else if (pxLum >= thresholds[5]) counted[threadIndexOffset + 5] += 1;

                counted[threadIndexOffset + 7] += 1;
            }
        }
        CountPixelsJob countingJob;
        JobHandle countingJobHandle;

        protected void StartCountPixels(Unity.Collections.NativeArray<Color32> tex, float thresholdShine, float thresholdHigh, float thresholdHighMid, float thresholdMid, float thresholdMidLow, float thresholdLow)
        {
            if (!tex.IsCreated) return;

            NativeArray<float> thresholds = new NativeArray<float>(6, Allocator.TempJob);
            thresholds[0] = thresholdShine;
            thresholds[1] = thresholdHigh;
            thresholds[2] = thresholdHighMid;
            thresholds[3] = thresholdMid;
            thresholds[4] = thresholdMidLow;
            thresholds[5] = thresholdLow;

            countingJob = new CountPixelsJob()
            {
                thresholds = thresholds,
                tex = tex,
                counted = new NativeArray<int>(8 * JobsUtility.MaxJobThreadCount , Allocator.TempJob, NativeArrayOptions.ClearMemory),
                lum = new NativeArray<float>(2 * JobsUtility.MaxJobThreadCount , Allocator.TempJob, NativeArrayOptions.ClearMemory)
            };
            countingJobHandle = countingJob.Schedule(tex.Length, tex.Length / 64);
            // Logger.LogInfo(string.Format("F{0} Counting {1} px in batches of {2}", Time.frameCount, tex.Length, tex.Length / 64));
        }
        protected void CompleteCountPixels(out int shine, out int high, out int highMid, out int mid, out int midLow, out int low, out int dark, out float lum, out float lumNonDark, out int valid)
        {
            shine = high = highMid = mid = midLow = low = dark = valid = 0;
            lum = 0;
            lumNonDark = 0;

            countingJobHandle.Complete();

            countingJob.thresholds.Dispose();
            
            if (countingJob.lum.IsCreated) for (int i = 0; i < countingJob.lum.Length; i += 2)
            {
                lum += countingJob.lum[i];
                lumNonDark += countingJob.lum[i + 1];
            }
            countingJob.lum.Dispose();
            if (countingJob.counted.IsCreated)
            for (int i = 0; i < countingJob.counted.Length; i += 8)
            {
                if (countingJob.counted[i+7] == 0) continue;
                shine += countingJob.counted[i];
                high += countingJob.counted[i+1];
                highMid += countingJob.counted[i+2];
                mid += countingJob.counted[i+3];
                midLow += countingJob.counted[i+4];
                low += countingJob.counted[i+5];
                dark += countingJob.counted[i+6];
                valid += countingJob.counted[i+7];
                // Logger.LogInfo(string.Format("F{0} #{9}---{1} {2} {3} {4} {5} {6} {7} {8}",
                                // Time.frameCount, countingJob.counted[i], countingJob.counted[i+1], countingJob.counted[i+2], countingJob.counted[i+3],
                                // countingJob.counted[i+4], countingJob.counted[i+5], countingJob.counted[i+6], countingJob.counted[i+7], i/8));
            }
            countingJob.counted.Dispose();
            // Logger.LogInfo(string.Format("F{0} Counted {1} px", Time.frameCount, valid));

        }

        protected virtual float CalculateBaseAmbienceScore(string locationId, float time)
        {
            return Mathf.Lerp(GetMinBaseAmbienceLitScore(locationId, time), GetMaxBaseAmbienceLitScore(locationId, time), GetMapAmbienceCoef(locationId, time));
        }

        // The visual brightness during the darkest hours with cloudiness 1... This is the base brightness of the map without any interference (e.g. sun/moon light)
        protected virtual float GetMinBaseAmbienceLitScore (string locationId, float time)
        {
            // if (ThatsLitPlugin.DevMode.Value)
            //     return ThatsLitPlugin.OverrideMinBaseAmbienceScore.Value;
            return MinBaseAmbienceScore;
        }
        // The visual brightness during the brightest hours with cloudiness 1... This is the base brightness of the map without any interference (e.g. sun/moon light)
        protected virtual float GetMaxBaseAmbienceLitScore(string locationId, float time)
        {
            // if (ThatsLitPlugin.DevMode.Value)
            //     return ThatsLitPlugin.OverrideMaxBaseAmbienceScore.Value;
            return MaxBaseAmbienceScore;
        }
        protected virtual float GetMapAmbienceCoef(string locationId, float time)
        {
            if (time >= 5 && time < 7.5f) // 0 ~ 0.5f
                return 0.5f * GetTimeProgress(time, 5, 7.5f);
            else if (time >= 7.5f && time < 12f) // 0.5f ~ 1
                return 0.5f + 0.5f * GetTimeProgress(time, 7.5f, 12f);
            else if (time >= 12 && time < 15) // 1 ~ 1
                return 1;
            else if (time >= 15 && time < 18) // 1 ~ 0.8f
                return 1f - 0.2f * GetTimeProgress(time, 18, 20);
            else if (time >= 18 && time < 20) // 0.8f ~ 0.3f
                return 0.8f - 0.8f * GetTimeProgress(time, 18, 20);
            else if (time >= 20 && time < 21.5f) // 0.3 ~ 0
                return 0.3f - 0.3f * GetTimeProgress(time, 20, 21.5f);
            else if (time >= 22 && time < 24) // 0 ~ 0.1
                return 0.1f * GetTimeProgress(time, 22, 24);
            else if (time >= 0 && time < 3) // 0 ~ 0.1
                return 0.1f;
            else if (time >= 3 && time < 5) // 0.1 ~ 0
                return 0.1f - 0.1f * GetTimeProgress(time, 3, 5);
            else return 0;
        }

        // Fog determine visual brightness of further envrionment, unused
        // The increased visual brightness when moon is up (0~5) when c < 1
        // cloudiness blocks moon light
        protected virtual float CalculateMoonLight(string locationId, float time, float cloudiness)
        {
            cloudiness = 1 - cloudiness; // difference from 1
            if (cloudiness > 1) cloudiness = Mathf.Lerp(cloudiness, 1, 0.25f); // Balancing days of -1.5 cloudiness, which seems more common in 3.7+
            float maxMoonlightScore = GetMaxMoonlightScore();
            return cloudiness * maxMoonlightScore * CalculateMoonLightTimeFactor(locationId, time);
        }

        // The increased visual brightness when sun is up (5~22) hours when c < 1
        // cloudiness blocks sun light
        protected virtual float CalculateSunLight(string locationId, float time, float cloudiness)
        {
            cloudiness = 1 - cloudiness; // difference from 1
            if (cloudiness > 2f) cloudiness = Mathf.Lerp(cloudiness, 2, 0.5f); // Balancing days of -1.5 cloudiness, which seems more common in 3.7+
            float maxSunlightScore = GetMaxSunlightScore();
            return cloudiness * maxSunlightScore * CalculateSunLightTimeFactor(locationId, time);
        }

        internal virtual float CalculateSunLightTimeFactor(string locationId, float time)
        {
            if (time >= 5 && time < 6) // 0 ~ 0.1
                return GetTimeProgress(time, 5, 6) * 0.1f;
            else if (time >= 6 && time < 8) // 0.1 ~ 0.3
                return 0.1f + GetTimeProgress(time, 6, 8) * 0.2f;
            else if (time >= 8 && time < 12) // 0.3 ~ 1
                return 0.3f + GetTimeProgress(time, 8, 12) * 0.7f;
            else if (time >= 12 && time < 15) // 1 ~ 1
                return 1;
            else if (time >= 15 && time < 19) // 1 ~ 0.5f
                return 1f - GetTimeProgress(time, 15, 19) * 0.5f;
            else if (time >= 19 && time < 21.5f) // 0.5 ~ 0f
                return 0.5f - GetTimeProgress(time, 19, 21.5f) * 0.5f;
            else return 0;
        }

        protected virtual float CalculateMoonLightTimeFactor(string locationId, float time)
        {
            if (time > 0 && time < 3.5f) // 0 ~ 1
                return Mathf.Clamp01(time / 2f);
            else if (time >= 3.5f && time < 5) // 1 ~ 0
                return (1f - Mathf.Clamp01((time - 3.5f) / 1.5f));
            else return 0;
        }

        protected virtual float GetTimeProgress (float now, float from, float to)
        {
            return Mathf.Clamp01((now - from) / (to - from));
        }
        protected virtual float GetMinAmbianceLum()
        {
            float minAmbienceLum = MinAmbienceLum;
            // if (ThatsLitPlugin.DevMode.Value)
            //     minAmbienceLum = ThatsLitPlugin.OverrideMinAmbienceLum.Value;
            return minAmbienceLum;
        }
        protected virtual float GetMaxAmbianceLum()
        {
            float maxAmbienceLum = MaxAmbienceLum;
            // if (ThatsLitPlugin.DevMode.Value)
            //     maxAmbienceLum = ThatsLitPlugin.OverrideMaxAmbienceLum.Value;
            return maxAmbienceLum;
        }
        protected virtual float GetAmbianceLumRange()
        {
            return GetMaxAmbianceLum() + 0.001f - GetMinAmbianceLum();
        }
        protected virtual float GetMaxSunlightScore()
        {
            float maxSunlightScore = MaxSunlightScore;
            // if (ThatsLitPlugin.DevMode.Value)
            //     maxSunlightScore = ThatsLitPlugin.OverrideMaxSunLightScore.Value;
            return maxSunlightScore;
        }
        protected virtual float GetMaxMoonlightScore()
        {
            float maxMoonlightScore = MaxMoonlightScore;
            // if (ThatsLitPlugin.DevMode.Value)
            //     maxMoonlightScore = ThatsLitPlugin.OverrideMaxMoonLightScore.Value;
            return maxMoonlightScore;
        }
        protected virtual float MinBaseAmbienceScore { get => -0.9f; }
        protected virtual float MaxBaseAmbienceScore { get => 0.0f; }
        /// <summary>
        /// The ambience change between c-1 and c1 during the darkest hours
        /// </summary>
        /// <value></value>
        protected virtual float NonCloudinessBaseAmbienceScoreImpact { get => 0.1f; }
        protected virtual float BunkerBaseAmbienceTarget { get => -0.3f; }
        protected virtual float MaxMoonlightScore { get => 0.3f; }
        protected virtual float MaxSunlightScore { get => 0.25f; }
        protected virtual float IndoorAmbienceScale { get => 0.5f; }
        protected virtual float MinAmbienceLum { get => 0.01f; }
        protected virtual float MaxAmbienceLum { get => 0.1f; }
        protected virtual float PixelLumScoreScale => 1f;
        protected virtual float ThresholdShine { get => 0.8f; }
        protected virtual float ThresholdHigh { get => 0.5f; }
        protected virtual float ThresholdHighMid { get => 0.25f; }
        protected virtual float ThresholdMid { get => 0.13f; }
        protected virtual float ThresholdMidLow { get => 0.06f; }
        protected virtual float ThresholdLow { get => 0.02f; }
        protected virtual float ScoreShine { get => 0.2f; }
        protected virtual float ScoreHigh { get => 0.2f; }
        protected virtual float ScoreHighMid { get => 0.25f; }
        protected virtual float ScoreMid { get => 0.2f; }
        protected virtual float ScoreMidLow { get => 0.1f; }
        protected virtual float ScoreLow { get => 0.05f; }
        protected virtual float ScoreDark { get => 0; }
        protected virtual float AvgLumContrastOffset { get => -0.05f; }
    }
    public class HideoutScoreCalculator : ScoreCalculator
    {
        protected override float MinBaseAmbienceScore => -0.65f;
        protected override float MaxBaseAmbienceScore { get => -0.65f; }
        protected override float MaxMoonlightScore => 0;
        protected override float MaxSunlightScore => 0;
        protected override float MinAmbienceLum { get => 0.066f; }
        protected override float MaxAmbienceLum { get => 0.07f; }
    }
    public class ReserveScoreCalculator : ScoreCalculator
    {
        protected override float MinBaseAmbienceScore => -0.88f;
        protected override float MaxBaseAmbienceScore => -0.05f;
        protected override float MinAmbienceLum => 0.011f;
        protected override float MaxAmbienceLum => 0.011f;
        protected override float ThresholdShine { get => 0.4f; }
        protected override float ThresholdHigh { get => 0.3f; }
        protected override float ThresholdHighMid { get => 0.2f; }
        protected override float ThresholdMid { get => 0.1f; }
        protected override float ThresholdMidLow { get => 0.04f; }
        protected override float ThresholdLow { get => 0.015f; }
        protected override float PixelLumScoreScale { get => 2f; }
    }

    public class WoodsScoreCalculator : ScoreCalculator
    {
        protected override float MinBaseAmbienceScore => -0.9f;
        protected override float MinAmbienceLum => 0.015f;
        protected override float MaxAmbienceLum => 0.017f;
        protected override float ThresholdShine { get => 0.2f; }
        protected override float ThresholdHigh { get => 0.1f; }
        protected override float ThresholdHighMid { get => 0.05f; }
        protected override float ThresholdMid { get => 0.02f; }
        protected override float ThresholdMidLow { get => 0.01f; }
        protected override float ThresholdLow { get => 0.005f; }
        protected override float GetMapAmbienceCoef(string locationId, float time)
        {
            if (time >= 5 && time < 7.5f) // 0 ~ 0.5f
                return 0.5f * GetTimeProgress(time, 5, 7.5f);
            else if (time >= 7.5f && time < 12f) // 0.5f ~ 1
                return 0.5f + 0.5f * GetTimeProgress(time, 7.5f, 12);
            else if (time >= 12 && time < 15) // 1 ~ 1
                return 1;
            else if (time >= 15 && time < 18) // 1 ~ 0.8f
                return 1f - 0.2f * GetTimeProgress(time, 18, 20);
            else if (time >= 18 && time < 20f) // 1 ~ 0.35
                return 0.8f - 0.45f * GetTimeProgress(time, 18, 20f);
            else if (time >= 20 && time < 21.5f)
                return 0.35f - 0.35f * GetTimeProgress(time, 20, 21.5f);
            else if (time >= 22 && time < 24) // 0 ~ 0.1
                return 0.1f * GetTimeProgress(time, 22, 24);
            else if (time >= 0 && time < 3) // 0 ~ 0.1
                return 0.1f;
            else if (time >= 3 && time < 5) // 0.1 ~ 0
                return 0.1f - 0.1f * GetTimeProgress(time, 3, 5);
            else return 0;
        }
    }

    public class LighthouseScoreCalculator : ScoreCalculator
    {
        protected override float MinBaseAmbienceScore => -0.88f;
        protected override float NonCloudinessBaseAmbienceScoreImpact => 0.05f;
        protected override float PixelLumScoreScale { get => 2.5f; }
        protected override float ThresholdShine { get => 0.4f; }
        protected override float ThresholdHigh { get => 0.3f; }
        protected override float ThresholdHighMid { get => 0.2f; }
        protected override float ThresholdMid { get => 0.1f; }
        protected override float ThresholdMidLow { get => 0.04f; }
        protected override float ThresholdLow { get => 0.015f; }

        internal override float CalculateSunLightTimeFactor(string locationId, float time)
        {
            if (time >= 15 && time < 20) // 1 ~ 0.5f
                return 1f - 0.3f * GetTimeProgress(time, 15, 20);
            else if (time >= 20 && time < 21.5f) // 0.5 ~ 0f
                return 0.7f * (1f - GetTimeProgress(time, 20, 21.5f));
            else return base.CalculateSunLightTimeFactor(locationId, time);
        }
    }
    public class CustomsScoreCalculator : ScoreCalculator
    {
        protected override float MinBaseAmbienceScore => -0.7f;
        protected override float NonCloudinessBaseAmbienceScoreImpact { get => 0.15f; }
        protected override float MaxMoonlightScore => 0.2f;
        protected override float PixelLumScoreScale { get => 2.2f; }
        protected override float BunkerBaseAmbienceTarget { get => -0.45f; }
    }
    public class InterchangeScoreCalculator : ScoreCalculator
    {
        protected override float MinBaseAmbienceScore => -0.8f;
        protected override float MaxBaseAmbienceScore { get => -0.1f; } // indoor
        protected override float MaxMoonlightScore => base.MaxMoonlightScore * 0.75f;
        protected override float MinAmbienceLum => 0.008f;
        protected override float MaxAmbienceLum => 0.008f;
        protected override float PixelLumScoreScale { get => 2f; }
        protected override float IndoorAmbienceScale => 0.2f;

        // Characters in the basement floor gets lit up by ambience lighting
        protected override float CalculateBaseAmbienceScore(string locationId, float time)
        {
            ThatsLitMainPlayerComponent p = Singleton<ThatsLitMainPlayerComponent>.Instance;
            if (Physics.Raycast(new Ray(p.MainPlayer.MainParts[BodyPartType.head].Position, Vector3.up), out var hit, 5, LayerMaskClass.LowPolyColliderLayerMask))
            {
                if (hit.transform.gameObject.scene.name == "Shopping_Mall_parking_work" && p.transform.position.y < 23.5f)
                {
                    return Mathf.Lerp(base.CalculateBaseAmbienceScore(locationId, time), -0.5f , p.overheadHaxRating * 0.5f);
                }
            }

            return base.CalculateBaseAmbienceScore(locationId, time);
        }

        protected override float CalculateMoonLightTimeFactor(string locationId, float time)
        {
            if (time > 23.9 && time < 0) // 0 ~ 1
                return 0.1f * (24f - time);
            else if (time >= 0 && time < 3.5f) // 0 ~ 1
                return 0.1f + 0.9f * GetTimeProgress(time, 0, 2f);
            else if (time >= 3.5f && time < 4.33) // 1 ~ 0
                return 1f - 0.6f * GetTimeProgress(time, 3.5f, 4.33f); 
            else if (time >= 4.33f && time < 4.416f) // 1 ~ 0
                return 0.4f - 0.2f * GetTimeProgress(time,  4.33f, 4.416f); // How interesting, the moon lose its brightness in 5 mins
            else if (time >= 4.416f && time < 5) // 1 ~ 0
                return 0.2f - GetTimeProgress(time, 4.416f, 5f);
            else return 0;
        }
    }
    public class ShorelineScoreCalculator : ScoreCalculator
    {
        protected override float MinBaseAmbienceScore => -0.9f;
        protected override float MaxMoonlightScore => base.MaxMoonlightScore * 0.5f;
        protected override float MinAmbienceLum => 0.008f;
        protected override float MaxAmbienceLum => 0.008f;
        protected override float ThresholdShine { get => 0.5f; }
        protected override float ThresholdHigh { get => 0.35f; }
        protected override float ThresholdHighMid { get => 0.2f; }
        protected override float ThresholdMid { get => 0.1f; }
        protected override float ThresholdMidLow { get => 0.025f; }
        protected override float NonCloudinessBaseAmbienceScoreImpact => 0.1f;
        internal override float CalculateSunLightTimeFactor(string locationId, float time)
        {
            if (time >= 5.5 && time < 6.5) // 0 ~ 0.1
                return GetTimeProgress(time, 5, 6) * 0.1f;
            else if (time >= 6.5 && time < 7.5) // 0.1 ~ 0.3
                return 0.1f + GetTimeProgress(time, 6, 8) * 0.2f;
            else if (time >= 7.5 && time < 12) // 0.3 ~ 1
                return 0.3f + GetTimeProgress(time, 8, 12) * 0.7f;
            else if (time >= 12 && time < 15) // 1 ~ 1
                return 1;
            else if (time >= 15 && time < 19) // 1 ~ 0.5f
                return 1f - GetTimeProgress(time, 15, 19) * 0.5f;
            else if (time >= 19 && time < 21.5f) // 0.5 ~ 0f
                return 0.5f - GetTimeProgress(time, 19, 21.5f) * 0.5f;
            else return 0;
        }
    }

    public class StreetsScoreCalculator : ScoreCalculator
    {
        protected override float MinBaseAmbienceScore => -0.75f;
        protected override float MaxSunlightScore { get => 0.05f; }
        protected override float MaxMoonlightScore { get => 0.1f; }
        protected override float MinAmbienceLum { get => 0.011f; }
        protected override float MaxAmbienceLum { get => 0.111f; }
        protected override float ThresholdShine { get => 0.2f; }
        protected override float ThresholdHigh { get => 0.1f; }
        protected override float ThresholdHighMid { get => 0.05f; }
        protected override float ThresholdMid { get => 0.02f; }
        protected override float ThresholdMidLow { get => 0.01f; }
        protected override float ThresholdLow { get => 0.005f; }
        protected override float PixelLumScoreScale { get => 1.5f; }

        protected override float GetMapAmbienceCoef(string locationId, float time)
        {
            float result;

            if (time >= 6 && time < 7.5f) // 0 ~ 0.35f
                result = 0.35f * GetTimeProgress(time, 6, 7.5f);
            else if (time >= 7.5f && time < 12f) // 0.5f ~ 1
                result = 0.5f + 0.5f * GetTimeProgress(time, 7.5f, 12);
            else if (time >= 12 && time < 15) // 1 ~ 1
                return 1;
            else if (time >= 15 && time < 18) // 1 ~ 0.8f
                return 1f - 0.2f * GetTimeProgress(time, 18, 20);
            else if (time >= 18 && time < 20) // 1 ~ 0.3f
                result = 0.8f - 0.4f * GetTimeProgress(time, 18, 20);
            else if (time >= 20 && time < 21.5f) // 0.3 ~ 0
                result = 0.3f - 0.3f * GetTimeProgress(time, 20, 21.5f);
            else if (time >= 22 && time < 24) // 0 ~ 0.1
                return 0.1f * GetTimeProgress(time, 22, 24);
            else if (time >= 0 && time < 3) // 0 ~ 0.1
                return 0.1f;
            else if (time >= 3 && time < 5) // 0.1 ~ 0
                result = 0.1f * GetTimeProgress(time, 3, 5);
            else result = 0;
            return result;
        }
    }

    public class GroundZeroScoreCalculator : ScoreCalculator
    {
        protected override float MinBaseAmbienceScore => -0.75f;
        protected override float MaxBaseAmbienceScore => -0.1f;
        protected override float MaxSunlightScore { get => 0.25f; }
        protected override float MaxMoonlightScore { get => 0.1f; }
        protected override float MinAmbienceLum { get => 0.011f; }
        protected override float MaxAmbienceLum { get => 0.111f; }
        protected override float ThresholdShine { get => 0.2f; }
        protected override float ThresholdHigh { get => 0.1f; }
        protected override float ThresholdHighMid { get => 0.05f; }
        protected override float ThresholdMid { get => 0.02f; }
        protected override float ThresholdMidLow { get => 0.01f; }
        protected override float ThresholdLow { get => 0.005f; }
        protected override float PixelLumScoreScale { get => 1.6f; }

        protected override float GetMapAmbienceCoef(string locationId, float time)
        {
            float result;

            if (time >= 6 && time < 7.5f) // 0 ~ 0.35f
                result = 0.35f * GetTimeProgress(time, 6, 7.5f);
            else if (time >= 7.5f && time < 12f) // 0.5f ~ 1
                result = 0.5f + 0.5f * GetTimeProgress(time, 7.5f, 12);
            else if (time >= 12 && time < 15) // 1 ~ 1
                return 1;
            else if (time >= 15 && time < 18) // 1 ~ 0.8f
                return 1f - 0.2f * GetTimeProgress(time, 18, 20);
            else if (time >= 18 && time < 20) // 1 ~ 0.3f
                result = 0.8f - 0.4f * GetTimeProgress(time, 18, 20);
            else if (time >= 20 && time < 21.5f) // 0.3 ~ 0
                result = 0.3f - 0.3f * GetTimeProgress(time, 20, 21.5f);
            else if (time >= 22 && time < 24) // 0 ~ 0.1
                return 0.1f * GetTimeProgress(time, 22, 24);
            else if (time >= 0 && time < 3) // 0 ~ 0.1
                return 0.1f;
            else if (time >= 3 && time < 5) // 0.1 ~ 0
                result = 0.1f * GetTimeProgress(time, 3, 5);
            else result = 0;
            return result;
        }

        // Characters in the basement floor in Ground Zero gets lit up by ambience lighting, so fucked up
        // Try to cheat here
        protected override float CalculateBaseAmbienceScore(string locationId, float time)
        {
            float playerY = Singleton<ThatsLitMainPlayerComponent>.Instance.MainPlayer.Transform.Original.position.y;
            var reduction = Mathf.Clamp01((14.7f - playerY) / 1.5f) * 0.6f;
            return Mathf.Max(base.CalculateBaseAmbienceScore(locationId, time) - reduction, MinBaseAmbienceScore);
        }
    }

    public class NightFactoryScoreCalculator : ScoreCalculator
    {
        protected override float MinBaseAmbienceScore => -1;
        protected override float MaxMoonlightScore { get => 0; }
        protected override float MaxSunlightScore { get => 0; }
        protected override float MinAmbienceLum { get => 0.002f; }
        protected override float MaxAmbienceLum { get => 0.002f; }
        protected override float PixelLumScoreScale { get => 5f; }
        protected override float ThresholdShine { get => 0.5f; }
        protected override float ThresholdHigh { get => 0.35f; }
        protected override float ThresholdHighMid { get => 0.2f; }
        protected override float ThresholdMid { get => 0.1f; }
        protected override float ThresholdMidLow { get => 0.025f; }
        protected override float ThresholdLow { get => 0.005f; }

        protected override void GetPixelScores(float tlf, out float scoreShine, out float scoreHigh, out float scoreHighMid, out float scoreMid, out float scoreMidLow, out float scoreLow, out float scoreDark)
        {
            scoreShine = 5f;
            scoreHigh = 1.5f;
            scoreHighMid = 0.8f;
            scoreMid = 0.5f;
            scoreMidLow = 0.2f;
            scoreLow = 0.1f;
            scoreDark = 0;
        }
        protected override float GetMapAmbienceCoef(string locationId, float time) => 0;
        protected override float CalculateMoonLight(string locationId, float time, float cloudiness) => 0;
        protected override float CalculateMoonLightTimeFactor(string locationId, float time) => 0;
        internal override float CalculateSunLightTimeFactor(string locationId, float time) => 0;
    }

    public class LabScoreCalculator : ScoreCalculator
    {
        protected override float MinBaseAmbienceScore => -0.1f;
        protected override float MaxMoonlightScore => 0;
        protected override float MaxSunlightScore => 0;
        protected override float MinAmbienceLum => 0.01f;
        protected override float MaxAmbienceLum => 0.01f;
    }
}