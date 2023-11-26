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
            __state = default;
            if (!__instance.Person.IsYourPlayer || !value || !__instance.IsVisible || !ThatsLitPlugin.EnabledMod.Value || !ThatsLitPlugin.EnabledEncountering.Value) return true;
            Vector3 look = __instance.Owner.GetPlayer.LookDirection;
            Vector3 to = __instance.Person.Position - __instance.Owner.Position;
            var angle = Vector3.Angle(look, to);

            // Vague hint instead, if the bot is facing away
            if (!Utility.IsBoss(__instance.Owner?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault))
            {
                float rand = UnityEngine.Random.Range(-1f, 1f);
                float rand2 = UnityEngine.Random.Range(-1f, 1f);
                if (angle > 75 && rand < Mathf.Clamp01(((angle - 75f) / 45f) * ThatsLitPlugin.VagueHintChance.Value))
                {
                    var vagueSource = __instance.Owner.Position + to * (0.75f + rand / 4f);
                    vagueSource += (Vector3.up * Mathf.Abs(rand + rand2) / 2f  + Vector3.right * rand2 + Vector3.forward * (rand2 - rand) / 2f) * to.sqrMagnitude / (100f * (1.5f + rand));
                    __instance.Owner.Memory.Spotted(false, vagueSource);
                    return false; // Cancel visible when facing away (SetVisible not only get called for the witness... ex: for group members )
                }
            }


            if (Time.time - __instance.PersonalSeenTime > 10f && (__instance.Person.Position - __instance.EnemyLastPosition).sqrMagnitude >= 49f)
            {
                if (Singleton<ThatsLitMainPlayerComponent>.Instance) Singleton<ThatsLitMainPlayerComponent>.Instance.encounter++;
                __state = new State () { triggered = true, unexpected = __instance.Owner.Memory.GoalEnemy != __instance || Time.time - __instance.TimeLastSeen > 45f * UnityEngine.Random.Range(1, 2f), sprinting = __instance.Owner?.Mover?.Sprinting ?? false, angle = angle };
            }
            return true;
        }
        [PatchPostfix]
        public static void PatchPostfix(EnemyInfo __instance, State __state)
        {
            if (!ThatsLitPlugin.EnabledMod.Value || !ThatsLitPlugin.EnabledEncountering.Value) return;
            if (!__state.triggered || __instance.Owner.Memory.GoalEnemy != __instance) return;

            var aim = __instance.Owner.AimingData;
            if (aim == null) return;

            if (__state.sprinting)
            {
                __instance.Owner.AimingData.SetNextAimingDelay(UnityEngine.Random.Range(0f, 0.45f) * (__state.unexpected? 1f : 0.5f) * Mathf.Clamp01(__state.angle/15f));
                if (UnityEngine.Random.Range(0f, 1f) < 0.2f  * (__state.unexpected? 1f : 0.5f) * Mathf.Clamp01(__state.angle/30f)) aim.NextShotMiss();
            }
            else if (__state.unexpected)
            {
                __instance.Owner.AimingData.SetNextAimingDelay(UnityEngine.Random.Range(0f, 0.15f) * Mathf.Clamp01(__state.angle/15f));
            }

            if (aim is GClass388 g388 && !__instance.Owner.WeaponManager.ShootController.IsAiming)
            {
                g388.ScatteringData.CurScatering += __instance.Owner.Settings.Current.CurrentMaxScatter * UnityEngine.Random.Range(0f, 0.15f) * Mathf.Clamp01((__state.angle - 30f)/45f);
            }
        }
    }
}