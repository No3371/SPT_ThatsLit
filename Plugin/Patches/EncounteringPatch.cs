using Aki.Reflection.Patching;
using HarmonyLib;
using ThatsLit.Components;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using EFT;

namespace ThatsLit.Patches.Vision
{
    // public class SoundOverlapPatch : ModulePatch
    // {
    //     protected override MethodBase GetTargetMethod()
    //     {
    //         //HearingSensor
    //         return AccessTools.Method(typeof(GClass553), "method_6");
    //     }

    //     [PatchPostfix]
    //     public static void PatchPostfix(GClass553 __instance, ref bool __result, BotOwner ___botOwner_0, Vector3 position, float power)
    //     {
    //         if (__result)
    //         {
    //             if (power > Singleton<ThatsLitGameworldComponent>.Instance.powerSample) Singleton<ThatsLitGameworldComponent>.Instance.powerSample = power;
    //             if (Singleton<ThatsLitGameworldComponent>.Instance.BotLastHearingEvents.TryGetValue(___botOwner_0.Id, out var last))
    //             {
    //                 var rand = UnityEngine.Random.Range(0.05f, 0.25f);
    //                 if (Time.time - last.Item1 < rand && (rand - 0.05f) * 5f > power / last.Item2)
    //                 {
    //                     __result = false;
    //                     return;
    //                 }
    //                 if ((rand - 0.05f) * 2f > power)
    //                 {
    //                     __result = false;
    //                     return;
    //                 }
    //             }
    //             Vector3 vector3 = ___botOwner_0.Transform.position - position;
    //             float num = ___botOwner_0.Settings.Current.CurrentHearingSense * power / vector3.magnitude;
    //             Singleton<ThatsLitGameworldComponent>.Instance.BotLastHearingEvents[___botOwner_0.Id] = (Time.time, num);
    //         }
    //     }
    // }

    public class EncounteringPatch : ModulePatch
    {
        private static PropertyInfo _GoalEnemyProp;
        protected override MethodBase GetTargetMethod()
        {
            _GoalEnemyProp = AccessTools.Property(typeof(BotMemoryClass), "GoalEnemy");
            return AccessTools.Method(typeof(GClass478), nameof(GClass478.SetVisible));
        }

        public struct State
        {
            public bool triggered;
            public bool unexpected;
            public bool sprinting;
            public float angle;
        }

        [PatchPrefix]
        public static bool PatchPrefix(GClass478 __instance, bool value, ref State __state)
        {
            Vector3 from = __instance.Owner.Transform.rotation * Vector3.forward;
            Vector3 to = __instance.Person.Transform.position - __instance.Owner.Transform.position;
            var angle = Vector3.Angle(from, to);
            if (__instance.Person.IsYourPlayer && !__instance.IsVisible && value  && !__instance.Owner.Boss.IamBoss)
            {
                if (angle > 75 && UnityEngine.Random.Range(0f, 1f) < Mathf.Clamp01((angle - 75f) / 25f) * 0.75f) return false; // Cancel visible when facing away (SetVisible not only get called for the witness... ex: for group members )
            }
            if (__instance.Person.IsYourPlayer && value && Time.time - __instance.PersonalSeenTime > 10f && (!__instance.HaveSeen || (__instance.Person.Position - __instance.EnemyLastPosition).sqrMagnitude >= 49f))
            {
                Singleton<ThatsLitMainPlayerComponent>.Instance.encounter++;
                __state = new State () { triggered = true, unexpected = __instance.Owner.Memory.GoalEnemy != __instance || Time.time - __instance.TimeLastSeen > 30f * UnityEngine.Random.Range(1, 2f), sprinting = __instance.Owner.Mover.Sprinting, angle = angle };
            }
            return true;
        }
        [PatchPostfix]
        public static void PatchPostfix(GClass478 __instance, State __state)
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
                if (__instance.Owner.AimingData is GClass463 g463 && !__instance.Owner.WeaponManager.ShootController.IsAiming)
                {
                    g463.ScatteringData.CurScatering += __instance.Owner.Settings.Current.CurrentMaxScatter * UnityEngine.Random.Range(0f, 0.35f) * Mathf.Clamp01((__state.angle - 15f)/45f);
                }
            }
        }
    }
}