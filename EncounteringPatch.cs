using Aki.Reflection.Patching;
using HarmonyLib;
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

            if (!value) return true; // SKIP. Only works when the player is set to be visible to the bot.
            if (__instance.IsVisible) return true; // SKIP. Only works when the bot hasn't see the player. IsVisible means the player is already seen.
            if (!ThatsLitPlugin.EnabledMod.Value || !ThatsLitPlugin.EnabledEncountering.Value) return true;
    
            ThatsLitMainPlayerComponent player = null;
            Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(__instance.Person, out player);
            if (player == null) return true;

            ThatsLitPlugin.swEncountering.MaybeResume();

            Vector3 botLookDir = __instance.Owner.GetPlayer.LookDirection;
            Vector3 botEyeToPlayerBody = __instance.Person.MainParts[BodyPartType.body].Position - __instance.Owner.MainParts[BodyPartType.head].Position;
            var visionDeviation = Vector3.Angle(botLookDir, botEyeToPlayerBody);

            
            float srand = UnityEngine.Random.Range(-1f, 1f);
            float srand2 = UnityEngine.Random.Range(-1f, 1f);
            float rand3 = UnityEngine.Random.Range(0, 1f); // Don't go underground
            float rand4 = UnityEngine.Random.Range(0, 1f);

            // Vague hint instead, if the bot is facing away
            BotImpactType botImpactType = Utility.GetBotImpactType(__instance.Owner?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault);
            if (botImpactType != BotImpactType.BOSS)
            {
                float angleRating = Mathf.Clamp01((visionDeviation - 65f) / 55f); // 0-1 from 70 to 115+deg
                if (rand3 < angleRating * Mathf.Clamp01(ThatsLitPlugin.VagueHintChance.Value)) // EX: At 115deg 60% chance to replace with vague hint
                {
                    var vagueSource = __instance.Owner.Position + botEyeToPlayerBody * (1f + 0.2f * srand); //  +-20% distance
                    vagueSource += Vector3.Cross(botEyeToPlayerBody, Vector3.up).normalized * srand2 * botEyeToPlayerBody.magnitude /3f;
                    vagueSource += Vector3.up * rand3 * botEyeToPlayerBody.magnitude /3f;
                    __instance.Owner.Memory.Spotted(false, vagueSource);
                    return false; // Cancel visibllity (SetVisible does not only get called for the witness... ex: for group members )
                }
            }

            if (player.DebugInfo != null) player.DebugInfo.encounter++;

            float sinceSeen = Time.time - __instance.PersonalSeenTime;
            Vector3 posDelta = __instance.Person.Position - __instance.EnemyLastPosition;

            if (rand4 - 0.35f * Mathf.InverseLerp(0, 5, player.Player.Velocity.magnitude) < 0.5f * Mathf.InverseLerp(0, 10f + srand2 * 5f, sinceSeen) + 0.5f * Mathf.InverseLerp(0, 10f, posDelta.magnitude))
            {
                __state = new State()
                {
                    triggered = true,
                    unexpected = __instance.Owner.Memory.GoalEnemy != __instance && sinceSeen > rand3 * 10f, // Bots can start search without visual so last seen time solely alone is unreliable
                    botSprinting = __instance.Owner?.Mover?.Sprinting ?? false,
                    visionDeviation = visionDeviation
                };
            }

            ThatsLitPlugin.swEncountering.Stop();

            return true;
        }
        [PatchPostfix]
        public static void PatchPostfix(EnemyInfo __instance, State __state)
        {
            if (!ThatsLitPlugin.EnabledMod.Value || !ThatsLitPlugin.EnabledEncountering.Value) return;
            if (!__state.triggered || __instance.Owner.Memory.GoalEnemy != __instance) return; // Not triggering the patch OR the bot is engaging others

            var aim = __instance.Owner.AimingData;
            if (aim == null) return;

            ThatsLitPlugin.swEncountering.MaybeResume();

            float rand = UnityEngine.Random.Range(0f, 1f);
            float rand2 = UnityEngine.Random.Range(0f, 1f);
            if (__state.botSprinting)
            {
                // Force a ~0.45s delay
                __instance.Owner.AimingData.SetNextAimingDelay(
                    rand * 0.45f
                    * (__state.unexpected? 1f : 0.5f)
                    * (0.05f + Mathf.InverseLerp(0, 15, __state.visionDeviation)));

                // ~30% chance to force a miss
                if (rand2 < 0.2f  * (__state.unexpected? 1f : 0.5f) * Mathf.InverseLerp(0, 30, __state.visionDeviation) + 0.2f * Mathf.InverseLerp(0, 5, __instance.Person?.Velocity.magnitude ?? 0))
                    aim.NextShotMiss();
            }
            else if (__state.unexpected)
            {
                // Force a ~0.15s delay
                __instance.Owner.AimingData.SetNextAimingDelay(rand * 0.15f * Mathf.InverseLerp(0, 15, __state.visionDeviation));

                // ~40% chance to force a miss
                if (rand2 < 0.2f * Mathf.InverseLerp(0, 35, __state.visionDeviation) + 0.2f * Mathf.InverseLerp(0, 5, __instance.Person?.Velocity.magnitude ?? 0))
                    aim.NextShotMiss();
            }

            ThatsLitPlugin.swEncountering.Stop();
        }
    }
}