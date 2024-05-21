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
            _ => BotImpactType.DEFAULT
        };
        public static bool IsBossNerfExcluded (WildSpawnType type) => type switch {
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
                prone = 0.007f * num;
                crouch = 0.013f * num;
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
            Weapon weapon = item as Weapon;
            if (weapon == null)
                return result;

            foreach (var it in item.GetAllItems())
            {
                if (string.IsNullOrWhiteSpace(it?.TemplateId)) continue;
                ThatsLitCompat.ExtraDevices.TryGetValue(it.TemplateId, out var extraDevice);
                // Logger.LogWarning($"{it?.TemplateId} {extraDevice?.TemplateInstance?.name}");
                if (extraDevice != null && extraDevice.alwaysOn)
                {
                    result = ThatsLitCompat.DeviceMode.MergeMax(result, extraDevice.TemplateInstance?.SafeGetMode(0) ?? default);
                    continue;
                }

                LightComponent light = it.GetItemComponent<LightComponent>();
                if (light == null || !light.IsActive) continue;
                var mode = GetDeviceMode(it.TemplateId, light.SelectedMode);
                result = ThatsLitCompat.DeviceMode.MergeMax(result, mode);
            }

            return result;
        }

        internal static void DetermineShiningEquipments(Player player, out ThatsLitCompat.DeviceMode mode, out ThatsLitCompat.DeviceMode modeSub)
        {
            Weapon activeWeapon = player?.ActiveSlot?.ContainedItem as Weapon;
            mode = CheckDevicesOnItem(activeWeapon);
            modeSub = default;
         
            var inv = player?.ActiveSlot?.ContainedItem?.Owner as InventoryControllerClass;
            EquipmentClass equipment = inv?.Inventory?.Equipment;
            if (equipment == null) return;

            mode = ThatsLitCompat.DeviceMode.MergeMax(mode, CheckDevicesOnItem(equipment.GetSlot(EquipmentSlot.Headwear)?.ContainedItem));

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

            // GClass2550 544909bb4bdc2d6f028b4577 x item tactical_all_insight_anpeq15 2457 / V + IR + IRL / MODES: 4  V -> IR -> IRL -> IR+IRL
            // 560d657b4bdc2da74d8b4572 tactical_all_zenit_2p_kleh_vis_laser MODES: 3, F -> F+V -> V
            // GClass2550 56def37dd2720bec348b456a item tactical_all_surefire_x400_vis_laser 2457 F + V MDOES: 3: F -> F + V -> V
            // 57fd23e32459772d0805bcf1 item tactical_all_holosun_ls321 2457 V + IR + IRL MDOES 4: V -> IR -> IRL -> IRL + IR
            // 55818b164bdc2ddc698b456c tactical_all_zenit_2irs_kleh_lam MODES: 3 IRL -> IRL+IR -> IR
            // 5a7b483fe899ef0016170d15 tactical_all_surefire_xc1 MODES: 1
            // 5a800961159bd4315e3a1657 tactical_all_glock_gl_21_vis_lam MODES 3
            // 5b07dd285acfc4001754240d tactical_all_steiner_las_tac_2 Modes 1

            // "_id": "5b3a337e5acfc4704b4a19a0", "_name": "tactical_all_zenit_2u_kleh", 1
            //"_id": "5c06595c0db834001a66af6c", "_name": "tactical_all_insight_la5", 4, V -> IR -> IRL -> IRL+IR
            //"_id": "5c079ed60db834001a66b372", "_name": "tactical_tt_dlp_tactical_precision_laser_sight", 1
            //"_id": "5c5952732e2216398b5abda2", "_name": "tactical_all_zenit_perst_3", 4
            //"_id": "5cc9c20cd7f00c001336c65d", "_name": "tactical_all_ncstar_tactical_blue_laser", 1
            //"_id": "5d10b49bd7ad1a1a560708b0", "_name": "tactical_all_insight_anpeq2", 2
            //"_id": "5d2369418abbc306c62e0c80", "_name": "tactical_all_steiner_9021_dbal_pl", 6 / F -> V -> F+V -> IRF -> IR -> IRF+IR
            //"_id": "61605d88ffa6e502ac5e7eeb", "_name": "tactical_all_wilcox_raptar_es", 5 / RF -> V -> IR -> IRL -> IRL+IR
            //"_id": "626becf9582c3e319310b837", "_name": "tactical_all_insight_wmx200", 2
            //"_id": "6272370ee4013c5d7e31f418", "_name": "tactical_all_olight_baldr_pro", 3
            //"_id": "6272379924e29f06af4d5ecb", "_name": "tactical_all_olight_baldr_pro_tan", 3


            //"_id": "57d17c5e2459775a5c57d17d", "_name": "flashlight_ultrafire_WF-501B", 1 (2) (different slot)
            //"_id": "59d790f486f77403cb06aec6", "_name": "flashlight_armytek_predator_pro_v3_xhp35_hi", 1(2) (different slot)


            // "_id": "5bffcf7a0db83400232fea79", "_name": "pistolgrip_tt_pm_laser_tt_206", always on
        }
        static ThatsLitCompat.DeviceMode GetDeviceMode(string itemTemplateId, int selectedMode)
        {
            ThatsLitCompat.Devices.TryGetValue(itemTemplateId, out var compat);
            if (compat == null) return default;

            if (compat.TemplateInstance?.modes == null || compat.TemplateInstance.modes.Length <= selectedMode)
            {
                if (ThatsLitPlayer.IsDebugSampleFrame)
                {
                    string message = $"[That's Lit] Unknown device or mode: {itemTemplateId} {Singleton<ItemFactory>.Instance?.GetPresetItem(itemTemplateId)?.Name} mode {selectedMode}";
                    NotificationManagerClass.DisplayWarningNotification(message);
                    Logger.LogWarning(message);
                }
                return default;
            }
            return compat.TemplateInstance.modes[selectedMode];
        }

        static Dictionary<(string, int), (bool light, bool lightIsIR, bool laser, bool laserIsIR)> CustomLightAndLaser { get; set; }
        public static void RegisterCustomLightAndLaser (string templateId, int mode, bool light, bool lightIsIR, bool laser, bool laserIsIR)
        {
            if (CustomLightAndLaser == null) CustomLightAndLaser = new Dictionary<(string, int), (bool light, bool lightIsIR, bool laser, bool laserIsIR)>();
            CustomLightAndLaser.Add((templateId, mode), (light, lightIsIR, laser, laserIsIR));
        }
        static HashSet<string> CustomNightVisionScopes { get; set; }
        static Dictionary<string, float> CustomThermalScopes { get; set; }
        public static void RegisterCustomNightVisionScopes (string templateId)
        {
            if (CustomNightVisionScopes == null) CustomNightVisionScopes = new HashSet<string>();
            CustomNightVisionScopes.Add(templateId);
        }
        public static void RegisterCustomThermalScopes (string templateId, float effDis)
        {
            if (CustomThermalScopes == null) CustomThermalScopes = new Dictionary<string, float>();
            CustomThermalScopes.Add(templateId, effDis);
        }

        internal static void GUILayoutDrawAsymetricMeter(int level, bool alternative = false)
        {
            if (alternative)
            {
                if (level < -10)
                {
                    GUILayout.Label("  ##########|----------");
                    return;
                }
                if (level > 10)
                {
                    GUILayout.Label("  ----------|##########");
                    return;
                }
                switch (level)
                {
                    case -11:
                        GUILayout.Label("  ##########|----------");
                        break;
                    case -10:
                        GUILayout.Label("  ##########|----------");
                        break;
                    case -9:
                        GUILayout.Label("  -#########|----------");
                        break;
                    case -8:
                        GUILayout.Label("  --########|----------");
                        break;
                    case -7:
                        GUILayout.Label("  ---#######|----------");
                        break;
                    case -6:
                        GUILayout.Label("  ----######|----------");
                        break;
                    case -5:
                        GUILayout.Label("  -----#####|----------");
                        break;
                    case -4:
                        GUILayout.Label("  ------####|----------");
                        break;
                    case -3:
                        GUILayout.Label("  -------###|----------");
                        break;
                    case -2:
                        GUILayout.Label("  --------##|----------");
                        break;
                    case -1:
                        GUILayout.Label("  ---------#|----------");
                        break;
                    case 0:
                        GUILayout.Label("  ----------|----------");
                        break;
                    case 1:
                        GUILayout.Label("  ----------|#---------");
                        break;
                    case 2:
                        GUILayout.Label("  ----------|##--------");
                        break;
                    case 3:
                        GUILayout.Label("  ----------|###-------");
                        break;
                    case 4:
                        GUILayout.Label("  ----------|####------");
                        break;
                    case 5:
                        GUILayout.Label("  ----------|#####-----");
                        break;
                    case 6:
                        GUILayout.Label("  ----------|######----");
                        break;
                    case 7:
                        GUILayout.Label("  ----------|#######---");
                        break;
                    case 8:
                        GUILayout.Label("  ----------|########--");
                        break;
                    case 9:
                        GUILayout.Label("  ----------|#########-");
                        break;
                    case 10:
                        GUILayout.Label("  ----------|##########");
                        break;
                    case 11:
                        GUILayout.Label("  ----------|##########");
                        break;
                }
                return;
            }

            if (level < -10)
            {
                GUILayout.Label("  ▰▰▰▰▰▰▰▰▰▰ ▱▱▱▱▱▱▱▱▱▱");
                return;
            }
            if (level > 10)
            {
                GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▰▰");
                return;
            }
            switch (level)
            {
                case -11:
                    GUILayout.Label("  ▰▰▰▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱");
                    break;
                case -10:
                    GUILayout.Label("  ▰▰▰▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱");
                    break;
                case -9:
                    GUILayout.Label("  ▱▰▰▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱");
                    break;
                case -8:
                    GUILayout.Label("  ▱▱▰▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱");
                    break;
                case -7:
                    GUILayout.Label("  ▱▱▱▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱");
                    break;
                case -6:
                    GUILayout.Label("  ▱▱▱▱▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱");
                    break;
                case -5:
                    GUILayout.Label("  ▱▱▱▱▱▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱");
                    break;
                case -4:
                    GUILayout.Label("  ▱▱▱▱▱▱▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱");
                    break;
                case -3:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▰▰▰  ▱▱▱▱▱▱▱▱▱▱");
                    break;
                case -2:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▰▰  ▱▱▱▱▱▱▱▱▱▱");
                    break;
                case -1:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▰  ▱▱▱▱▱▱▱▱▱▱");
                    break;
                case 0:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▱▱▱▱▱▱▱▱▱▱");
                    break;
                case 1:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▱▱▱▱▱▱▱▱▱");
                    break;
                case 2:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▱▱▱▱▱▱▱▱");
                    break;
                case 3:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▱▱▱▱▱▱▱");
                    break;
                case 4:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▱▱▱▱▱▱");
                    break;
                case 5:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▱▱▱▱▱");
                    break;
                case 6:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▱▱▱▱");
                    break;
                case 7:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▱▱▱");
                    break;
                case 8:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▱▱");
                    break;
                case 9:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▰▱");
                    break;
                case 10:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▰▰");
                    break;
                case 11:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▰▰");
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

        internal static void GUILayoutFoliageMeter(int level, bool alternative = false)
        {
            if (alternative)
            {
                if (level <= 0)
                {
                    GUILayout.Label("  FOLIAGE  ----------|");
                    return;
                }
                if (level >= 10)
                {
                    GUILayout.Label("  FOLIAGE  ##########|");
                    return;
                }
                switch (level)
                {
                    case 1:
                        GUILayout.Label("  FOLIAGE  #---------|");
                        break;
                    case 2:
                        GUILayout.Label("  FOLIAGE  ##--------|");
                        break;
                    case 3:
                        GUILayout.Label("  FOLIAGE  ###-------|");
                        break;
                    case 4:
                        GUILayout.Label("  FOLIAGE  ####------|");
                        break;
                    case 5:
                        GUILayout.Label("  FOLIAGE  #####-----|");
                        break;
                    case 6:
                        GUILayout.Label("  FOLIAGE  ######----|");
                        break;
                    case 7:
                        GUILayout.Label("  FOLIAGE  #######---|");
                        break;
                    case 8:
                        GUILayout.Label("  FOLIAGE  ########--|");
                        break;
                    case 9:
                        GUILayout.Label("  FOLIAGE  #########-|");
                        break;
                }
            }

            if (level <= 0)
            {
                GUILayout.Label("  FOLIAGE  ▱▱▱▱▱▱▱▱▱▱");
                return;
            }
            if (level >= 10)
            {
                GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▰▰▰▰");
                return;
            }
            switch (level)
            {
                case 1:
                    GUILayout.Label("  FOLIAGE  ▰▱▱▱▱▱▱▱▱▱");
                    break;
                case 2:
                    GUILayout.Label("  FOLIAGE  ▰▰▱▱▱▱▱▱▱▱");
                    break;
                case 3:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▱▱▱▱▱▱▱");
                    break;
                case 4:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▱▱▱▱▱▱");
                    break;
                case 5:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▱▱▱▱▱");
                    break;
                case 6:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▱▱▱▱");
                    break;
                case 7:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▰▱▱▱");
                    break;
                case 8:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▰▰▱▱");
                    break;
                case 9:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▰▰▰▱");
                    break;
            }
        }

        internal static void GUILayoutTerrainMeter(int level, bool alternative = false)
        {
            if (alternative)
            {
                if (level <= 0)
                {
                    GUILayout.Label("  TERRAIN  ----------|");
                    return;
                }
                if (level >= 10)
                {
                    GUILayout.Label("  TERRAIN  ##########|");
                    return;
                }
                switch (level)
                {
                    case 1:
                        GUILayout.Label("  TERRAIN  #---------|");
                        break;
                    case 2:
                        GUILayout.Label("  TERRAIN  ##--------|");
                        break;
                    case 3:
                        GUILayout.Label("  TERRAIN  ###-------|");
                        break;
                    case 4:
                        GUILayout.Label("  TERRAIN  ####------|");
                        break;
                    case 5:
                        GUILayout.Label("  TERRAIN  #####-----|");
                        break;
                    case 6:
                        GUILayout.Label("  TERRAIN  ######----|");
                        break;
                    case 7:
                        GUILayout.Label("  TERRAIN  #######---|");
                        break;
                    case 8:
                        GUILayout.Label("  TERRAIN  ########--|");
                        break;
                    case 9:
                        GUILayout.Label("  TERRAIN  #########-|");
                        break;
                }
            }

            if (level <= 0)
            {
                GUILayout.Label("  TERRAIN  ▱▱▱▱▱▱▱▱▱▱");
                return;
            }
            if (level >= 10)
            {
                GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▰▰▰▰");
                return;
            }
            switch (level)
            {
                case 1:
                    GUILayout.Label("  TERRAIN  ▰▱▱▱▱▱▱▱▱▱");
                    break;
                case 2:
                    GUILayout.Label("  TERRAIN  ▰▰▱▱▱▱▱▱▱▱");
                    break;
                case 3:
                    GUILayout.Label("  TERRAIN  ▰▰▰▱▱▱▱▱▱▱");
                    break;
                case 4:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▱▱▱▱▱▱");
                    break;
                case 5:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▱▱▱▱▱");
                    break;
                case 6:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▱▱▱▱");
                    break;
                case 7:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▰▱▱▱");
                    break;
                case 8:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▰▰▱▱");
                    break;
                case 9:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▰▰▰▱");
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
    }
}