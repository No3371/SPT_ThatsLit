using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace ThatsLit
{
    public enum BotImpactType
    {
        BOSS,
        FOLLOWER,
        DEFAULT
    }
    public static class Utility
    {
        
        public static BotImpactType GetBotImpactType (WildSpawnType type) => type switch {
            WildSpawnType.bossBoar => BotImpactType.BOSS,
            WildSpawnType.bossBoarSniper => BotImpactType.BOSS,
            WildSpawnType.bossBully => BotImpactType.BOSS,
            WildSpawnType.bossGluhar => BotImpactType.BOSS,
            WildSpawnType.bossKilla => BotImpactType.BOSS,
            WildSpawnType.bossKnight => BotImpactType.BOSS,
            WildSpawnType.bossKojaniy => BotImpactType.BOSS,
            WildSpawnType.bossSanitar => BotImpactType.BOSS,
            WildSpawnType.bossTagilla => BotImpactType.BOSS,
            WildSpawnType.bossZryachiy => BotImpactType.BOSS,
            WildSpawnType.ravangeZryachiyEvent => BotImpactType.BOSS,
            WildSpawnType.peacefullZryachiyEvent => BotImpactType.BOSS,
            WildSpawnType.bossKolontay => BotImpactType.BOSS,
            WildSpawnType.arenaFighter => BotImpactType.FOLLOWER,
            WildSpawnType.arenaFighterEvent => BotImpactType.FOLLOWER,
            WildSpawnType.followerGluharScout => BotImpactType.FOLLOWER,
            WildSpawnType.followerGluharAssault => BotImpactType.FOLLOWER,
            WildSpawnType.followerGluharSecurity => BotImpactType.FOLLOWER,
            WildSpawnType.followerGluharSnipe => BotImpactType.FOLLOWER,
            WildSpawnType.followerBigPipe => BotImpactType.FOLLOWER,
            WildSpawnType.followerBirdEye => BotImpactType.FOLLOWER,
            WildSpawnType.followerBoar => BotImpactType.FOLLOWER,
            WildSpawnType.followerBoarClose1 => BotImpactType.FOLLOWER,
            WildSpawnType.followerBoarClose2 => BotImpactType.FOLLOWER,
            WildSpawnType.followerBully => BotImpactType.FOLLOWER,
            WildSpawnType.followerKojaniy => BotImpactType.FOLLOWER,
            WildSpawnType.followerSanitar => BotImpactType.FOLLOWER,
            WildSpawnType.followerTagilla => BotImpactType.FOLLOWER,
            WildSpawnType.followerZryachiy => BotImpactType.FOLLOWER,
            WildSpawnType.followerKolontayAssault => BotImpactType.FOLLOWER,
            WildSpawnType.followerKolontaySecurity => BotImpactType.FOLLOWER,
            WildSpawnType.sectantPriest => BotImpactType.BOSS,
            WildSpawnType.sectantWarrior => BotImpactType.FOLLOWER,
            WildSpawnType.sectactPriestEvent => BotImpactType.BOSS,
            WildSpawnType.gifter => BotImpactType.BOSS,
            WildSpawnType.pmcBot => BotImpactType.FOLLOWER,
            WildSpawnType.exUsec => BotImpactType.FOLLOWER,
            WildSpawnType.crazyAssaultEvent => BotImpactType.FOLLOWER,
            WildSpawnType.marksman => BotImpactType.FOLLOWER, // scav_sniper
            _ => (int)type switch {
                199 => BotImpactType.BOSS,
                _ => BotImpactType.DEFAULT
            }
        };
        public static bool IsExcludedSpawnType (WildSpawnType type) => type switch {
            // WildSpawnType.bossTagilla => true,
            // WildSpawnType.followerTagilla => true,
            _ => false
        };

        internal static float GetInGameDayTime()
        {
            if (Singleton<GameWorld>.Instance?.GameDateTime == null) return 19f;

            var GameDateTime = Singleton<GameWorld>.Instance.GameDateTime.Calculate();

            float minutes = GameDateTime.Minute / 59f;
            return GameDateTime.Hour + minutes;
        }

        internal static float GetNightProgress()
        {
            var time = GetInGameDayTime();
            if (time < 12) time += 4; // 20 => 0, 24 => 4, 0 => 4, 6 => 10 
            return Mathf.InverseLerp(0f, 10f, time);
        }
        internal static float GetDayProgress()
        {
            var time = GetInGameDayTime();
            return Mathf.InverseLerp(6f, 20f, time);
        }

        static string lastLogged;
        internal static void CalculateDetailScore (string name, int num, out float prone, out float crouch)
        {
            prone = 0;
            crouch = 0;
            if (name == null) return; // Somehow happens on raid start?

            if (num == 0)
            {
                prone = 0;
                crouch = 0;
                return;
            }

            if (name.EndsWith("e2eb60")) // Main grasses in Woods, half of crouching char tall
            {
                prone = 0.05f * Mathf.Pow(Mathf.Clamp01(num / 20f), 2) * Mathf.Min(num, 50); // Needs cluster
                crouch = 0.003f * Mathf.Min(num, 10);
            }
            else if (name.EndsWith("df6e82")
                  || name.EndsWith("7c58e7")
                  || name.EndsWith("994963")
            )
            {
                prone = 0.05f * Mathf.Pow(Mathf.Clamp01(num / 20f), 2) * Mathf.Min(num, 50); // Needs cluster
                crouch = 0.0035f * Mathf.Min(num, 5);
            }
            else if (name.EndsWith("27bbce")) // Grass_new_3_D_27bbce, shorter and smaller, cross shape
            {
                prone = 0.008f * Mathf.Min(num, 25);
                crouch = 0;
            }
            else if (name.EndsWith("fa097b") // Grass_new_2_D_fa097b, denser and slightly bigger grass cluster
                  || name.EndsWith("2adee9")) // low res, bigger yellower grass cross
            {
                prone = 0.06f * num; 
                crouch = 0.009f * Mathf.Min(num, 15);
            }
            else if (name.EndsWith("eb7931")) // brown, dense, somewhat tall
            {
                prone = 0.06f * num; 
                crouch = 0.01f * num;
            }
            else if (name.EndsWith("adb33a")) // wheat like
            {
                prone = 0.02f * num;
                crouch = 0.02f * num;
            }
            else if (name.EndsWith("f83e15")) // tall white grass
            {
                prone = 0.04f * num;
                crouch = 0.018f * num;
            }
            else if (name.EndsWith("ead4fa")) // with little white flowers
            {
                prone = 0.06f * num;
                crouch = 0.008f * num;
            }
            else if (name.EndsWith("40d9d4"))
            {
                prone = 0.007f * num;
                crouch = 0.009f * num;
            }
            else if (name.EndsWith("4ad690")) // tall and somewhat thick
            {
                prone = 0.015f * num;
                crouch = 0.015f * num;
            }
            else if (name.EndsWith("bf0a23"))
            {
                prone = 0.007f * num;
                crouch = 0.007f * num;
            }
            else if (name.EndsWith("b6cf18"))
            {
                prone = 0.01f * num;
                crouch = 0.01f * num;
            }
            else if (name.EndsWith("a84c21"))
            {
                prone = 0.007f * num;
                crouch = 0.006f * num;
            }
            else if (name.EndsWith("d17f80"))  // sharp gray short grass
            {
                prone = 0.008f * Mathf.Min(num, 25);
                crouch = 0;
            }
            else if (name.EndsWith("d17180")) {} // Flat grass paste (found in Custom)
            else if (name.EndsWith("e84f39")) {} // super tiny little grass
            else if (name.EndsWith("e9cd39")) {} // rock
            else if (ThatsLitPlugin.DebugInfo.Value)
            {
                if (Time.frameCount % 47 == 0 && name != lastLogged)
                {
                    string message = string.Format("That's Lit: Missing terrain detail: {0}", name);
                    NotificationManagerClass.DisplayWarningNotification(message);
                    Logger.LogWarning(message);
                    lastLogged = name;
                }
            }


                // I REALLY DONT WANT TO CALL SUBSTRING HERE
                // switch (string.Intern(name.Substring(name.Length - 6, 6)))
                // {
                //     case "e2eb60": // Grass_new_1_D_e2eb60, normal grass, 8~12
                //     case "df6e82": // Grass_02_512_df6e82
                //     case "7c58e7": // Grass5_512_D_7c58e7
                //     // case "!vertexlit_rock_e9cd39":
                //     case "994963": // _Grass3_D_994963
                //         prone = 0.05f * Mathf.Pow(Mathf.Clamp01(num / 10f), 2) * num; // Needs cluster
                //         crouch = 0.005f * num;
                //         break;
                //     case "27bbce": // Grass_new_3_D_27bbce, shorter and smaller, cross shape
                //         prone = 0.008f * num;
                //         crouch = 0;
                //         break;
                //     case "fa097b": // Grass_new_2_D_fa097b, denser and slightly bigger grass cluster
                //         prone = 0.06f * num; 
                //         crouch = 0.01f * num;
                //         break;
                //     case "eb7931": // Grass_2_roma_eb7931, brown, dense, somewhat tall
                //         prone = 0.07f * num; 
                //         crouch = 0.02f * num;
                //         break;
                //     case "adb33a": // Grass6_D_adb33a, wheat like
                //         prone = 0.02f * num;
                //         crouch = 0.02f * num;
                //         break;
                //     case "f83e15": // _T_WhitGrass_A_f83e15, tall white grass
                //         prone = 0.04f * num;
                //         crouch = 0.03f * num;
                //         break;
                //     case "ead4fa": // Field_grass_D_ead4fa, with little white flowers
                //         prone = 0.06f * num;
                //         crouch = 0.008f * num;
                //         break;
                //     case "40d9d4": // Grass2_D_40d9d4, thin, tall, wheat
                //         prone = 0.007f * num;
                //         crouch = 0.009f * num;
                //         break;
                //     case "a84c21": // Grass5_D_a84c21, shorter wheat like
                //         prone = 0.007f * num;
                //         crouch = 0.006f * num;
                //         break;
                //     case "4ad690": // grass11_4ad690
                //     case "bf0a23": // Grass4_D_bf0a23, reed like, thin and tall
                //         prone = 0.007f * num;
                //         crouch = 0.007f * num;
                //         break;
                //     case "b6cf18": // _T_KrapivaLittle_A_b6cf18, tall, green
                //         prone = 0.01f * num;
                //         crouch = 0.01f * num;
                //         break;
                //     default:
                //         return;
                // }
            // }
            
        }
        static ThatsLitCompat.DeviceMode CheckDevicesOnItem(Item item)
        {
            ThatsLitCompat.DeviceMode result = default;
            if (item == null)
                return result;

            foreach (var it in item.GetAllItems())
            {
                if (string.IsNullOrWhiteSpace(it?.TemplateId)) continue;
                if (ThatsLitCompat.ExtraDevices.TryGetValue(it.TemplateId, out var extraDevice)
                 && extraDevice?.TemplateInstance != null)
                {
                    if (it is SightsItemClass sightMod)
                    {
                        result = ThatsLitCompat.DeviceMode.MergeMax(result, extraDevice.TemplateInstance.SafeGetMode(sightMod.Sight?.SelectedScopeMode ?? 0));
                    }
                    else
                        result = ThatsLitCompat.DeviceMode.MergeMax(result, extraDevice.TemplateInstance.SafeGetMode(0));
                    continue;
                }

                LightComponent light = it.GetItemComponent<LightComponent>();
                if (light == null || !light.IsActive) continue;
                var mode = GetDeviceMode(it.TemplateId, light.SelectedMode);
                result = ThatsLitCompat.DeviceMode.MergeMax(result, mode);
            }

            return result;
        }

        internal static (ThatsLitCompat.DeviceMode mode, ThatsLitCompat.DeviceMode modeSub) DetermineShiningEquipments(Player player)
        {
            ThatsLitCompat.DeviceMode mode, modeSub;
            Weapon activeWeapon = player?.ActiveSlot?.ContainedItem as Weapon;
            mode = CheckDevicesOnItem(activeWeapon);
            modeSub = default;
         
            InventoryEquipment equipment = player.Inventory?.Equipment;
            if (equipment == null)
                return (mode, modeSub);

            mode = ThatsLitCompat.DeviceMode.MergeMax(mode, CheckDevicesOnItem(equipment.GetSlot(EquipmentSlot.Headwear)?.ContainedItem));

            // If not ActiveWeapon (which is already checked on top)
            if (player?.ActiveSlot != equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon))
            {
                modeSub = ThatsLitCompat.DeviceMode.MergeMax(modeSub, CheckDevicesOnItem(equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem));
            }


            if (player?.ActiveSlot != equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon))
            {
                modeSub = ThatsLitCompat.DeviceMode.MergeMax(modeSub, CheckDevicesOnItem(equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem));
            }


            if (player?.ActiveSlot != equipment.GetSlot(EquipmentSlot.Holster))
            {
                modeSub = ThatsLitCompat.DeviceMode.MergeMax(modeSub, CheckDevicesOnItem(equipment.GetSlot(EquipmentSlot.Holster)?.ContainedItem));
            }

            return (mode, modeSub);
        }
        static ThatsLitCompat.DeviceMode GetDeviceMode(string itemTemplateId, int selectedMode)
        {
            ThatsLitCompat.Devices.TryGetValue(itemTemplateId, out var compat);
            if (compat == null) return default;

            if (compat.TemplateInstance?.modes == null || compat.TemplateInstance.modes.Length <= selectedMode)
            {
                if (ThatsLitPlayer.IsDebugSampleFrame)
                {
                    string message = $"[That's Lit] Unknown device or mode: {itemTemplateId} {Singleton<ItemFactoryClass>.Instance?.GetPresetItem(itemTemplateId)?.Name} mode {selectedMode}";
                    NotificationManagerClass.DisplayWarningNotification(message);
                    Logger.LogWarning(message);
                    EFT.UI.ConsoleScreen.Log(message);
                }
                return default;
            }
            return compat.TemplateInstance.modes[selectedMode];
        }

        public static void GUILayoutDrawAsymetricMeter(int level, bool alternative = false, GUIStyle style = null)
        {
            if (style == null) style = GUI.skin.label;
            if (alternative)
            {
                if (level < -10)
                {
                    GUILayout.Label("  ##########|----------", style);
                    return;
                }
                if (level > 10)
                {
                    GUILayout.Label("  ----------|##########", style);
                    return;
                }
                switch (level)
                {
                    case -11:
                        GUILayout.Label("  ##########|----------", style);
                        break;
                    case -10:
                        GUILayout.Label("  ##########|----------", style);
                        break;
                    case -9:
                        GUILayout.Label("  -#########|----------", style);
                        break;
                    case -8:
                        GUILayout.Label("  --########|----------", style);
                        break;
                    case -7:
                        GUILayout.Label("  ---#######|----------", style);
                        break;
                    case -6:
                        GUILayout.Label("  ----######|----------", style);
                        break;
                    case -5:
                        GUILayout.Label("  -----#####|----------", style);
                        break;
                    case -4:
                        GUILayout.Label("  ------####|----------", style);
                        break;
                    case -3:
                        GUILayout.Label("  -------###|----------", style);
                        break;
                    case -2:
                        GUILayout.Label("  --------##|----------", style);
                        break;
                    case -1:
                        GUILayout.Label("  ---------#|----------", style);
                        break;
                    case 0:
                        GUILayout.Label("  ----------|----------", style);
                        break;
                    case 1:
                        GUILayout.Label("  ----------|#---------", style);
                        break;
                    case 2:
                        GUILayout.Label("  ----------|##--------", style);
                        break;
                    case 3:
                        GUILayout.Label("  ----------|###-------", style);
                        break;
                    case 4:
                        GUILayout.Label("  ----------|####------", style);
                        break;
                    case 5:
                        GUILayout.Label("  ----------|#####-----", style);
                        break;
                    case 6:
                        GUILayout.Label("  ----------|######----", style);
                        break;
                    case 7:
                        GUILayout.Label("  ----------|#######---", style);
                        break;
                    case 8:
                        GUILayout.Label("  ----------|########--", style);
                        break;
                    case 9:
                        GUILayout.Label("  ----------|#########-", style);
                        break;
                    case 10:
                        GUILayout.Label("  ----------|##########", style);
                        break;
                    case 11:
                        GUILayout.Label("  ----------|##########", style);
                        break;
                }
                return;
            }

            if (level < -10)
            {
                GUILayout.Label("  ▰▰▰▰▰▰▰▰▰▰ ▱▱▱▱▱▱▱▱▱▱", style);
                return;
            }
            if (level > 10)
            {
                GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▰▰", style);
                return;
            }
            switch (level)
            {
                case -11:
                    GUILayout.Label("  ▰▰▰▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -10:
                    GUILayout.Label("  ▰▰▰▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -9:
                    GUILayout.Label("  ▱▰▰▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -8:
                    GUILayout.Label("  ▱▱▰▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -7:
                    GUILayout.Label("  ▱▱▱▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -6:
                    GUILayout.Label("  ▱▱▱▱▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -5:
                    GUILayout.Label("  ▱▱▱▱▱▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -4:
                    GUILayout.Label("  ▱▱▱▱▱▱▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -3:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -2:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -1:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case 0:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case 1:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▱▱▱▱▱▱▱▱▱", style);
                    break;
                case 2:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▱▱▱▱▱▱▱▱", style);
                    break;
                case 3:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▱▱▱▱▱▱▱", style);
                    break;
                case 4:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▱▱▱▱▱▱", style);
                    break;
                case 5:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▱▱▱▱▱", style);
                    break;
                case 6:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▱▱▱▱", style);
                    break;
                case 7:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▱▱▱", style);
                    break;
                case 8:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▱▱", style);
                    break;
                case 9:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▰▱", style);
                    break;
                case 10:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▰▰", style);
                    break;
                case 11:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▰▰", style);
                    break;
            }
        }

        internal static void RightAlignedGUILabel (string str)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(str);
            }
        }

        internal static void GUILayoutFoliageMeter(int level, bool alternative = false, GUIStyle style= null)
        {
            if (style == null) style = GUI.skin.label;
            if (alternative)
            {
                if (level <= 0)
                {
                    GUILayout.Label("  FOLIAGE  ----------|", style);
                    return;
                }
                if (level >= 10)
                {
                    GUILayout.Label("  FOLIAGE  ##########|", style);
                    return;
                }
                switch (level)
                {
                    case 1:
                        GUILayout.Label("  FOLIAGE  #---------|", style);
                        break;
                    case 2:
                        GUILayout.Label("  FOLIAGE  ##--------|", style);
                        break;
                    case 3:
                        GUILayout.Label("  FOLIAGE  ###-------|", style);
                        break;
                    case 4:
                        GUILayout.Label("  FOLIAGE  ####------|", style);
                        break;
                    case 5:
                        GUILayout.Label("  FOLIAGE  #####-----|", style);
                        break;
                    case 6:
                        GUILayout.Label("  FOLIAGE  ######----|", style);
                        break;
                    case 7:
                        GUILayout.Label("  FOLIAGE  #######---|", style);
                        break;
                    case 8:
                        GUILayout.Label("  FOLIAGE  ########--|", style);
                        break;
                    case 9:
                        GUILayout.Label("  FOLIAGE  #########-|", style);
                        break;
                }
            }

            if (level <= 0)
            {
                GUILayout.Label("  FOLIAGE  ▱▱▱▱▱▱▱▱▱▱", style);
                return;
            }
            if (level >= 10)
            {
                GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▰▰▰▰", style);
                return;
            }
            switch (level)
            {
                case 1:
                    GUILayout.Label("  FOLIAGE  ▰▱▱▱▱▱▱▱▱▱", style);
                    break;
                case 2:
                    GUILayout.Label("  FOLIAGE  ▰▰▱▱▱▱▱▱▱▱", style);
                    break;
                case 3:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▱▱▱▱▱▱▱", style);
                    break;
                case 4:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▱▱▱▱▱▱", style);
                    break;
                case 5:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▱▱▱▱▱", style);
                    break;
                case 6:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▱▱▱▱", style);
                    break;
                case 7:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▰▱▱▱", style);
                    break;
                case 8:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▰▰▱▱", style);
                    break;
                case 9:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▰▰▰▱", style);
                    break;
            }
        }

        internal static void GUILayoutTerrainMeter(int level, bool alternative = false, GUIStyle style = null)
        {
            if (style == null) style = GUI.skin.label;
            if (alternative)
            {
                if (level <= 0)
                {
                    GUILayout.Label("  TERRAIN  ----------|", style);
                    return;
                }
                if (level >= 10)
                {
                    GUILayout.Label("  TERRAIN  ##########|", style);
                    return;
                }
                switch (level)
                {
                    case 1:
                        GUILayout.Label("  TERRAIN  #---------|", style);
                        break;
                    case 2:
                        GUILayout.Label("  TERRAIN  ##--------|", style);
                        break;
                    case 3:
                        GUILayout.Label("  TERRAIN  ###-------|", style);
                        break;
                    case 4:
                        GUILayout.Label("  TERRAIN  ####------|", style);
                        break;
                    case 5:
                        GUILayout.Label("  TERRAIN  #####-----|", style);
                        break;
                    case 6:
                        GUILayout.Label("  TERRAIN  ######----|", style);
                        break;
                    case 7:
                        GUILayout.Label("  TERRAIN  #######---|", style);
                        break;
                    case 8:
                        GUILayout.Label("  TERRAIN  ########--|", style);
                        break;
                    case 9:
                        GUILayout.Label("  TERRAIN  #########-|", style);
                        break;
                }
            }

            if (level <= 0)
            {
                GUILayout.Label("  TERRAIN  ▱▱▱▱▱▱▱▱▱▱", style);
                return;
            }
            if (level >= 10)
            {
                GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▰▰▰▰", style);
                return;
            }
            switch (level)
            {
                case 1:
                    GUILayout.Label("  TERRAIN  ▰▱▱▱▱▱▱▱▱▱", style);
                    break;
                case 2:
                    GUILayout.Label("  TERRAIN  ▰▰▱▱▱▱▱▱▱▱", style);
                    break;
                case 3:
                    GUILayout.Label("  TERRAIN  ▰▰▰▱▱▱▱▱▱▱", style);
                    break;
                case 4:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▱▱▱▱▱▱", style);
                    break;
                case 5:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▱▱▱▱▱", style);
                    break;
                case 6:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▱▱▱▱", style);
                    break;
                case 7:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▰▱▱▱", style);
                    break;
                case 8:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▰▰▱▱", style);
                    break;
                case 9:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▰▰▰▱", style);
                    break;
            }
        }

        public static string DetermineDir (Vector3 dir)
        {
            var dirFlat = (new Vector2 (dir.x, dir.z)).normalized;
            var angle = Vector2.SignedAngle(Vector2.up, dirFlat);
            if (angle >= -22.5f && angle <= 22.5f)
            {
                return "N";
            }
            else if (angle >= 22.5f && angle <= 67.5f)
            {
                return "NE";
            }
            else if (angle >= 67.5f && angle <= 112.5f)
            {
                return "E";
            }
            else if (angle >= 112.5f && angle <= 157.5f)
            {
                return "SE";
            }
            else if (angle >= 157.5f && angle <= 180f || angle >= -180f && angle <= -157.5f)
            {
                return "S";
            }
            else if (angle >= -157.5f && angle <= -112.5f)
            {
                return "SW";
            }
            else if (angle >= -112.5f && angle <= -67.5f)
            {
                return "W";
            }
            else if (angle >= -67.5f && angle <= -22.5f)
            {
                return "NW";
            }
            else return "?";
        }
        internal static T ExpensiveCopyComponent<T>(T original, GameObject destination, System.Reflection.BindingFlags bindingFlags) where T : Component
        {
            System.Type type = original.GetType();
            Component copy = destination.AddComponent(type);
            System.Reflection.FieldInfo[] fields = type.GetFields(bindingFlags);
            foreach (System.Reflection.FieldInfo field in fields)
            {
                field.SetValue(copy, field.GetValue(original));
            }
            return copy as T;
        }

        internal static float GetPoseFactor(float currentPoseLevel, float maxPoseLevel, bool isInPronePose)
        {
            var pPoseFactor = currentPoseLevel / maxPoseLevel * 0.6f + 0.4f; // crouch: 0.4f
            if (isInPronePose) pPoseFactor -= 0.4f; // prone: 0
            pPoseFactor += 0.05f; // base -> prone -> 0.05f, crouch -> 0.45f (Prevent devide by zero)
            pPoseFactor = Mathf.Clamp01(pPoseFactor);
            return pPoseFactor;
        }

        internal static float GetPoseWeightedRegularTerrainScore(float pPoseFactor, TerrainDetailScore terrainScore)
        {
            return terrainScore.regular / (1f + 0.35f * Mathf.InverseLerp(0.45f, 1f, pPoseFactor));
        }

        internal static float GetObservedTerrainDetailScoreProne (Player player, Vector3 observerEyePos, Vector3 observerLookDir, float base3x3TerrainScore)
        {
            // Calculating cutoff from lying on slopes
            var playerSurfaceNormal = player.MovementContext.SurfaceNormal;
            System.Collections.Generic.Dictionary<BodyPartType, EnemyPart> playerParts = player.MainParts;
            Vector3 playerLegPos = (playerParts[BodyPartType.leftLeg].Position + playerParts[BodyPartType.rightLeg].Position) / 2f;
            var playerLegToHead = playerParts[BodyPartType.head].Position - playerLegPos;
            var playerLegToBotEye = observerEyePos - playerLegPos;
            var playerDirInView = Vector3.ProjectOnPlane(playerLegToHead, -playerLegToBotEye);
            // projected length = 1.0x original => perpendicular (most visible)
            var sizeInViewFactor = playerDirInView.magnitude / playerLegToHead.magnitude;
            // BUT this means players lying flat which should get covers could be considered very visible (lying horizontally in view)
            
            // bot -> ↖ player (opposite side of ground, must ignore) #1
            // bot -> ↘ player (opposite side of ground, must ignore) #2
            // bot -> ↗ player
            // bot -> ↙ player
            // <-player->
            var facingAngle = Vector3.Angle(playerLegToHead, playerLegToBotEye);
            if (facingAngle > 90f) sizeInViewFactor *= Mathf.InverseLerp(180f, 90f, facingAngle);
            else sizeInViewFactor *= Mathf.InverseLerp(0, 90f, facingAngle);

            var surfaceNormalAngle = Vector3.Angle(playerSurfaceNormal, playerLegToBotEye);
            sizeInViewFactor *= Mathf.InverseLerp(0f, 90f, surfaceNormalAngle);

            return base3x3TerrainScore * sizeInViewFactor;
        }

        internal static bool IsPMCSpawnType (WildSpawnType? spawnType)
        {
            return spawnType != null && spawnType == WildSpawnType.pmcBEAR || spawnType == WildSpawnType.pmcUSEC;
        }
    }
}