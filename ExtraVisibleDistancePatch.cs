#define DEBUG_DETAILS
using Aki.Reflection.Patching;
using HarmonyLib;
using ThatsLit.Components;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using System.Collections.Generic;


namespace ThatsLit
{
    public class ExtraVisibleDistancePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EnemyInfo), "CheckVisibility");
        }

        [PatchPrefix]
        public static bool PatchPrefix(EnemyInfo __instance, KeyValuePair<EnemyPart, EnemyPartData> part, ref float addVisibility)
        {
            if (!part.Key.Owner.IsYourPlayer || ThatsLitPlugin.LitVisionDistanceScale.Value == 0 || !ThatsLitPlugin.EnabledLighting.Value) return true;

            ThatsLitMainPlayerComponent mainPlayer = Singleton<ThatsLitMainPlayerComponent>.Instance;
            if (mainPlayer.scoreCalculator == null) return true;

            float fromLights = 1;
            if (mainPlayer.scoreCalculator.frame0.multiFrameLitScore < 0)
            {
                if (__instance.Owner.NightVision.UsingNow && __instance.Owner.NightVision.NightVisionItem?.Template?.Mask != EFT.InventoryLogic.NightVisionComponent.EMask.Thermal)
                {
                    if (mainPlayer?.scoreCalculator?.irLight?? false) fromLights = 2f;
                    else if (mainPlayer?.scoreCalculator?.irLaser?? false) fromLights = 1.7f;
                }
                fromLights = Mathf.Lerp(1, fromLights, Mathf.Clamp01(mainPlayer.scoreCalculator.frame0.multiFrameLitScore / -1f));
            }

            float delta = __instance.Owner.LookSensor.VisibleDist * mainPlayer.scoreCalculator.litScoreFactor * ThatsLitPlugin.LitVisionDistanceScale.Value * fromLights;
            delta = Mathf.Min(75, delta);

            addVisibility += UnityEngine.Random.Range(delta * 0.2f, delta);
            return true;
        }
    }
}