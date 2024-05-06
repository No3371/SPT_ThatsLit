using Aki.Reflection.Patching;
using HarmonyLib;
using ThatsLit.Components;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using EFT;
using System;

namespace ThatsLit.Patches.Vision
{
    public class EncounteringPatch : ModulePatch
    {
        internal static System.Diagnostics.Stopwatch _benchmarkSW;
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EnemyInfo), nameof(EnemyInfo.SetVisible));
        }

        public struct State
        {
            public bool triggered;
            public bool unexpected;
            public bool botSprinting;
            public float visionDeviation;
        }

        [PatchPrefix]
        public static bool PatchPrefix(EnemyInfo __instance, bool value, ref State __state)
        {
            __state = default;
            if (!__instance.Person.IsYourPlayer) return true; // SKIP non-player.
            if (!value) return true; // SKIP. Only works when the player is set to be visible to the bot.
            if (__instance.IsVisible) return true; // SKIP. Only works when the bot hasn't see the player. IsVisible means the player is already seen.
            if (!ThatsLitPlugin.EnabledMod.Value || !ThatsLitPlugin.EnabledEncountering.Value) return true;

#region BENCHMARK
            if (ThatsLitPlugin.EnableBenchmark.Value && ThatsLitPlugin.DebugInfo.Value)
            {
                if (_benchmarkSW == null) _benchmarkSW = new System.Diagnostics.Stopwatch();
                if (_benchmarkSW.IsRunning)
                {
                    string message = $"[That's Lit] Benchmark stopwatch is not stopped!";
                    NotificationManagerClass.DisplayWarningNotification(message);
                    Logger.LogWarning(message);
                }
                _benchmarkSW.Start();
            }
            else if (_benchmarkSW != null)
                _benchmarkSW = null;
#endregion

            Vector3 look = __instance.Owner.GetPlayer.LookDirection;
            Vector3 to = __instance.Person.Position - __instance.Owner.Position;
            var angle = Vector3.Angle(look, to);

            float rand = UnityEngine.Random.Range(-1f, 1f);
            float rand2 = UnityEngine.Random.Range(-1f, 1f);

            // Vague hint instead, if the bot is facing away
            if (!Utility.IsBoss(__instance.Owner?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault))
            {
                if (angle > 75 && rand < Mathf.Clamp01(((angle - 75f) / 45f) * ThatsLitPlugin.VagueHintChance.Value))
                {
                    var vagueSource = __instance.Owner.Position + to * (0.75f + 0.25f * rand);
                    vagueSource += (Vector3.right * rand2 + Vector3.forward * (rand2 - rand) / 2f) * to.sqrMagnitude / (100f * (1.5f + rand));
                    __instance.Owner.Memory.Spotted(false, vagueSource);
                    return false; // Cancel visibllity (SetVisible does not only get called for the witness... ex: for group members )
                }
            }


            if (Time.time - __instance.PersonalSeenTime > 7f && (__instance.Person.Position - __instance.EnemyLastPosition).sqrMagnitude >= 36f)
            {
                if (Singleton<ThatsLitMainPlayerComponent>.Instance) Singleton<ThatsLitMainPlayerComponent>.Instance.encounter++;
                __state = new State()
                {
                    triggered = true,
                    unexpected = __instance.Owner.Memory.GoalEnemy != __instance || Time.time - __instance.TimeLastSeen > 30f + rand * 10f,
                    botSprinting = __instance.Owner?.Mover?.Sprinting ?? false,
                    visionDeviation = angle
                };
            }

#region BENCHMARK
            _benchmarkSW?.Stop();
#endregion

            return true;
        }
        [PatchPostfix]
        public static void PatchPostfix(EnemyInfo __instance, State __state)
        {
            if (!ThatsLitPlugin.EnabledMod.Value || !ThatsLitPlugin.EnabledEncountering.Value) return;
            if (!__state.triggered || __instance.Owner.Memory.GoalEnemy != __instance) return; // Not triggering the patch OR the bot is engaging others

            var aim = __instance.Owner.AimingData;
            if (aim == null) return;

#region BENCHMARK
            if (ThatsLitPlugin.EnableBenchmark.Value && ThatsLitPlugin.DebugInfo.Value)
            {
                if (_benchmarkSW == null) _benchmarkSW = new System.Diagnostics.Stopwatch();
                if (_benchmarkSW.IsRunning)
                {
                    string message = $"[That's Lit] Benchmark stopwatch is not stopped! (Encountering)";
                    NotificationManagerClass.DisplayWarningNotification(message);
                    Logger.LogWarning(message);
                }
                _benchmarkSW.Start();
            }
            else if (_benchmarkSW != null)
                _benchmarkSW = null;
#endregion

            if (__state.botSprinting)
            {
                // Force a ~0.45s delay
                __instance.Owner.AimingData.SetNextAimingDelay(
                    UnityEngine.Random.Range(0f, 0.45f)
                    * (__state.unexpected? 1f : 0.5f)
                    * Mathf.Clamp01(__state.visionDeviation/15f));

                // ~20% chance to force a miss
                if (UnityEngine.Random.Range(0f, 1f) < 0.2f  * (__state.unexpected? 1f : 0.5f) * Mathf.Clamp01(__state.visionDeviation/30f))
                    aim.NextShotMiss();
            }
            else if (__state.unexpected)
            {
                // Force a ~0.15s delay
                __instance.Owner.AimingData.SetNextAimingDelay(UnityEngine.Random.Range(0f, 0.15f) * Mathf.Clamp01(__state.visionDeviation/15f));
            }

#region BENCHMARK
            _benchmarkSW?.Stop();
#endregion
        }
    }
}