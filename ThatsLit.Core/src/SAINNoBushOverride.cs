using SPT.Reflection.Patching;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using EFT;
using System;

namespace ThatsLit.Patches.Vision
{
    public class SAINNoBushOverride : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(Type.GetType("SAIN.Components.SAINNoBushESP, SAIN"), "SetCanShoot", new Type[]{typeof(bool)});
        }

        
        [PatchPrefix]
        public static void PatchPrefix(ref bool blockShoot, ref BotOwner ___BotOwner)
        {
            if (!ThatsLitPlugin.InterruptSAINNoBush.Value)
                return;

            var enemy = ___BotOwner?.Memory?.GoalEnemy;
            if (enemy?.Person == null)
                return;

            float lastSeenPosDelta = (enemy.Person.Position - enemy.EnemyLastPositionReal).magnitude;
            float sinceSeen = Time.time - enemy.PersonalSeenTime;

            if (enemy.Distance > 100f && sinceSeen > 5f) return;

            ThatsLitPlayer player = null;
            Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(enemy.Person, out player);
            if (player == null) return;

            ThatsLitPlugin.swNoBushOverride.MaybeResume();

            var caution = ___BotOwner.Id % 10;
            Vector3 botVisionDir = ___BotOwner.GetPlayer.LookDirection;
            Vector3 eyeToPlayerBody = player.Player.MainParts[BodyPartType.body].Position - ___BotOwner.MainParts[BodyPartType.head].Position;
            var visionAngleDelta = Vector3.Angle(botVisionDir, eyeToPlayerBody);
            if (visionAngleDelta > 85f)
            {
                ThatsLitPlugin.swNoBushOverride.Stop();
                return;
            }

            var interruptChance = Mathf.InverseLerp(35f - caution * 2f, 10f - caution, enemy.Distance);
            interruptChance *= interruptChance * interruptChance;
            interruptChance = Mathf.Abs(interruptChance);
            interruptChance += Mathf.InverseLerp(15f - caution, 10f - caution, enemy.Distance) * 0.1f;
            interruptChance *= Mathf.InverseLerp(75f, 10f, visionAngleDelta);
            if (player.Player.IsInPronePose)
                interruptChance /= 2f;
            else
                interruptChance *= 0.2f + player.Player.PoseLevel;
            
            var interruptChanceSeen = 1f - 0.5f * Mathf.InverseLerp(5, 50f, enemy.Distance);
            if (interruptChance < interruptChanceSeen)
                interruptChance = Mathf.Lerp(interruptChance, interruptChanceSeen, 0.75f * Mathf.InverseLerp(5f, 0.3f, lastSeenPosDelta) + 0.9f * Mathf.InverseLerp(5f, 1f, sinceSeen));

            if (___BotOwner.GetPlayer?.HandsController is Player.FirearmController fc
             && fc.IsAiming
             && interruptChance < 0.75f)
                interruptChance = Mathf.Lerp(interruptChance, 0.75f, Mathf.InverseLerp(5f, 0f, visionAngleDelta) * Mathf.InverseLerp(100, 10f, enemy.Distance));
    
            if (player.DebugInfo != null)
            {
                player.DebugInfo.lastInterruptChance = interruptChance;
                player.DebugInfo.lastInterruptChanceDis = enemy.Distance;
            }

            if (player.DebugInfo != null)
                player.DebugInfo.attemptToCancelSAINNoBush++;

            if (UnityEngine.Random.Range(0f, 1f) < interruptChance)
            {
                blockShoot = false;
                if (player.DebugInfo != null)
                    player.DebugInfo.cancelledSAINNoBush++;
            }
            ThatsLitPlugin.swNoBushOverride.Stop();
        }
    }
}