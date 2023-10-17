using Aki.Reflection.Patching;
using HarmonyLib;
using ThatsLit.Components;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using EFT;

namespace ThatsLit.Patches.Vision
{
    public class EncounteringPatch : ModulePatch
    {
        private static PropertyInfo _GoalEnemyProp;
        protected override MethodBase GetTargetMethod()
        {
            _GoalEnemyProp = AccessTools.Property(typeof(BotMemoryClass), "GoalEnemy");
            return AccessTools.Method(typeof(EnemyInfo), nameof(EnemyInfo.SetVisible));
        }

        public struct State
        {
            public bool triggered;
            public bool unexpected;
            public bool sprinting;
            public float angle;
        }

        [PatchPrefix]
        public static bool PatchPrefix(EnemyInfo __instance, bool value, ref State __state)
        {
            Vector3 from = __instance.Owner.Transform.rotation * Vector3.forward;
            Vector3 to = __instance.Person.Transform.position - __instance.Owner.Transform.position;
            var angle = Vector3.Angle(from, to);
            if (__instance.Person.IsYourPlayer && !__instance.IsVisible && value  && !__instance.Owner.Boss.IamBoss)
            {
                float rand = UnityEngine.Random.Range(-1f, 1f);
                float rand2 = UnityEngine.Random.Range(-1f, 1f);
                if (angle > 75 && rand < Mathf.Clamp01(((angle - 75f) / 25f) * ThatsLitPlugin.VagueHintChance.Value))
                {
                    var source = __instance.Owner.Position + (__instance.Person.Position - __instance.Owner.Position) * (0.75f + rand / 4f);
                    source += (Vector3.up * rand  + Vector3.right * rand2 + Vector3.forward * (rand2 - rand) / 2f) * to.sqrMagnitude / (100f * (1.5f + rand));
                    __instance.Owner.Memory.Spotted(false, source);
                    return false; // Cancel visible when facing away (SetVisible not only get called for the witness... ex: for group members )
                }
            }
            if (__instance.Person.IsYourPlayer && value && Time.time - __instance.PersonalSeenTime > 10f && (!__instance.HaveSeen || (__instance.Person.Position - __instance.EnemyLastPosition).sqrMagnitude >= 49f))
            {
                Singleton<ThatsLitMainPlayerComponent>.Instance.encounter++;
                __state = new State () { triggered = true, unexpected = __instance.Owner.Memory.GoalEnemy != __instance || Time.time - __instance.TimeLastSeen > 30f * UnityEngine.Random.Range(1, 2f), sprinting = __instance.Owner.Mover.Sprinting, angle = angle };
            }
            return true;
        }
        [PatchPostfix]
        public static void PatchPostfix(EnemyInfo __instance, State __state)
        {
            var aim = __instance.Owner.AimingData;
            if (aim == null) return;
            if (__state.triggered && __instance.Owner.Memory.GoalEnemy == __instance)
            {
                if (__state.sprinting)
                {
                    __instance.Owner.AimingData.SetNextAimingDelay(UnityEngine.Random.Range(0f, 0.6f) * (__state.unexpected? 1f : 0.5f) * Mathf.Clamp01(__state.angle/15f));
                    if (UnityEngine.Random.Range(0f, 1f) < 0.2f  * (__state.unexpected? 1f : 0.5f) * Mathf.Clamp01(__state.angle/15f)) aim.NextShotMiss();
                }
                else if (__state.unexpected)
                {
                    __instance.Owner.AimingData.SetNextAimingDelay(UnityEngine.Random.Range(0f, 0.25f) * Mathf.Clamp01(__state.angle/15f));
                }
                if (__instance.Owner.AimingData is GClass388 g388 && !__instance.Owner.WeaponManager.ShootController.IsAiming)
                {
                    g388.ScatteringData.CurScatering += __instance.Owner.Settings.Current.CurrentMaxScatter * UnityEngine.Random.Range(0f, 0.35f) * Mathf.Clamp01((__state.angle - 15f)/45f);
                }
            }
        }
    }
}