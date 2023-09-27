using Aki.Reflection.Patching;
using HarmonyLib;
using ThatsLit.Components;
using System.Reflection;
using UnityEngine;
using Comfort.Common;

namespace ThatsLit.Patches.Vision
{
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
        }

        [PatchPrefix]
        public static bool PatchPrefix(GClass478 __instance, bool value, ref State __state)
        {
            if (__instance.Person.IsYourPlayer && value && Time.time - __instance.PersonalSeenTime > 10f)
            {
                __state = new State () { triggered = true, unexpected = __instance.Owner.Memory.GoalEnemy != __instance || Time.time - __instance.TimeLastSeen > 30f * UnityEngine.Random.Range(1, 2f), sprinting = __instance.Owner.Mover.Sprinting };
            }
            return true;
        }
        [PatchPostfix]
        public static void PatchPostfix(GClass478 __instance, State __state)
        {
            var aim = __instance.Owner.AimingData;
            if (aim == null) return;
            if (__state.triggered) Singleton<ThatsLitMainPlayerComponent>.Instance.seen++;
            if (__state.triggered && __instance.Owner.Memory.GoalEnemy == __instance)
            {
                if (__state.sprinting)
                {
                    __instance.Owner.AimingData.SetNextAimingDelay(UnityEngine.Random.Range(0.2f, 1f) * (__state.unexpected? 1f : 0.5f));
                    if (UnityEngine.Random.Range(0f, 1f) < 0.2f  * (__state.unexpected? 1f : 0.5f)) aim.NextShotMiss();
                }
                else if (__state.unexpected)
                {
                    __instance.Owner.AimingData.SetNextAimingDelay(UnityEngine.Random.Range(0f, 0.25f));
                }
                if (__instance.Owner.AimingData is GClass463 g463)
                {
                    Vector3 from = __instance.Owner.Transform.rotation * Vector3.forward;
                    Vector3 to = __instance.Person.Transform.position - __instance.Owner.Transform.position;
                    var angle = Vector3.Angle(from, to);
                    g463.ScatteringData.CurScatering += __instance.Owner.Settings.Current.CurrentMaxScatter * UnityEngine.Random.Range(0f, 0.35f) * Mathf.Clamp01((angle - 15f)/45f);
                }
            }
        }
    }
}