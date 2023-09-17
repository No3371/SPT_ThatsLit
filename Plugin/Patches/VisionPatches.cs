using Aki.Reflection.Patching;
using EFT;
using HarmonyLib;
using ThatsLit.Components;
using System;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using EFT.Utilities;
using System.Globalization;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace ThatsLit.Patches.Vision
{
    public class SeenCoefPatch : ModulePatch
    {
        private static PropertyInfo _enemyRel;

        protected override MethodBase GetTargetMethod()
        {
            _enemyRel = AccessTools.Property(typeof(BotMemoryClass), "GoalEnemy");
            Type lookType = _enemyRel.PropertyType;

            return AccessTools.Method(lookType, "method_7");
        }

        private static int lastFrame;
        private static float closetLastFrame;

        [PatchPostfix]
        public static void PatchPostfix(GClass478 __instance, BifacialTransform BotTransform, BifacialTransform enemy, ref float __result)
        {
            if (__result == 8888 || ThatsLitPlugin.DisableEffect.Value) return;
            var original = __result;

            ThatsLitMainPlayerComponent mainPlayer = Singleton<ThatsLitMainPlayerComponent>.Instance;
            if (Time.frameCount != lastFrame)
            {
                lastFrame = Time.frameCount;
                closetLastFrame = float.MaxValue;
                if (mainPlayer) mainPlayer.calcedLastFrame = 0;
            }

            Vector3 to = enemy.position - BotTransform.position;
            var dis = to.magnitude;
            var disFactor = Mathf.Clamp01((dis - 10) / 100f);
            // To scale down various sneaking bonus
            // The bigger the distance the bigger it is, capped to 110m
            disFactor = disFactor * disFactor * 0.5f; // A slow accelerating curve, 110m => 1, 10m => 0

            if (__instance.Owner.WeaponManager.ShootController.IsAiming)
            {
                disFactor *= 1 + 0.1f * (300 - __instance.Owner.WeaponManager.CurrentWeapon.GetSightingRange()) / 100;
                // 10m sight? => 1.29x... 10m -> 0, 110m -> 0.645
                // 50m sight => 1.25x... 10m -> 0, 110m -> 0.6
                // 100m sight => 1.2x... 10m -> 0, 110m -> 0.6
                // 300m sight => 1x... 110m -> 0.5
                // 600m sight => 0.8x... 110m -> 0.4
                // 1000m sight => 0.3x... 110m -> 0.15
            }

            var poseFactor = __instance.Person.AIData.Player.PoseLevel / __instance.Person.AIData.Player.Physical.MaxPoseLevel;
            if (UnityEngine.Random.Range(0f, 1f) < 0.01f * disFactor / poseFactor)
            {
                __result *= 10;
                // prone, 110m, 6%
            }

            if (EFTInfo.IsPlayerMainPlayer(__instance.Person))
            {
                if (!mainPlayer) return;
                if (mainPlayer.disableVisionPatch) return;

                bool foundCloser = false;
                if (dis < closetLastFrame)
                {
                    closetLastFrame = dis;
                    foundCloser = true;
                    if (Time.frameCount % 30 == 0) mainPlayer.lastCalcFrom = original;
                }


                var score = mainPlayer.multiFrameLitScore; // -1 ~ 1
                if (score < 0 && __instance.Owner.NightVision.UsingNow) // The score was not reduced (toward 0) for IR lights, process the score here
                {
                    if (mainPlayer.lightOn && mainPlayer.lightIR) score *= 1.5f;
                    else if (mainPlayer.laserOn && mainPlayer.laserIR) score *= 1.25f;
                }

                var factor = Mathf.Pow(score, ThatsLitMainPlayerComponent.POWER); // -1 ~ 1, the graph is basically flat when the score is between ~0.3 and 0.3


                // Maybe randomly lose vision
                if (UnityEngine.Random.Range(0f, 1f) < disFactor
                 || UnityEngine.Random.Range(0f, 1f) < disFactor * mainPlayer.foliageScore * (1 - factor)) // Among bushes, from afar
                {
                    __result *= 10f;
                    if (Time.frameCount % 30 == 0 && foundCloser) mainPlayer.lastCalcTo = __result;
                    return;
                }

                if (factor < 0 && __instance.Owner.NightVision.UsingNow)
                    factor *= Mathf.Clamp01(disFactor + 0.1f); // Factor is reduced to only 10% at 10m for AIs using NVG, but at 110m their vision still get impacted

                //if (factor < 0 && (__instance.Person.AIData.UsingLight || __instance.Person.AIData.GetFlare)) factor /= 5f; // Moved to score calculation

                //if (__instance.Person.AIData.Player.IsInPronePose)
                //{
                //    if (factor < 0f) factor *= 1 + disFactor / 2f; // Darkness will be more effective from afar
                //    else if (factor > 0f) factor /= 1 + disFactor / 2f; // Highlight will be less effective from afar
                //}

                // Give posing influence
                // (0.1) prone, 0.5 (110m) => 1.28 / (0.1) prone, 0.1 (20m) => 1.072
                // (1) stand => 1
                if (factor < 0) factor *= 1 + disFactor * ((1 - poseFactor) * 0.8f); // Darkness will be more effective from afar
                else if (factor > 0) factor /= 1 + disFactor; // Highlight will be less effective from afar
                factor = Mathf.Clamp(factor, -0.95f, 0.95f);

                // factor: -0.1 => -0.005~-0.01, factor: -0.2 => -0.02~-0.04, factor: -0.5 => -0.125~-0.25, factor: -0.8 => -0.32~-0.64
                // 
                var reducingSeconds = (Mathf.Pow(Mathf.Abs(factor), 2)) * Mathf.Sign(factor) * UnityEngine.Random.Range(0.5f, 1f);
                reducingSeconds *= factor < 0 ? 1 : 0.1f; // Give positive factor a lower impact because the normal value are like 0.15 or something
                __result -= reducingSeconds;

                if (factor < 0 && UnityEngine.Random.Range(-1, 0) > factor) __result = 8888f;
                else if (factor > 0 && UnityEngine.Random.Range(0, 1) < factor) __result *= (1f - factor * 0.5f * ThatsLitPlugin.ImpactScale.Value); // Make it so even at 100% it only reduce half of the time
                else if (factor < -0.9f) __result *= 1 - (factor * 3f * ThatsLitPlugin.ImpactScale.Value);
                else if (factor < -0.5f) __result *= 1 - (factor * 2f * ThatsLitPlugin.ImpactScale.Value);
                else if (factor < -0.2f) __result *= 1 - factor * ThatsLitPlugin.ImpactScale.Value;
                else if (factor < 0f) __result *= 1 - factor / 2f * ThatsLitPlugin.ImpactScale.Value;
                else if (factor > 0f) __result /= (1 + factor / 2f * ThatsLitPlugin.ImpactScale.Value);

                __result += ThatsLitPlugin.ImpactOffset.Value;
                if (__result < 0.001f) __result = 0.001f;

                if (Time.frameCount % 30 == 0 && foundCloser) mainPlayer.lastCalcTo = __result;
                mainPlayer.calced++;
                mainPlayer.calcedLastFrame++;

            }
        }
    }

    // Thanks to SAIN
    internal class EFTInfo
    {
        public static bool IsEnemyMainPlayer(BotOwner bot) => EFTInfo.IsPlayerMainPlayer(EFTInfo.GetPlayer(bot?.Memory?.GoalEnemy?.Person));

        public static bool IsPlayerMainPlayer(Player player) => (UnityEngine.Object)player != (UnityEngine.Object)null && EFTInfo.Compare(player, EFTInfo.MainPlayer);

        public static bool IsPlayerMainPlayer(IAIDetails player) => player != null && EFTInfo.Compare(player, EFTInfo.MainPlayer);

        public static Player GetPlayer(BotOwner bot) => EFTInfo.GetPlayer(bot?.ProfileId);

        public static Player GetPlayer(IAIDetails person) => EFTInfo.GetPlayer(person?.ProfileId);

        public static Player GetPlayer(string profileID) => EFTInfo.GameWorld?.GetAlivePlayerByProfileID(profileID);

        public static bool Compare(IAIDetails A, IAIDetails B) => EFTInfo.Compare(A?.ProfileId, B?.ProfileId);

        public static bool Compare(Player A, IAIDetails B) => EFTInfo.Compare(A?.ProfileId, B?.ProfileId);

        public static bool Compare(IAIDetails A, Player B) => EFTInfo.Compare(A?.ProfileId, B?.ProfileId);

        public static bool Compare(Player A, Player B) => EFTInfo.Compare(A?.ProfileId, B?.ProfileId);

        public static bool Compare(Player A, string B) => EFTInfo.Compare(A?.ProfileId, B);

        public static bool Compare(string A, Player B) => EFTInfo.Compare(A, B);

        public static bool Compare(string A, string B) => A == B;

        public static GameWorld GameWorld => Singleton<GameWorld>.Instance;

        public static Player MainPlayer => EFTInfo.GameWorld?.MainPlayer;

        public static List<IAIDetails> AllPlayers => EFTInfo.GameWorld?.RegisteredPlayers;

        public static List<Player> AlivePlayers => EFTInfo.GameWorld?.AllAlivePlayersList;

        public static Dictionary<string, Player> AlivePlayersDictionary => EFTInfo.GameWorld?.allAlivePlayersByID;
    }
}