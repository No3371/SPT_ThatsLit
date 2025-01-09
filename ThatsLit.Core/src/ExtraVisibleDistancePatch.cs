#define DEBUG_DETAILS
using SPT.Reflection.Patching;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using System.Collections.Generic;
using EFT.InventoryLogic;
using System;
using EFT;


namespace ThatsLit
{
    public class ExtraVisibleDistancePatch : ModulePatch
    {
        internal static System.Diagnostics.Stopwatch _benchmarkSW;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EnemyInfo), "CheckVisibility");
        }

        [PatchPrefix]
        [HarmonyAfter("me.sol.sain")]
        public static bool PatchPrefix(EnemyInfo __instance, KeyValuePair<EnemyPart, EnemyPartData> part, ref float addVisibility)
        {
            ThatsLitPlugin.swExtraVisDis.MaybeResume();
            if (__instance?.Owner == null
             || (part.Key?.Owner?.IsAI ?? true) == true
             || !ThatsLitPlugin.EnabledMod.Value
             || ThatsLitPlugin.ExtraVisionDistanceScale.Value == 0
             || !ThatsLitPlugin.EnabledLighting.Value
             || ThatsLitPlugin.PMCOnlyMode.Value && !Utility.IsPMCSpawnType(__instance.Owner?.Profile?.Info?.Settings?.Role))
            {
                ThatsLitPlugin.swExtraVisDis.Stop();
                return true;
            }

            if (Singleton<ThatsLitGameworld>.Instance?.ScoreCalculator == null || __instance.Owner?.LookSensor == null) return true;

            ThatsLitPlayer player = null;
            Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(__instance.Person, out player);
            if (player == null || player.PlayerLitScoreProfile == null)
            {
                ThatsLitPlugin.swExtraVisDis.Stop();
                return true;
            }


            bool isNearest = false;
            if (player.lastNearest == __instance.Owner)
            {
                isNearest = true;
                if (player.DebugInfo != null)
                {
                    player.DebugInfo.lastDisComp = 0f;
                    player.DebugInfo.lastDisCompThermal = 0f;
                    player.DebugInfo.lastDisCompNVG = 0f;
                    player.DebugInfo.lastDisCompDay = 0f;
                }
            }

            bool thermalActive = false, nvgActive = false;
            float thermalRange = 0;

            var botNVG = __instance.Owner?.NightVision;
            if (botNVG?.UsingNow == true) // goggles
            {
                NightVisionComponent.EMask? mask = botNVG.NightVisionItem?.Template?.Mask;
                thermalActive = mask == EFT.InventoryLogic.NightVisionComponent.EMask.Thermal;
                nvgActive = mask != null && mask != EFT.InventoryLogic.NightVisionComponent.EMask.Thermal;
            }
            else
            {
                EFT.InventoryLogic.SightComponent sightMod = __instance.Owner?.GetPlayer?.ProceduralWeaponAnimation?.CurrentAimingMod;
                if (sightMod != null)
                {
                    ThatsLitCompat.Scopes.TryGetValue(sightMod.Item?.TemplateId, out var scopeCompat);
                    if (scopeCompat?.TemplateInstance?.thermal != null)
                    {
                        thermalActive = true;
                        thermalRange = scopeCompat.TemplateInstance.thermal.effectiveDistance;
                    }
                    else if (scopeCompat?.TemplateInstance?.nightVision != null)
                        nvgActive = true;
                }
            }

            float fogFactor = EFT.Weather.WeatherController.Instance?.WeatherCurve?.Fog?? 0f;
            fogFactor = Mathf.InverseLerp(0, 0.35f, fogFactor);
            ScoreCalculator scoreCalculator = Singleton<ThatsLitGameworld>.Instance.ScoreCalculator;
            FrameStats frame0 = player.PlayerLitScoreProfile?.frame0 ?? default;
            var originalDist = __instance.Owner.LookSensor.VisibleDist;
            if (thermalActive)
            {
                float compensation = thermalRange - originalDist;
                if (compensation > 0) addVisibility += UnityEngine.Random.Range(0.5f, 1f) * compensation * ThatsLitPlugin.ExtraVisionDistanceScale.Value;
                if (isNearest && player.DebugInfo != null)
                    player.DebugInfo.lastDisCompThermal = compensation;
            }
            else if (nvgActive && frame0.ambienceScore < 0) // Base + Sun/Moon < 0
            {
                float scale;
                if (player.LightAndLaserState.IRLight)
                    scale = 3.5f;
                else if (player.LightAndLaserState.IRLaser)
                    scale = 3f;
                else
                    scale = 2.5f;
                scale = Mathf.Lerp(1, scale, Mathf.Clamp01(frame0.ambienceScore / -1f)); // The darker the ambience, the more effective the NVG is

                float compensation = __instance.Owner.Settings.FileSettings.Look.MIDDLE_DIST - originalDist;
                if (compensation > 0)
                {
                    compensation *= 1f - fogFactor;
                    compensation *= scale;
                    compensation *= player.PlayerLitScoreProfile.litScoreFactor * ThatsLitPlugin.ExtraVisionDistanceScale.Value;
                    addVisibility += compensation * UnityEngine.Random.Range(0.25f, 1f); // 0.25x~1x of extra capped at 100m
                }
                if (isNearest && player.DebugInfo != null)
                    player.DebugInfo.lastDisCompNVG = compensation;
            }
            else if (!nvgActive && frame0.ambienceScore > 0) // Base + Sun/Moon > 0
            {
                float compensation = __instance.Owner.Settings.FileSettings.Look.MIDDLE_DIST - originalDist;
                if (compensation > 0)
                {
                    compensation *= 1f - fogFactor;
                    compensation *= (1f + frame0.ambienceScore / 5f) * ThatsLitPlugin.ExtraVisionDistanceScale.Value;
                    addVisibility += compensation * UnityEngine.Random.Range(0.2f, 1f);
                }
                if (isNearest && player.DebugInfo != null)
                    player.DebugInfo.lastDisCompDay = compensation;
            }
            else if (!nvgActive)
            {
                float litDiff = frame0.multiFrameLitScore - frame0.baseAmbienceScore; // The visibility provided by sun/moon + lightings
                litDiff = Mathf.InverseLerp(0, 2, litDiff);
                litDiff *= 1f - fogFactor;

                float compensation = __instance.Owner.Settings.FileSettings.Look.MIDDLE_DIST - originalDist;
                if (compensation > 0)
                {
                    compensation *= litDiff;
                    addVisibility += compensation;
                }
                if (isNearest && player.DebugInfo != null)
                    player.DebugInfo.lastDisComp = compensation;
            }

            ThatsLitPlugin.swExtraVisDis.Stop();

            return true;
        }
    }
}