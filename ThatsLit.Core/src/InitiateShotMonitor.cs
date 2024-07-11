using SPT.Reflection.Patching;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using EFT;

namespace ThatsLit.Patches.Vision
{
    public class InitiateShotMonitor : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.InitiateShot));
        }

        
        [PatchPostfix]
        public static void PatchPostfix(Player.FirearmController __instance, Vector3 shotDirection, ref Player ____player)
        {
            ThatsLitPlayer player = null;
            Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(____player, out player);
            if (player == null) return;

            player.lastShotVector = shotDirection;
            player.lastShotTime = Time.time;
        }
    }
}