using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace ThatsLit.Components
{
    public static class Utility
    {
        internal static float GetInGameDayTime()
        {
            if (Singleton<GameWorld>.Instance?.GameDateTime == null) return 19f;

            var GameDateTime = Singleton<GameWorld>.Instance.GameDateTime.Calculate();

            float minutes = GameDateTime.Minute / 59f;
            return GameDateTime.Hour + minutes;
        }

        internal static IEnumerable<T> FindComponents<T> (Item topLevelItem) where T: class, IItemComponent
        {
            foreach (var it in topLevelItem.GetAllItems())
            {
                yield return it.GetItemComponent<T>();
            }
        }

        internal static void DetermineShiningEquipments(Player player, out bool vLight, out bool vLaser, out bool irLight, out bool irLaser, out bool vLightSub, out bool vLaserSub, out bool irLightSub, out bool irLaserSub)
        {
            vLight = vLaser = irLight = irLaser = vLightSub = vLaserSub = irLightSub = irLaserSub = false;
            if (player?.ActiveSlot?.ContainedItem != null)
            {
                Weapon weapon = player.ActiveSlot.ContainedItem as Weapon;
                if (weapon != null)
                foreach (var light in FindComponents<LightComponent>(weapon))
                {
                    if (!light.IsActive) continue;
                    MapComponentsModes(light.Item.TemplateId, light.SelectedMode, out bool thisLight, out bool thisLaser, out bool thisLightIsIR, out bool thisLaserIsIR);
                    if (thisLight && !thisLightIsIR) vLight = true;
                    if (thisLight && thisLightIsIR) irLight = true;
                    if (thisLaser && !thisLaserIsIR) vLaser = true;
                    if (thisLaser && thisLaserIsIR) irLaser = true;
                    if (vLight) return; // Early exit for main visible light because that's enough to decrease score
                }
            }

            var inv = player?.ActiveSlot?.ContainedItem?.Owner as InventoryControllerClass;

            if (inv == null) return;

            var helmet = inv?.Inventory?.Equipment?.GetSlot(EquipmentSlot.Headwear)?.ContainedItem as GClass2537;

            if (helmet != null)
            {
                foreach (var light in FindComponents<LightComponent>(helmet))
                {
                    if (!light.IsActive) continue;
                    MapComponentsModes(light.Item.TemplateId, light.SelectedMode, out bool thisLight, out bool thisLaser, out bool thisLightIsIR, out bool thisLaserIsIR);
                    if (thisLight && !thisLightIsIR) vLight = true;
                    if (thisLight && thisLightIsIR) irLight = true;
                    if (thisLaser && !thisLaserIsIR) vLaser = true;
                    if (thisLaser && thisLaserIsIR) irLaser = true;
                    if (vLight) return; // Early exit for main visible light because that's enough to decrease score
                }
            }

            var holstered = inv?.Inventory?.Equipment?.GetSlot(EquipmentSlot.Holster)?.ContainedItem;
            if (holstered != null)
            {
                Weapon weapon = holstered as Weapon;
                if (weapon != null) 
                foreach (var light in FindComponents<LightComponent>(weapon))
                {
                    if (!light.IsActive) continue;
                    MapComponentsModes(light.Item.TemplateId, light.SelectedMode, out bool thisLight, out bool thisLaser, out bool thisLightIsIR, out bool thisLaserIsIR);
                    if (thisLight && !thisLightIsIR) vLight = true;
                    if (thisLight && thisLightIsIR) irLight = true;
                    if (thisLaser && !thisLaserIsIR) vLaser = true;
                    if (thisLaser && thisLaserIsIR) irLaser = true;
                    if (vLight) return; // Early exit for main visible light because that's enough to decrease score
                }
            }

            var secondary = inv?.Inventory?.Equipment?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem;
            if (secondary != null)
            {
                Weapon weapon = secondary as Weapon;
                if (weapon != null) 
                foreach (var light in FindComponents<LightComponent>(weapon))
                {
                    if (!light.IsActive) continue;
                    MapComponentsModes(light.Item.TemplateId, light.SelectedMode, out bool thisLight, out bool thisLaser, out bool thisLightIsIR, out bool thisLaserIsIR);
                    if (thisLight && !thisLightIsIR) vLight = true;
                    if (thisLight && thisLightIsIR) irLight = true;
                    if (thisLaser && !thisLaserIsIR) vLaser = true;
                    if (thisLaser && thisLaserIsIR) irLaser = true;
                    if (vLight) return; // Early exit for main visible light because that's enough to decrease score
                }
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
        static void MapComponentsModes(string templateId, int selectedMode, out bool light, out bool laser, out bool lightIsIR, out bool laserIsIR)
        {
            light = laser = laserIsIR = lightIsIR = false;

            switch (templateId)
            {
                case "544909bb4bdc2d6f028b4577": // tactical_all_insight_anpeq15
                case "57fd23e32459772d0805bcf1": // tactical_all_holosun_ls321
                case "5c06595c0db834001a66af6c": // tactical_all_insight_la5
                case "5c5952732e2216398b5abda2": // tactical_all_zenit_perst_3
                    switch (selectedMode)
                    {
                        case 0:
                            laser = true;
                            break;
                        case 1:
                            laser = laserIsIR = true;
                            break;
                        case 2:
                            light = lightIsIR = true;
                            break;
                        case 3:
                            laser = laserIsIR = light = lightIsIR = true;
                            break;
                    }
                    break;
                case "61605d88ffa6e502ac5e7eeb": // tactical_all_wilcox_raptar_es
                    switch (selectedMode)
                    {
                        case 1:
                            laser = true;
                            break;
                        case 2:
                            laser = laserIsIR = true;
                            break;
                        case 3:
                            light = lightIsIR = true;
                            break;
                        case 4:
                            laser = laserIsIR = light = lightIsIR = true;
                            break;
                    }
                    break;
                case "560d657b4bdc2da74d8b4572": // tactical_all_zenit_2p_kleh_vis_laser
                case "56def37dd2720bec348b456a": // tactical_all_surefire_x400_vis_laser
                case "5a800961159bd4315e3a1657": // tactical_all_glock_gl_21_vis_lam
                case "6272379924e29f06af4d5ecb": // tactical_all_olight_baldr_pro_tan
                case "6272370ee4013c5d7e31f418": // tactical_all_olight_baldr_pro
                    switch (selectedMode)
                    {
                        case 0:
                            light = true;
                            break;
                        case 1:
                            laser = light = true;
                            break;
                        case 2:
                            laser = true;
                            break;
                    }
                    break;
                case "55818b164bdc2ddc698b456c": // tactical_all_zenit_2irs_kleh_lam
                    switch (selectedMode)
                    {
                        case 0:
                            light = lightIsIR = true;
                            break;
                        case 1:
                            laser = laserIsIR = light = lightIsIR = true;
                            break;
                        case 2:
                            laser = laserIsIR = true;
                            break;
                    }
                    break;
                case "5a7b483fe899ef0016170d15": // tactical_all_surefire_xc1
                case "5b3a337e5acfc4704b4a19a0": // tactical_all_zenit_2u_kleh
                case "59d790f486f77403cb06aec6": // flashlight_armytek_predator_pro_v3_xhp35_hi
                case "57d17c5e2459775a5c57d17d": // flashlight_ultrafire_WF
                    light = true;
                    break;
                case "5b07dd285acfc4001754240d": // tactical_all_steiner_las_tac_2
                case "5c079ed60db834001a66b372": // tactical_tt_dlp_tactical_precision_laser_sight
                case "5cc9c20cd7f00c001336c65d": // tactical_all_ncstar_tactical_blue_laser
                case "5bffcf7a0db83400232fea79": // pistolgrip_tt_pm_laser_tt_206
                    laser = true;
                    break;
                case "5d10b49bd7ad1a1a560708b0": // tactical_all_insight_anpeq2
                    switch (selectedMode)
                    {
                        case 0:
                            laser = laserIsIR = true;
                            break;
                        case 1:
                            laser = laserIsIR = light = lightIsIR = true;
                            break;
                        case 2:
                            break;
                    }
                    break;
                case "5d2369418abbc306c62e0c80": // tactical_all_steiner_9021_dbal_pl
                    switch (selectedMode)
                    {
                        case 0:
                            light = true;
                            break;
                        case 1:
                            laser = true;
                            break;
                        case 2:
                            laser = light = true;
                            break;
                        case 3:
                            light = lightIsIR = true;
                            break;
                        case 4:
                            laser = laserIsIR = true;
                            break;
                        case 5:
                            light = lightIsIR = laser = laserIsIR = true;
                            break;
                    }
                    break;
                case "626becf9582c3e319310b837": // tactical_all_insight_wmx200
                    switch (selectedMode)
                    {
                        case 0:
                            light = true;
                            break;
                        case 1:
                            light = lightIsIR = true;
                            break;
                    }
                    break;
            }
        }
        internal static void GUILayoutDrawAsymetricMeter(int level)
        {
            if (level < -10)
            {
                GUILayout.Label("＋＋＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                return;
            }
            if (level > 10)
            {
                GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋＋＋");
                return;
            }
            switch (level)
            {
                case -11:
                    GUILayout.Label("＋＋＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -10:
                    GUILayout.Label("＋＋＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -9:
                    GUILayout.Label("－＋＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -8:
                    GUILayout.Label("－－＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -7:
                    GUILayout.Label("－－－＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -6:
                    GUILayout.Label("－－－－＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -5:
                    GUILayout.Label("－－－－－＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -4:
                    GUILayout.Label("－－－－－－＋＋＋＋|－－－－－－－－－－");
                    break;
                case -3:
                    GUILayout.Label("－－－－－－－＋＋＋|－－－－－－－－－－");
                    break;
                case -2:
                    GUILayout.Label("－－－－－－－－＋＋|－－－－－－－－－－");
                    break;
                case -1:
                    GUILayout.Label("－－－－－－－－－＋|－－－－－－－－－－");
                    break;
                case 0:
                    GUILayout.Label("－－－－－－－－－－|－－－－－－－－－－");
                    break;
                case 1:
                    GUILayout.Label("－－－－－－－－－－|＋－－－－－－－－－");
                    break;
                case 2:
                    GUILayout.Label("－－－－－－－－－－|＋＋－－－－－－－－");
                    break;
                case 3:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋－－－－－－－");
                    break;
                case 4:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋－－－－－－");
                    break;
                case 5:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋－－－－－");
                    break;
                case 6:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋－－－－");
                    break;
                case 7:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋－－－");
                    break;
                case 8:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋－－");
                    break;
                case 9:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋＋－");
                    break;
                case 10:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋＋＋");
                    break;
                case 11:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋＋＋");
                    break;
            }
        }
    }
}