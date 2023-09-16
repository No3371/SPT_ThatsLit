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
            ThatsLitMainPlayerComponent mainPlayer = Singleton<ThatsLitMainPlayerComponent>.Instance;
            if (Time.frameCount != lastFrame)
            {
                lastFrame = Time.frameCount;
                closetLastFrame = float.MaxValue;
                mainPlayer.calcedLastFrame = 0;
            }

            if (__result == 8888 || ThatsLitPlugin.DisableEffect.Value) return;

            Vector3 to = enemy.position - BotTransform.position;
            var dis = to.magnitude;
            var disFactor = Mathf.Clamp01((dis - 10) / 100f);
            disFactor = disFactor * disFactor * 0.5f; // A slow accelerating curve

            var poseFactor = __instance.Person.AIData.Player.PoseLevel / __instance.Person.AIData.Player.Physical.MaxPoseLevel;
            if (UnityEngine.Random.Range(0f, 1f) < 0.01f * disFactor / poseFactor)
            {
                __result *= 10;
                return;
            }

            if (EFTInfo.IsPlayerMainPlayer(__instance.Person))
            {
                if (!mainPlayer) return;
                if (mainPlayer.disableVisionPatch) return;

                var score = mainPlayer.multiFrameLitScore; // -1 ~ 1
                var factor = Mathf.Pow(score, ThatsLitMainPlayerComponent.POWER); // -1 ~ 1, the graph is basically flat when the score is between ~0.3 and 0.3



                bool foundCloser = false;
                if (dis < closetLastFrame)
                {
                    closetLastFrame = dis;
                    foundCloser = true;
                }
                // Maybe randomly lose vision
                if (UnityEngine.Random.Range(0f, 1f) < disFactor
                 || UnityEngine.Random.Range(0f, 1f) < mainPlayer.bushScore * disFactor // Among bushes, from afar
                 || UnityEngine.Random.Range(0f, 1f) < mainPlayer.grassScore * (1.05f - poseFactor) * disFactor) // Among grasses, low pose, from afar
                {
                    if (!__instance.Owner.WeaponManager.ShootController.IsAiming || UnityEngine.Random.Range(0f, 1f) > 0.85f)
                    {
                        __result = 8888f;
                        return;
                    }
                }

                if (factor < 0 && __instance.Owner.NightVision.UsingNow)
                    if (factor < -disFactor * 2) factor /= 2f; // ex: at 110m away, change nothing, but at ~10m away, 0.66x the factor

                //if (factor < 0 && (__instance.Person.AIData.UsingLight || __instance.Person.AIData.GetFlare)) factor /= 5f; // Moved to score calculation

                if (__instance.Person.AIData.Player.IsInPronePose)
                {
                    if (factor < 0f) factor *= 1 + disFactor / 2f; // Darkness will be more effective from afar
                    else if (factor > 0f) factor /= 1 + disFactor / 2f; // Highlight will be less effective from afar
                }

                if (factor < -0.3f) factor *= 1 + disFactor; // Darkness will be more effective from afar
                else if (factor > 0.25f) factor /= 1 + disFactor; // Highlight will be less effective from afar
                factor = Mathf.Clamp(factor, -0.95f, 0.95f);

                if (Time.frameCount % 30 == 0 && foundCloser) mainPlayer.lastCalcFrom = __result;

                var baseOffsetNegative = (Mathf.Pow(Mathf.Abs(factor), 2)) * Mathf.Sign(factor) * UnityEngine.Random.Range(0.5f, 1f);
                baseOffsetNegative *= factor < 0 ? 1 : 0.75f; // Give positive factor a lower impact because
                __result -= baseOffsetNegative;

                if (factor < 0 && UnityEngine.Random.Range(-1, 0) > factor) __result = 8888f;
                else if (factor > 0 && UnityEngine.Random.Range(0, 1) < factor) __result *= (1f - factor * 0.5f * ThatsLitPlugin.ImpactScale.Value); // Make it so even at 100% it only reduce half of the time
                else if (factor < -0.9f) __result *= 1 - (factor * 3f * ThatsLitPlugin.ImpactScale.Value);
                else if (factor < -0.5f) __result *= 1 - (factor * 2f * ThatsLitPlugin.ImpactScale.Value);
                else if (factor < -0.2f) __result *= 1 - factor * ThatsLitPlugin.ImpactScale.Value;
                else if (factor < 0f) __result *= 1 - factor / 2f * ThatsLitPlugin.ImpactScale.Value;
                else if (factor > 0f) __result /= (1 + factor / 2f * ThatsLitPlugin.ImpactScale.Value);

                // Make it closer to 1 second if it's dark enough
                if (factor < -0.9f && __result < 1) __result = (__result + 1f) / 2f;
                else if (factor < -0.7f) __result = (__result + 1f) / 3f;
                else if (factor < -0.5f) __result = (__result + 1f) / 4f;

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