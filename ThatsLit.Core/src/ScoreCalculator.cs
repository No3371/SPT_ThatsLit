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

namespace ThatsLit
{
    public class ScoreCalculator
    {
        readonly int RESOLUTION = 32 * ThatsLitPlugin.ResLevel.Value;
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
        protected internal virtual void OnGUI (PlayerLitScoreProfile player, bool layout = false) {
        }

        public void PreCalculate (PlayerLitScoreProfile player, Unity.Collections.NativeArray<Color32> tex, float time)
        {
            GetThresholds(time, out float thS, out float thH, out float thHM, out float thM, out float thML, out float thL);
            StartCountPixels(player, tex, thS, thH, thHM, thM, thML, thL);
        }
        
        public virtual float CalculateMultiFrameScore (float cloud, float fog, float rain, ThatsLitGameworld gameWorld, PlayerLitScoreProfile player, float time, string locationId)
        {
            // if (ThatsLitPlugin.DevMode.Value)
            //     minAmbienceLum = ThatsLitPlugin.OverrideMinAmbienceLum.Value;
            // if (ThatsLitPlugin.DevMode.Value)
            //     maxAmbienceLum = ThatsLitPlugin.OverrideMaxAmbienceLum.Value;
            // if (ThatsLitPlugin.DevMode.Value)
            //     lumScoreScale = ThatsLitPlugin.OverridePixelLumScoreScale.Value;
            
            player.frame5 = player.frame4;
            player.frame4 = player.frame3;
            player.frame3 = player.frame2;
            player.frame2 = player.frame1;
            player.frame1 = player.frame0;
            FrameStats thisFrame = default;
            thisFrame.cloudiness = cloud;
            CompleteCountPixels(player, out thisFrame.pxS, out thisFrame.pxH, out thisFrame.pxHM, out thisFrame.pxM, out thisFrame.pxML, out thisFrame.pxL, out thisFrame.pxD, out thisFrame.lum, out float lumNonDark, out thisFrame.pixels);
            if (player.IsProxy)
            {
                return 0;
            }
            if (thisFrame.pixels == 0) thisFrame.pixels = RESOLUTION * RESOLUTION;
            thisFrame.avgLum = thisFrame.lum / (float)thisFrame.pixels;
            thisFrame.avgLumNonDark = thisFrame.lum / (float) (thisFrame.pixels - thisFrame.pxD);
            thisFrame.avgLumMultiFrames = (thisFrame.avgLum + player.frame1.avgLum + player.frame2.avgLum + player.frame3.avgLum + player.frame4.avgLum + player.frame5.avgLum) / 6f;
            player.UpdateLumTrackers(thisFrame.avgLumMultiFrames);
            
            float insideTime = Time.time - player.Player.lastOutside;
            if (insideTime < 0) insideTime = 0;
            float outside1s = Mathf.Clamp01(1f - insideTime);

            float ambienceShadowFactor = player.Player.AmbienceShadowFactor;
            if (gameWorld.IsWinter) ambienceShadowFactor *= 1f - 0.3f * outside1s;

            var baseAmbienceScore = CalculateBaseAmbienceScore(locationId, time, cloud, player);

            if (gameWorld.IsWinter && insideTime < 2f) // Debuff from winter terrain
            {
                baseAmbienceScore = Mathf.Lerp(baseAmbienceScore, 0.5f, 0.25f * Mathf.InverseLerp(0f, 1.2f, cloud) * Mathf.InverseLerp(1.2f, 0.2f, insideTime)) ;
            }
            baseAmbienceScore += (MinBaseAmbienceScore - baseAmbienceScore) * player.Player.OverheadHaxRatingFactor * 0.2f * GetMapAmbienceCoef(locationId, time);


            // =====
            // Handle bunker environment
            // =====
            float bunkerTimeFactor = Mathf.Pow(player.Player.bunkerTimeClamped / 10f, 2);
            // var bunkerOffset = (baseAmbienceScore - MinBaseAmbienceScore) * (0.5f * bunkerTimeFactor + 0.15f * Mathf.Clamp01((insideTime - 2f) / 3f)); 
            // baseAmbienceScore -= bunkerOffset;

            // Bunker: Bunker is overall moderately lit
            baseAmbienceScore = Mathf.Lerp(baseAmbienceScore, BunkerBaseAmbienceTarget, bunkerTimeFactor * Mathf.Clamp01(insideTime / 10) * 0.65f);
            if (ThatsLitPlayer.IsDebugSampleFrame && player.Player.DebugInfo != null)
                player.Player.DebugInfo.scoreRawBase = baseAmbienceScore;

            var ambienceScore = baseAmbienceScore;
            float insideCoef2to9s = Mathf.InverseLerp(2f, 9f, insideTime); // 0 ~ 2 sec => 0%, 9 sec => 100%
            var locCloudiness = Mathf.Lerp(cloud, 1.3f, bunkerTimeFactor); // So it's always considered "cloudy" in bunker
            ambienceScore += Mathf.Clamp01((locCloudiness - 1f) / -2f) * NonCloudinessBaseAmbienceScoreImpact; // Weather offset
            moonLightScore = CalculateMoonLight(player, locationId, time, locCloudiness);
            sunLightScore = CalculateSunLight(player, locationId, time, locCloudiness);
            ambienceScore += (moonLightScore + sunLightScore)  * (1f - ambienceShadowFactor - insideCoef2to9s * IndoorAmbienceCutoff * ambienceShadowFactor);
            
            // =====
            // Scale up ambience score in winter raidsm given any light (regardless ambience shadow)
            // =====
            if (gameWorld.IsWinter) // Debuff from winter environment when outside
            {
                ambienceScore *= 1 + 0.2f * Mathf.Clamp01((sunLightScore + moonLightScore) / 0.5f) * Mathf.InverseLerp(1.2f, 0.2f, insideTime);
            }

            // =====
            // Ambience score reduction from dense grasses in nights
            // (Plants and grasses look extra dark in nights)
            // =====
            if (player.Player.TerrainDetails != null)
            {
                float surroundingTerrainScoreProne = Singleton<ThatsLitGameworld>.Instance.CalculateDetailScore(player.Player.TerrainDetails, Vector3.zero, 0, 0).prone;
                float footTerrainScoreProne = Singleton<ThatsLitGameworld>.Instance.CalculateCenterDetailScore(player.Player.TerrainDetails).prone;
                var surroundingDetailsScaling = Mathf.Clamp01(surroundingTerrainScoreProne) * 0.667f + Mathf.Clamp01(footTerrainScoreProne) * 0.333f;
                surroundingDetailsScaling = Mathf.Clamp01(surroundingDetailsScaling);
                surroundingDetailsScaling *= Mathf.Clamp01((0.6f * player.Player.TerrainDetails.RecentDetailCount3x3 + 0.4f * player.Player.TerrainDetails.RecentDetailCount5x5)/ 60f);
                surroundingDetailsScaling *= surroundingDetailsScaling;
                surroundingDetailsScaling *= Mathf.InverseLerp(-0.1f, MinBaseAmbienceScore, ambienceScore);
                surroundingDetailsScaling *= NightTerrainImpactScale; // max 0.2
                if (surroundingDetailsScaling > player.detailBonusSmooth)
                    player.detailBonusSmooth = Mathf.Lerp(player.detailBonusSmooth, surroundingDetailsScaling, Time.fixedDeltaTime * 2.2f);
                else if (surroundingDetailsScaling < player.detailBonusSmooth)
                    player.detailBonusSmooth = Mathf.Lerp(player.detailBonusSmooth, surroundingDetailsScaling, Time.fixedDeltaTime * 6f);

                if (gameWorld.IsWinter)
                {
                    player.detailBonusSmooth *= 0.5f; // Debuff from winter environment
                }

                ambienceScore -= player.detailBonusSmooth * outside1s;
                ambienceScore = Mathf.Clamp(ambienceScore, MinBaseAmbienceScore, 1f);
            }
            if (ThatsLitPlayer.IsDebugSampleFrame && player.Player.DebugInfo != null)
                player.Player.DebugInfo.scoreRaw0 = ambienceScore;


            //float score = CalculateTotalPixelScore(time, thisFrame.pxS, thisFrame.pxH, thisFrame.pxHM, thisFrame.pxM, thisFrame.pxML, thisFrame.pxL, thisFrame.pxD);
            //score /= (float)thisFrame.pixels;
            float lowAmbienceScoreFactor = Mathf.Max(0.5f - ambienceScore, 0) / 1.5f;
            float hightLightedPixelFactor = 0.9f * thisFrame.RatioShinePixels + 0.75f * thisFrame.RatioHighPixels + 0.4f * thisFrame.RatioHighMidPixels + 0.15f * thisFrame.RatioMidPixels;
            float lumScore = CalculateRawLumScore(thisFrame, lowAmbienceScoreFactor, hightLightedPixelFactor, player);
            if (ThatsLitPlayer.IsDebugSampleFrame && player.Player.DebugInfo != null)
                player.Player.DebugInfo.scoreRaw1 = lumScore + ambienceScore;

            //var topScoreMultiFrames = FindHighestScoreRecentFrame(true, score);
            //var bottomScoreMultiFrames = FindLowestScoreRecentFrame(true, score);
            var topAvgLumMultiFrames = player.FindHighestAvgLumRecentFrame(true, thisFrame.avgLum);
            var bottomAvgLumMultiFrames = player.FindLowestAvgLumRecentFrame(true, thisFrame.avgLum);
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


            lumScore += CalculateChangingLumModifier(thisFrame.avgLumMultiFrames, player.lum1s, player.lum3s, ambienceScore);
            if (ThatsLitPlayer.IsDebugSampleFrame && player.Player.DebugInfo != null)
                player.Player.DebugInfo.scoreRaw2 = lumScore + ambienceScore;

            // Extra score for multi frames(sides) contrast in darkness
            // For exmaple, lighting rods on the floor contributes not much to the score but should make one much more visible
            var avgLumContrast = topAvgLumMultiFrames - bottomAvgLumMultiFrames; // a.k.a all sides contrast
            avgLumContrast -= 0.01f;
            avgLumContrast = Mathf.Clamp01(avgLumContrast);
            var compensationTarget = avgLumContrast * avgLumContrast + lowAmbienceScoreFactor * 0.5f; // 0.1 -> 0.01, 0.5 -> 0.25
            compensationTarget *= 1 + hightLightedPixelFactor * lowAmbienceScoreFactor;
            var expectedFinalScore = lumScore + ambienceScore;
            var compensation = Mathf.Clamp(compensationTarget - expectedFinalScore, 0, 2); // contrast:0.1 -> final toward 0.1, contrast:0.5 -> final toward 0.25
            lumScore += compensation * Mathf.Clamp01(avgLumContrast * 10f) * lowAmbienceScoreFactor * MultiFrameContrastImpactScale; // amb-1 => 1f, amb-0.5 => *0.75f, amb0 => 5f (not needed)
            if (ThatsLitPlayer.IsDebugSampleFrame && player.Player.DebugInfo != null)
                player.Player.DebugInfo.scoreRaw3 = lumScore + ambienceScore;

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

            if (player.Player.LightAndLaserState.AnyVisible)
            {
                expectedFinalScore = lumScore + ambienceScore;
                if (player.Player.LightAndLaserState.VisibleLight)          compensation = Mathf.Clamp(0.4f - expectedFinalScore, 0, 2) * player.Player.LightAndLaserState.deviceStateCache.light;
                else if (player.Player.LightAndLaserState.VisibleLaser)     compensation = Mathf.Clamp(0.2f - expectedFinalScore, 0, 2) * player.Player.LightAndLaserState.deviceStateCache.laser;
                else if (player.Player.LightAndLaserState.VisibleLightSub)  compensation = Mathf.Clamp(0f - expectedFinalScore, 0, 2) * player.Player.LightAndLaserState.deviceStateCacheSub.light;
                else if (player.Player.LightAndLaserState.VisibleLaserSub)  compensation = Mathf.Clamp(0f - expectedFinalScore, 0, 2) * player.Player.LightAndLaserState.deviceStateCacheSub.laser;
                else compensation = 0;
                lumScore += compensation * (lowAmbienceScoreFactor + 0.1f);
            }

            if (ThatsLitPlayer.IsDebugSampleFrame && player.Player.DebugInfo != null)
                player.Player.DebugInfo.scoreRaw4 = lumScore + ambienceScore;


            player.litScoreFactor = Mathf.Pow(Mathf.Clamp(lumScore, 0, 2f) / 2f, 2); // positive
            player.litScoreFactor /= 1 + Mathf.Max(ambienceScore, 0);
            player.litScoreFactor = Mathf.Max(player.litScoreFactor, 0);
            lumScore -= lumScore * 0.25f * Mathf.Clamp01(ambienceScore + 0.05f); // When ambience is already above -0.05, reduce lumScore contribution
            lumScore += ambienceScore;
            lumScore = Mathf.Clamp(lumScore, -1, 1);

            thisFrame.score = lumScore;
            thisFrame.ambienceScore = ambienceScore;
            thisFrame.baseAmbienceScore = baseAmbienceScore;

            var topScoreMultiFrames = player.FindHighestScoreRecentFrame(true, lumScore);
            var bottomScoreMultiFrames = player.FindLowestScoreRecentFrame(true, lumScore);
            thisFrame.multiFrameLitScore = (topScoreMultiFrames * 2f
                                + thisFrame.score
                                + player.frame1.score
                                + player.frame2.score
                                + player.frame3.score
                                + player.frame4.score
                                + player.frame5.score
                                - bottomScoreMultiFrames * 2) / 6f;

            player.frame0 = thisFrame;

            return thisFrame.multiFrameLitScore;

        }

        /// <summary>
        /// The percentage of score provided by observed brightness (from 3d lightings)
        /// </summary>
        internal float sunLightScore, moonLightScore;

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

        protected virtual float CalculateStaticLumModifier(float score, float avgLumMultiFrames, float envLum, float envLumSlow, PlayerLitScoreProfile player)
        {
            var recentChangeFactor = Mathf.Clamp01(Mathf.Abs(avgLumMultiFrames - player.lum3s) / 0.2f); // (avgLumMultiFrames - envLumEstiSlow) is always approaching zero when the environment lighting is stable
            if (score > 0f) score /= 1 + 0.3f * (1 - recentChangeFactor); // The bigger the difference, the more it should be suppressed
            else if (score < 0f) score *= 1 + 0.1f * (1 - recentChangeFactor);

            recentChangeFactor = Mathf.Clamp01(Mathf.Abs(avgLumMultiFrames - player.lum3s) / 0.2f); // (avgLumMultiFrames - envLumEstiSlow) is always approaching zero when the environment lighting is stable
            if (score > 0f) score /= 1 + 0.1f * (1 - recentChangeFactor); // The bigger the difference, the more it should be suppressed
            else if (score < 0f) score *= 1 + 0.1f * (1 - recentChangeFactor);
            return score;
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

        protected virtual float CalculateRawLumScore (FrameStats thisFrame, float lowAmbienceScoreFactor, float hightLightedPixelFactor, PlayerLitScoreProfile player)
        {
            float lumScore = (thisFrame.avgLum - MinAmbienceLum) * PixelLumScoreScale * (1+2f*lowAmbienceScoreFactor* lowAmbienceScoreFactor) * (0.1f + lowAmbienceScoreFactor);
            lumScore *= 1 + hightLightedPixelFactor;
            lumScore = Mathf.Clamp(lumScore, 0, 2);
            return lumScore;
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

        protected void StartCountPixels(PlayerLitScoreProfile player, NativeArray<Color32> tex, float thresholdShine, float thresholdHigh, float thresholdHighMid, float thresholdMid, float thresholdMidLow, float thresholdLow)
        {
            if (player == null || tex == null || !tex.IsCreated) return;

            NativeArray<float> thresholds = new NativeArray<float>(6, Allocator.TempJob);
            thresholds[0] = thresholdShine;
            thresholds[1] = thresholdHigh;
            thresholds[2] = thresholdHighMid;
            thresholds[3] = thresholdMid;
            thresholds[4] = thresholdMidLow;
            thresholds[5] = thresholdLow;

            player.PixelCountingJob = new PlayerLitScoreProfile.CountPixelsJob()
            {
                thresholds = thresholds,
                tex = tex,
                counted = new NativeArray<int>(8 * JobsUtility.MaxJobThreadCount , Allocator.TempJob, NativeArrayOptions.ClearMemory),
                lum = new NativeArray<float>(2 * JobsUtility.MaxJobThreadCount , Allocator.TempJob, NativeArrayOptions.ClearMemory)
            };
            player.CountingJobHandle = player.PixelCountingJob.Schedule(tex.Length, tex.Length / 64);
            // Logger.LogInfo(string.Format("F{0} Counting {1} px in batches of {2}", Time.frameCount, tex.Length, tex.Length / 64));
        }
        protected void CompleteCountPixels(PlayerLitScoreProfile player, out int shine, out int high, out int highMid, out int mid, out int midLow, out int low, out int dark, out float lum, out float lumNonDark, out int valid)
        {
            shine = high = highMid = mid = midLow = low = dark = valid = 0;
            lum = 0;
            lumNonDark = 0;

            player.CountingJobHandle.Complete();
            var job = player.PixelCountingJob;
            if (player.IsProxy == true)
            {
                job.Dispose();
                player.PixelCountingJob = job;
                return;
            }
            job.tex.Dispose();
            job.thresholds.Dispose();
            
            if (job.lum.IsCreated) for (int i = 0; i < job.lum.Length; i += 2)
            {
                lum += job.lum[i];
                lumNonDark += job.lum[i + 1];
            }
            job.lum.Dispose();
            if (job.counted.IsCreated)
            for (int i = 0; i < job.counted.Length; i += 8)
            {
                if (job.counted[i+7] == 0) continue;
                shine += job.counted[i];
                high += job.counted[i+1];
                highMid += job.counted[i+2];
                mid += job.counted[i+3];
                midLow += job.counted[i+4];
                low += job.counted[i+5];
                dark += job.counted[i+6];
                valid += job.counted[i+7];
                // Logger.LogInfo(string.Format("F{0} #{9}---{1} {2} {3} {4} {5} {6} {7} {8}",
                                // Time.frameCount, job.counted[i], job.counted[i+1], job.counted[i+2], job.counted[i+3],
                                // job.counted[i+4], job.counted[i+5], job.counted[i+6], job.counted[i+7], i/8));
            }
            job.counted.Dispose();
            player.PixelCountingJob = job;
            // Logger.LogInfo(string.Format("F{0} Counted {1} px", Time.frameCount, valid));

        }

        protected virtual float CalculateBaseAmbienceScore(string locationId, float time, float cloud, PlayerLitScoreProfile player)
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

        // The increased visual brightness when moon is up (0~5) when c < 1
        // cloudiness blocks moon light
        // Fog determine visibility and visual brightness of further envrionment, unused
        protected virtual float CalculateMoonLight(PlayerLitScoreProfile player, string locationId, float time, float cloudiness)
        {
            float maxMoonlightScore = GetMaxMoonlightScore();
            return CloudnessToAmbienceScale(cloudiness) * maxMoonlightScore * CalculateMoonLightTimeFactor(locationId, time);
        }

        // The increased visual brightness when sun is up (5~22) hours when c < 1
        // cloudiness blocks sun light
        protected virtual float CalculateSunLight(PlayerLitScoreProfile player, string locationId, float time, float cloudiness)
        {
            float maxSunlightScore = GetMaxSunlightScore();
            return CloudnessToAmbienceScale(cloudiness) * maxSunlightScore * CalculateSunLightTimeFactor(locationId, time);
        }

        // https://www.desmos.com/calculator/vuem57hptl
        // This curve is used to scale sun and moon light from clouded day to fully clear day
        float CloudnessToAmbienceScale (float cloudiness)
        {
            var eval = Mathf.InverseLerp(1.15f, -1.5f, cloudiness);
            eval = eval * eval * (3f - 2f * eval);
            eval = eval * 2.2f;
            return eval;
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
            else if (time >= 19 && time < 22f) // 0.5 ~ 0f
                return 0.5f - GetTimeProgress(time, 19, 22f) * 0.5f;
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
        protected internal virtual float GetMinAmbianceLum()
        {
            float minAmbienceLum = MinAmbienceLum;
            // if (ThatsLitPlugin.DevMode.Value)
            //     minAmbienceLum = ThatsLitPlugin.OverrideMinAmbienceLum.Value;
            return minAmbienceLum;
        }
        protected internal virtual float GetMaxAmbianceLum()
        {
            float maxAmbienceLum = MaxAmbienceLum;
            // if (ThatsLitPlugin.DevMode.Value)
            //     maxAmbienceLum = ThatsLitPlugin.OverrideMaxAmbienceLum.Value;
            return maxAmbienceLum;
        }
        protected internal virtual float GetAmbianceLumRange()
        {
            return GetMaxAmbianceLum() + 0.001f - GetMinAmbianceLum();
        }
        protected internal virtual float GetMaxSunlightScore()
        {
            float maxSunlightScore = MaxSunlightScore;
            // if (ThatsLitPlugin.DevMode.Value)
            //     maxSunlightScore = ThatsLitPlugin.OverrideMaxSunLightScore.Value;
            return maxSunlightScore;
        }
        protected internal virtual float GetMaxMoonlightScore()
        {
            float maxMoonlightScore = MaxMoonlightScore;
            // if (ThatsLitPlugin.DevMode.Value)
            //     maxMoonlightScore = ThatsLitPlugin.OverrideMaxMoonLightScore.Value;
            return maxMoonlightScore;
        }
        protected virtual float MinBaseAmbienceScore { get => -0.9f; }
        protected virtual float MaxBaseAmbienceScore { get => -0.1f; }
        /// <summary>
        /// The ambience change between c-1 and c1 during the darkest hours
        /// </summary>
        /// <value></value>
        protected virtual float NonCloudinessBaseAmbienceScoreImpact { get => 0.1f; }
        protected virtual float BunkerBaseAmbienceTarget { get => -0.3f; }
        protected virtual float MaxMoonlightScore { get => 0.25f; }
        protected virtual float MaxSunlightScore { get => 0.25f; }
        protected virtual float IndoorAmbienceCutoff { get => 0f; }
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
        protected virtual float MultiFrameContrastImpactScale { get => 1f; }
        protected virtual float NightTerrainImpactScale { get => 0.2f; }
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
        
        protected override float MinBaseAmbienceScore => -0.82f;
        protected override float MaxBaseAmbienceScore => -0.1f;
        protected override float MinAmbienceLum => 0.011f;
        protected override float MaxAmbienceLum => 0.015f;
        protected override float ThresholdShine { get => 0.3f; }
        protected override float ThresholdHigh { get => 0.2f; }
        protected override float ThresholdHighMid { get => 0.12f; }
        protected override float ThresholdMid { get => 0.06f; }
        protected override float ThresholdMidLow { get => 0.03f; }
        protected override float ThresholdLow { get => 0.015f; }
        protected override float PixelLumScoreScale { get => 2.5f; }
        protected override float BunkerBaseAmbienceTarget { get => -0.5f; }
    }

    public class WoodsScoreCalculator : ScoreCalculator
    {
        protected override float MinBaseAmbienceScore => -0.75f;
        protected override float MinAmbienceLum => 0.015f;
        protected override float MaxAmbienceLum => 0.017f;
        protected override float ThresholdShine { get => 0.2f; }
        protected override float ThresholdHigh { get => 0.1f; }
        protected override float ThresholdHighMid { get => 0.05f; }
        protected override float ThresholdMid { get => 0.02f; }
        protected override float ThresholdMidLow { get => 0.01f; }
        protected override float ThresholdLow { get => 0.005f; }
        protected override float NightTerrainImpactScale { get => 0.3f; }

        protected override float GetMapAmbienceCoef(string locationId, float time)
        {
            if (time >= 5 && time < 7.5f) // 0 ~ 0.5f
                return 0.5f * Mathf.InverseLerp(5, 7.5f, time);
            else if (time >= 7.5f && time < 12f) // 0.5f ~ 1
                return 0.5f + 0.5f * Mathf.InverseLerp(7.5f, 12, time);
            else if (time >= 12f && time < 15f) // 1 ~ 1
                return 1;
            else if (time >= 15f && time < 18f) // 1 ~ 0.8f
                return 1f - 0.2f * Mathf.InverseLerp(15, 18, time);
            else if (time >= 18 && time < 20f) // 1 ~ 0.35
                return 0.8f - 0.4f * Mathf.InverseLerp(18, 20f, time);
            else if (time >= 20 && time < 23f)
                return 0.4f - 0.3f * Mathf.InverseLerp(20, 23f, time);
            else if (time >= 23 && time < 24) // 0 ~ 0.1
                return 0.1f;
            else if (time >= 0 && time < 3) // 0 ~ 0.1
                return 0.1f;
            else if (time >= 3 && time < 5) // 0.1 ~ 0
                return 0.1f - 0.1f * Mathf.InverseLerp(3, 5, time);
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
        public class Data
        {
            private bool isPlayerInParking;
            public bool IsPlayerInParking
            {
                get => isPlayerInParking;
                set
                {
                    if (isPlayerInParking != value)
                    {
                        TimeEnterOrLeaveParking = Time.time;
                    }
                    isPlayerInParking = value;
                }
            }
            public float TimeEnterOrLeaveParking { get; set; }
            public float ParkingTransitionFactor => Mathf.Clamp01((Time.time - TimeEnterOrLeaveParking) / 5f);
            public bool IsOverhead { get; set; }
        }

        protected override float MinBaseAmbienceScore => -0.8f;
        protected override float MaxBaseAmbienceScore { get => -0.15f; } // indoor
        protected override float MaxMoonlightScore => base.MaxMoonlightScore * 0.75f;
        protected override float MinAmbienceLum => 0.008f;
        protected override float MaxAmbienceLum => 0.008f;
        protected override float PixelLumScoreScale { get => 2.5f; }
        protected override float IndoorAmbienceCutoff => 1.0f;
        protected override float ThresholdShine { get => 0.5f; }
        protected override float ThresholdHigh { get => 0.35f; }
        protected override float ThresholdHighMid { get => 0.2f; }
        protected override float ThresholdMid { get => 0.1f; }
        protected override float ThresholdMidLow { get => 0.025f; }
        protected override float ThresholdLow { get => 0.005f; }

        protected override float MultiFrameContrastImpactScale => 0.65f;

        protected override float CalculateMoonLight(PlayerLitScoreProfile player, string locationId, float time, float cloudiness)
        {
            Data data = player.ScoreCalcData as Data;
            if (data.IsPlayerInParking) cloudiness = Mathf.Lerp(cloudiness, 1.3f, data.ParkingTransitionFactor);
            return base.CalculateMoonLight(player, locationId, time, cloudiness);
        }

        protected override float CalculateSunLight(PlayerLitScoreProfile player, string locationId, float time, float cloudiness)
        {
            Data data = player.ScoreCalcData as Data;
            if (data.IsPlayerInParking) cloudiness = Mathf.Lerp(cloudiness, 1.3f, data.ParkingTransitionFactor);
            return base.CalculateSunLight(player, locationId, time, cloudiness);
        }

        public override float CalculateMultiFrameScore(float cloud, float fog, float rain, ThatsLitGameworld gameWorld, PlayerLitScoreProfile player, float time, string locationId)
        {
            if (player.ScoreCalcData as Data == null)
                player.ScoreCalcData = new Data();
            Data data = player.ScoreCalcData as Data;

            bool isParking = false;
            data.IsOverhead = false;

            if (Physics.Raycast(new Ray(player.Player.Player.MainParts[BodyPartType.head].Position, Vector3.up), out var hit, 5, LayerMaskClass.LowPolyColliderLayerMask))
            {
                data.IsOverhead = true;
                if (hit.transform.gameObject.scene.name == "Shopping_Mall_parking_work" && player.Player.transform.position.y < 23.5f) isParking = true;
            }

            if (data.IsPlayerInParking != isParking) data.IsPlayerInParking = isParking;
            return base.CalculateMultiFrameScore(cloud, fog, rain, gameWorld, player, time, locationId);
        }

        // Characters in the basement floor gets lit up by ambience lighting
        protected override float CalculateBaseAmbienceScore(string locationId, float time, float cloud, PlayerLitScoreProfile player)
        {
            if (player.ScoreCalcData as Data == null)
                player.ScoreCalcData = new Data();
            Data data = player.ScoreCalcData as Data;
            if (data.IsPlayerInParking)
                return Mathf.Lerp(base.CalculateBaseAmbienceScore(locationId, time, cloud, player), MinBaseAmbienceScore + 0.15f * cloud , (player.Player.OverheadHaxRatingFactor * player.Player.OverheadHaxRatingFactor) * data.ParkingTransitionFactor);
            else if (data.IsOverhead)
            {
                // Enhance indoor darkness in daytime
                if (this.CalculateSunLightTimeFactor(locationId, Utility.GetInGameDayTime()) > 0.05f)
                {
                    return Mathf.Lerp(base.CalculateBaseAmbienceScore(locationId, time, cloud, player), -0.54f, 0.7f * Mathf.InverseLerp(2f, 7f, Time.time - player.Player.lastOutside) * (player.Player.AmbienceShadowFactor));
                }
                else
                {
                    // Limiting darkness indoor in nights
                    float fading = Mathf.Clamp01((player.Player.transform.position.y - 22f) / 2.5f);
                    return Mathf.Lerp(base.CalculateBaseAmbienceScore(locationId, time, cloud, player), -0.35f + 0.2f * cloud , (player.Player.OverheadHaxRatingFactor * player.Player.OverheadHaxRatingFactor) * 0.9f * fading);
                }
            }

            return base.CalculateBaseAmbienceScore(locationId, time, cloud, player);
        }

        protected override float CalculateRawLumScore (FrameStats thisFrame, float lowAmbienceScoreFactor, float hightLightedPixelFactor, PlayerLitScoreProfile player)
        {
            Data data = player.ScoreCalcData as Data;
            if (!data.IsPlayerInParking)
                return base.CalculateRawLumScore(thisFrame, lowAmbienceScoreFactor, hightLightedPixelFactor, player);

            float lumScore = (thisFrame.avgLum - MinAmbienceLum) * (PixelLumScoreScale*1.2f) * (1+2f*lowAmbienceScoreFactor* lowAmbienceScoreFactor) * (0.1f + lowAmbienceScoreFactor);
            lumScore *= 1f - (thisFrame.RatioDarkPixels + thisFrame.RatioLowPixels * 0.75f + thisFrame.RatioMidLowPixels * 0.5f) * (1f - thisFrame.cloudiness * 0.15f) * (player.Player.OverheadHaxRatingFactor * player.Player.OverheadHaxRatingFactor) * data.ParkingTransitionFactor;
            lumScore *= 1 + hightLightedPixelFactor;
            lumScore = Mathf.Clamp(lumScore, 0.1f, 2); // When there is no sun/moon light the player model is pitch black
            return lumScore;
        }

        protected override float CalculateMoonLightTimeFactor(string locationId, float time)
        {
            if (time > 23.9 && time < 0)
                return 0.1f * (24f - time);
            else if (time >= 0 && time < 3.5f) 
                return 0.1f + 0.9f * GetTimeProgress(time, 0, 2f);
            else if (time >= 3.5f && time < 4.33) 
                return 1f - 0.5f * GetTimeProgress(time, 3.5f, 4.33f); 
            else if (time >= 4.33f && time < 4.416f) 
                return 0.5f - 0.3f * GetTimeProgress(time,  4.33f, 4.416f); // How interesting, the moon lose its brightness in 5 mins
            else if (time >= 4.416f && time < 5)
                return 0.3f - GetTimeProgress(time, 4.416f, 5f);
            else return 0;
        }

        protected internal override void OnGUI(PlayerLitScoreProfile player, bool layout = false)
        {
            if (player.ScoreCalcData as Data == null)
                player.ScoreCalcData = new Data();
            Data data = player.ScoreCalcData as Data;
            base.OnGUI(player, layout);
            if (data.IsPlayerInParking) GUILayout.Label($"  Parking: { data.ParkingTransitionFactor }");
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
            else if (time >= 12 && time < 15.5f) // 1 ~ 1
                return 1;
            else if (time >= 15.5f && time < 20.5f) // 1 ~ 0.65f
                return 1f - 0.35f * GetTimeProgress(time, 15.5f, 20.5f);
            else if (time >= 20.5f && time < 21.9f) // 0.5 ~ 0f
                return 0.65f - 0.65f * GetTimeProgress(time, 20.5f, 21.9f);
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
        protected override float PixelLumScoreScale { get => 1.9f; }

        protected override float GetMapAmbienceCoef(string locationId, float time)
        {
            if (time >= 6 && time < 7.5f) // 0 ~ 0.35f
                return 0.35f * GetTimeProgress(time, 6, 7.5f);
            else if (time >= 7.5f && time < 12f) // 0.5f ~ 1
                return 0.35f + 0.65f * GetTimeProgress(time, 7.5f, 12);
            else if (time >= 12 && time < 15) // 1 ~ 1
                return 1;
            else if (time >= 15 && time < 18) // 1 ~ 0.8f
                return 1f - 0.2f * GetTimeProgress(time, 15, 18);
            else if (time >= 18 && time < 20) // 1 ~ 0.3f
                return 0.8f - 0.4f * GetTimeProgress(time, 18, 20);
            else if (time >= 20 && time < 21.8f) // 0.3 ~ 0
                return 0.4f - 0.4f * GetTimeProgress(time, 20, 21.8f);
            else if (time >= 21.8 && time < 24) // 0 ~ 0.1
                return 0.1f * GetTimeProgress(time, 22, 24); // city lights?
            else if (time >= 0 && time < 3) // 0 ~ 0.1
                return 0.1f;
            else if (time >= 3 && time < 5) // 0.1 ~ 0
                return 0.1f * GetTimeProgress(time, 3, 5);
            else return 0;
        }
    }

    public class GroundZeroScoreCalculator : ScoreCalculator
    {
        
        protected override float MinBaseAmbienceScore => -0.75f;
        protected override float MaxBaseAmbienceScore => -0.15f;
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
        protected override float CalculateBaseAmbienceScore(string locationId, float time, float cloud, PlayerLitScoreProfile player)
        {
            float playerY = player.Player.Player.Transform.Original.position.y;
            var reduction = Mathf.Clamp01((14.7f - playerY) / 1.5f) * 0.6f;
            return Mathf.Max(base.CalculateBaseAmbienceScore(locationId, time, cloud, player) - reduction, MinBaseAmbienceScore);
        }
    }

    public class NightFactoryScoreCalculator : ScoreCalculator
    {
        
        protected override float MinBaseAmbienceScore => -0.87f;
        protected override float MaxMoonlightScore { get => 0; }
        protected override float MaxSunlightScore { get => 0; }
        protected override float MinAmbienceLum { get => 0.002f; }
        protected override float MaxAmbienceLum { get => 0.002f; }
        protected override float PixelLumScoreScale { get => 6f; }
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
        protected override float GetMapAmbienceCoef(string locationId, float time) => 0.1f;
        protected override float CalculateMoonLight(PlayerLitScoreProfile player, string locationId, float time, float cloudiness) => 0;
        protected override float CalculateMoonLightTimeFactor(string locationId, float time) => 0;
        internal override float CalculateSunLightTimeFactor(string locationId, float time) => 0;
    }

    public class LabScoreCalculator : ScoreCalculator
    {
        protected override float BunkerBaseAmbienceTarget { get => 0f; }
        protected override float MinBaseAmbienceScore => -0.5f;
        protected override float MaxBaseAmbienceScore => -0.5f;
        protected override float MaxMoonlightScore { get => 0; }
        protected override float MaxSunlightScore { get => 0; }
        protected override float MinAmbienceLum { get => 0.02f; }
        protected override float MaxAmbienceLum { get => 0.06f; }
        protected override float ThresholdShine { get => 0.2f; }
        protected override float ThresholdHigh { get => 0.1f; }
        protected override float ThresholdHighMid { get => 0.05f; }
        protected override float ThresholdMid { get => 0.025f; }
        protected override float ThresholdMidLow { get => 0.01f; }
        protected override float ThresholdLow { get => 0.005f; }
        protected override float PixelLumScoreScale { get => 5.5f; }
        protected override float GetMapAmbienceCoef(string locationId, float time) => 0f;
        protected override float CalculateMoonLight(PlayerLitScoreProfile player, string locationId, float time, float cloudiness) => 0;
        protected override float CalculateMoonLightTimeFactor(string locationId, float time) => 0;
        internal override float CalculateSunLightTimeFactor(string locationId, float time) => 0;
    }
}