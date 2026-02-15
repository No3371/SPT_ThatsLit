using SPT.Reflection.Patching;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using EFT;

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

        [HarmonyAfter("me.sol.sain")]
        [PatchPrefix]
        public static bool PatchPrefix(EnemyInfo __instance, bool value, ref State __state)
        {
            __state = default;

            if (!value)
                return true; // SKIP. Only works when the player is set to be visible to the bot.
            if (__instance.IsVisible)
                return true; // SKIP. Only works when the bot hasn't see the player. IsVisible means the player is already seen.
            if (!ThatsLitPlugin.EnabledMod.Value || !ThatsLitPlugin.EnabledEncountering.Value)
                return true;
            if (ThatsLitPlugin.PMCOnlyMode.Value && !Utility.IsPMCSpawnType(__instance.Owner?.Profile?.Info?.Settings?.Role))
                return true;
            if (__instance.Owner == null)
                return true;
    
            ThatsLitPlayer player = null;
            Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(__instance.Person, out player);
            if (player == null)
                return true;

            ThatsLitPlugin.swEncountering.MaybeResume();

            Vector3 botPos = __instance.Owner.Position;
            Vector3 botLookDir = __instance.Owner.GetPlayer.LookDirection;
            Vector3 botEyeToPlayerBody = __instance.Person.MainParts[BodyPartType.body].Position - __instance.Owner.MainParts[BodyPartType.head].Position;
            Vector3 botEyeToLastSeenPos = __instance.EnemyLastPositionReal - __instance.Owner.MainParts[BodyPartType.head].Position;
            float distance = botEyeToPlayerBody.magnitude;
            var visionDeviation = Vector3.Angle(botLookDir, botEyeToPlayerBody);
            var visionDeviationToLast = Vector3.Angle(botEyeToPlayerBody, botEyeToLastSeenPos);
            if (!__instance.HaveSeen)
                visionDeviationToLast = 180f;

            float srand = UnityEngine.Random.Range(-1f, 1f);
            float srand2 = UnityEngine.Random.Range(-1f, 1f);
            float rand3 = UnityEngine.Random.Range(0, 1f); // Don't go underground
            float rand4 = UnityEngine.Random.Range(0, 1f);

            float sinceLastSeen = Time.time - __instance.PersonalSeenTime;
            float knownPosDelta = (__instance.Person.Position - __instance.EnemyLastPosition).magnitude;

            float GetSurpriseChanceInFront ()
            {
                // Handle situations where the player is shooting from afar
                // Prevent bots constantly get vague hints even if its facing the shooting player
                
                float cutoff = 0;
                if (player.lastShotVector != Vector3.zero && Time.time - player.lastShotTime < 0.5f)
                {
                    float shotAngleDelta = Vector3.Angle(-botEyeToPlayerBody, player.lastShotVector);
                    if (player.DebugInfo != null)
                        player.DebugInfo.lastEncounterShotAngleDelta = shotAngleDelta;
                    
                    cutoff = Mathf.InverseLerp(0.5f, 0f, Time.time - player.lastShotTime);
                    cutoff *= Mathf.InverseLerp(15f, 0f, shotAngleDelta);
                    if (player.DebugInfo != null) player.DebugInfo.lastEncounteringShotCutoff = cutoff;
                }
                 // Assuming surprise attack by the player, even not facing away
                return (0.5f * Mathf.InverseLerp(30f + 15f * rand3, 100f, sinceLastSeen) + 0.5f * Mathf.InverseLerp(0, 60f, visionDeviationToLast))
                     * Mathf.InverseLerp(10, 110f, distance)
                     * Mathf.InverseLerp(0f, 15f, visionDeviation)
                     * (1f - cutoff * (0.5f + 0.5f * rand4));
            }

            // Vague hint instead, if the bot is facing away
            BotImpactType botImpactType = Utility.GetBotImpactType(__instance.Owner?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault);
            if (botImpactType != BotImpactType.BOSS)
            {
                float vagueHintAngleFactor = Mathf.InverseLerp(0f, 3.5f, distance) * Mathf.InverseLerp(70f, 100f, visionDeviation); // When facing away, replace with vague hint
                vagueHintAngleFactor *= 1f + Mathf.InverseLerp(10f, 25f, botEyeToPlayerBody.y);
                if ((rand3 < vagueHintAngleFactor)
                 || rand3 < GetSurpriseChanceInFront())
                {
                    if (player.DebugInfo != null)
                        player.DebugInfo.vagueHint++;
                    var vagueSource = UnityEngine.Random.insideUnitSphere * 50f * Mathf.InverseLerp(3.5f, 100f, distance); //  ~50m deviation
                    if (vagueSource.y < 0)
                        vagueSource.y = 0;
                    vagueSource += __instance.Person.MainParts[BodyPartType.body].Position;
                    if (__instance.Owner?.Memory != null
                     && __instance.Owner?.Covers != null)
                    {
                        ThatsLitGameworld.SingleIdThrottler throttler;
                        Singleton<ThatsLitGameworld>.Instance.singleIdThrottlers.TryGetValue(__instance.Owner.ProfileId, out throttler);
                        if (Time.time - throttler.lastAddedDangerPoint > 1f)
                        {

                            if (player.DebugInfo != null)
                                player.DebugInfo.signalDanger++;
                            __instance.Owner?.DangerPointsData?.AddPointOfDanger(new PlaceForCheck(vagueSource, PlaceForCheckType.simple), true);
                            throttler.lastAddedDangerPoint = Time.time;
                            Singleton<ThatsLitGameworld>.Instance.singleIdThrottlers[__instance.Owner.ProfileId] = throttler;
                        }
                    }

                    if (__instance.Owner?.BotsGroup?.CoverPointMaster != null
                     && __instance.Owner?.Memory?.BotCurrentCoverInfo != null)
                    {
                        __instance?.Owner?.Memory?.Spotted(false, vagueSource);
                        ThatsLitPlugin.swEncountering.Stop();
                        return false; // Cancel visibllity (SetVisible does not only get called for direct vision... ex: for group members )
                    }
                    else
                        if (player.DebugInfo != null)
                            player.DebugInfo.vagueHintCancel++;
                }
            }

            if (player.DebugInfo != null)
                player.DebugInfo.encounter++;

            float delayAimChance = 0.5f * Mathf.InverseLerp(0, 9f + srand2 * 5f, sinceLastSeen) + 0.5f * Mathf.InverseLerp(0, 5f, knownPosDelta);
            if (__instance.Owner?.Memory?.GoalEnemy?.Person == player.Player as IPlayer)
                delayAimChance *= 0.65f;
            if (rand4 - 0.2f * Mathf.InverseLerp(0, 5, player.Player.Velocity.magnitude) < delayAimChance) // Busting into sight / out of cover
            {
                __state = new State()
                {
                    triggered = true,
                    unexpected = __instance.Owner?.Memory.GoalEnemy != __instance && sinceLastSeen > rand3 * 10f, // Bots can start search without visual so last seen time solely alone is unreliable
                    botSprinting = __instance.Owner?.Mover?.Sprinting ?? false,
                    visionDeviation = visionDeviation
                };
            }

            ThatsLitPlugin.swEncountering.Stop();

            return true;
        }
        // CalcGoalForBot could change the goalEnemy to the palyer in SetVisible()
        [PatchPostfix]
        [HarmonyAfter("me.sol.sain")]
        public static void PatchPostfix(EnemyInfo __instance, State __state)
        {
            if (!ThatsLitPlugin.EnabledMod.Value || !ThatsLitPlugin.EnabledEncountering.Value)
                return;
            if (!__state.triggered || __instance.Owner?.Memory?.GoalEnemy != __instance)
                return; // Not triggering the patch OR the bot is engaging others

            var aim = __instance.Owner?.AimingManager.CurrentAiming;
            if (aim == null)
                return;

            ThatsLitPlugin.swEncountering.MaybeResume();

            var caution = __instance.Owner.Id % 10;
            BotImpactType botImpactType = Utility.GetBotImpactType(__instance.Owner?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault);
            float rand = UnityEngine.Random.Range(0f, 1f);
            rand *= rand;
            float rand2 = UnityEngine.Random.Range(0f, 1f);
            rand2 *= rand2;
            if (__state.botSprinting)
            {
                // Force a ~0.45s delay
                aim.SetNextAimingDelay(
                    (caution * 0.01f + rand * (0.25f + caution * 0.01f))
                    * (__state.unexpected? 1f : 0.5f)
                    * (caution * 0.01f + Mathf.InverseLerp(0, 25, __state.visionDeviation))
                    * (botImpactType == BotImpactType.BOSS? 0.25f : botImpactType == BotImpactType.FOLLOWER? 0.5f : 1f));

                // ~30% chance to force a miss
                if (rand2 < 0.225f  * (__state.unexpected? 1f : 0.5f) * Mathf.InverseLerp(0, 30, __state.visionDeviation) + 0.2f * Mathf.InverseLerp(0, 5, __instance.Person?.Velocity.magnitude ?? 0))
                    aim.NextShotMiss();
            }
            else if (__state.unexpected)
            {
                // Force a ~0.15s delay
                aim.SetNextAimingDelay(
                    rand * (0.18f + caution * 0.01f)
                    * Mathf.InverseLerp(0, 25f, __state.visionDeviation)
                    * (botImpactType == BotImpactType.BOSS? 0.25f : botImpactType == BotImpactType.FOLLOWER? 0.5f : 1f));

                // ~40% chance to force a miss
                if (rand2 < 0.225f * Mathf.InverseLerp(0, 40f, __state.visionDeviation) + 0.2f * Mathf.InverseLerp(0, 5, __instance.Person?.Velocity.magnitude ?? 0))
                    aim.NextShotMiss();
            }

            ThatsLitPlugin.swEncountering.Stop();
        }
    }
}