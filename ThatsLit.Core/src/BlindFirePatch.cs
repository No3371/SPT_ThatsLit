using System.Reflection;
using UnityEngine;
using EFT;
using System.Linq;
using SPT.Reflection.Patching;
using SPT.Reflection.Utils;

namespace ThatsLit.Patches.Vision
{
    public class BlindFirePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return PatchConstants.EftTypes.First(t => t.GetProperty("LastSpreadCount") != null
                                                   && t.GetProperty("LastAimTime")     != null
                                                   && t.GetProperty("HardAim")    != null)
                   .GetMethod("get_EndTargetPoint");
        }

        [PatchPostfix]
        public static void Postfix (ref BotOwner ___botOwner_0, ref BifacialTransform ___bifacialTransform_0, ref Vector3 __result)
        {
            if (!ThatsLitPlugin.ForceBlindFireScatter.Value) return;
            ThatsLitPlugin.swBlindFireScatter.MaybeResume();
            if (___botOwner_0.GetPlayer == null
             ||(___botOwner_0.GetPlayer.HandsController as Player.FirearmController)?.Blindfire != true)
            {
                ThatsLitPlugin.swBlindFireScatter.Stop();
                return;
            }
            float dis = Vector3.Distance(__result, ___botOwner_0.GetPlayer.Position);

            __result += UnityEngine.Random.insideUnitSphere * 5 * Mathf.InverseLerp(15f, 200f, dis);
            ThatsLitPlugin.swBlindFireScatter.Stop();
        }
    }
}