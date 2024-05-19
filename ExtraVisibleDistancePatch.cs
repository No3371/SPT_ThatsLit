#define DEBUG_DETAILS
using Aki.Reflection.Patching;
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
        public static bool PatchPrefix(EnemyInfo __instance, KeyValuePair<EnemyPart, EnemyPartData> part, ref float addVisibility)
        {
            if (__instance?.Owner == null
             || (part.Key?.Owner?.IsAI ?? true) == true
             || !ThatsLitPlugin.EnabledMod.Value
             || ThatsLitPlugin.LitVisionDistanceScale.Value == 0
             || !ThatsLitPlugin.EnabledLighting.Value)
                return true;

            if (Singleton<ThatsLitGameworld>.Instance.ScoreCalculator == null || __instance.Owner?.LookSensor == null) return true;

            ThatsLitMainPlayerComponent player = null;
            Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(__instance.Person, out player);
            if (player == null) return true;

            ThatsLitPlugin.swExtraVisDis.MaybeResume();

            bool thermalActive = false, nvgActive = false, scope = false;
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
                EFT.InventoryLogic.SightComponent sightMod = __instance.Owner.GetPlayer?.ProceduralWeaponAnimation?.CurrentAimingMod;
                if (sightMod != null)
                {
                    scope = true;
                    ThatsLitCompat.Scopes.TryGetValue(sightMod.Item.TemplateId, out var scopeCompat);
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
                if (compensation > 0) addVisibility += UnityEngine.Random.Range(0.5f, 1f) * compensation * ThatsLitPlugin.LitVisionDistanceScale.Value;
            }
            else if (nvgActive && frame0.ambienceScore < 0) // Base + Sun/Moon < 0
            {
                float scale;
                if (player.LightAndLaserState.IRLight) scale = 4f;
                else if (player.LightAndLaserState.IRLaser) scale = 3.5f;
                else scale = 3f;
                scale = Mathf.Lerp(1, scale, Mathf.Clamp01(frame0.ambienceScore / -1f)); // The darker the ambience, the more effective the NVG is

                float compensation = __instance.Owner.Settings.FileSettings.Look.FAR_DISTANCE - originalDist;
                if (compensation > 0)
                {
                    compensation *= 1f - fogFactor;
                    compensation *= player.PlayerLitScoreProfile.litScoreFactor * ThatsLitPlugin.LitVisionDistanceScale.Value * scale;
                    addVisibility += compensation * UnityEngine.Random.Range(0.25f, 1f); // 0.25x~1x of extra capped at 100m
                }
            }
            else if (!nvgActive && frame0.ambienceScore > 0) // Base + Sun/Moon > 0
            {
                float compensation = __instance.Owner.Settings.FileSettings.Look.FAR_DISTANCE - originalDist;
                if (compensation > 0)
                {
                    compensation *= 1f - fogFactor;
                    compensation *= (1f + frame0.ambienceScore / 5f) * ThatsLitPlugin.LitVisionDistanceScale.Value;
                    addVisibility += compensation * UnityEngine.Random.Range(0.2f, 1f);
                }
            } 
            else if (!nvgActive) // 
            {
                float litDiff = frame0.multiFrameLitScore - frame0.baseAmbienceScore; // The visibility provided by sun/moon + lightings
                litDiff = Mathf.InverseLerp(0, 2, litDiff);
                litDiff *= 1f - fogFactor;

                float compensation = __instance.Owner.Settings.FileSettings.Look.MIDDLE_DIST - originalDist;
                if (compensation > 0)
                {
                    addVisibility += litDiff * UnityEngine.Random.Range(0.5f, 1f);
                }
            }

            ThatsLitPlugin.swExtraVisDis.Stop();

            return true;
        }
    }
}