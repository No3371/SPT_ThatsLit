using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using DrakiaXYZ.VersionChecker;
using ThatsLit.Components;
using ThatsLit.Helpers;
using System;
using UnityEngine;
using static ThatsLit.AssemblyInfo;
using ThatsLit.Patches.Vision;

namespace ThatsLit
{
    public static class AssemblyInfo
    {
        public const string Title = ModName;
        public const string Description = "Let lighting matters.";
        public const string Configuration = SPTVersion;
        public const string Company = "";
        public const string Product = ModName;
        public const string Copyright = "Copyright © 2023 BA";
        public const string Trademark = "";
        public const string Culture = "";

        // spt 3.6.0 == 25206
        public const int TarkovVersion = 25206;
        public const string EscapeFromTarkov = "EscapeFromTarkov.exe";
        public const string ModName = "That's Lit";
        public const string ModVersion = "1.0.7";

        public const string SPTGUID = "com.spt-aki.core";
        public const string SPTVersion = "3.6.0";
    }

    [BepInPlugin("bastudio.thatslit", ModName, ModVersion)]
    [BepInDependency(SPTGUID, SPTVersion)]
    [BepInProcess(EscapeFromTarkov)]
    public class ThatsLitPlugin : BaseUnityPlugin
    {

        private void Awake()
        {
            if (!VersionChecker.CheckEftVersion(Logger, base.Info, Config))
            {
                throw new Exception("Invalid EFT Version");
            }

            //new DefaultBrainsClass();

            BindConfigs();
            Patches();
        }

        private void BindConfigs()
        {
            string category = "1. Main";
            Enabled = Config.Bind(category, "Enable", true, "Enable the mod");
            EnableUncalibratedMaps = Config.Bind(category, "Enable Uncalibrated Maps", false, "Every map need specific parameter to prevent incorrect lighting detection, by default That's Lit is disabled on maps not yet calibrated.");
            //ScoreOffset = Config.Bind(category, key: "Score Offset", 0f, "Modify the score ranging from -1 to 1, which reflect how much the player is lit. Starting from -0.4 a

            category = "2. Darkness / Brightness";
            DarknessImpactScale = Config.Bind(category, key: "Darkness Impact Scale", 1f, "Scale how AI noticing players slower due to darkness.");
            BrightnessImpactScale = Config.Bind(category, key: "Brightness Impact Scale", 1f, "Scale how AI noticing players faster due to brightness.");
            EnableFactoryDay = Config.Bind(category, "Factory (Day)", false, "Enable darkness/brightness on the map.");
            EnableFactoryNight = Config.Bind(category, "Factory (Night)", true, "Enable darkness/brightness on the map.");
            EnableLighthouse = Config.Bind(category, "Lighthouse", true, "Enable darkness/brightness on the map.");
            EnableShoreline = Config.Bind(category, "Shoreline", true, "Enable darkness/brightness on the map.");
            EnableReserve = Config.Bind(category, "Reserve", true, "Enable darkness/brightness on the map.");
            EnableWoods = Config.Bind(category, "Woods", true, "Enable darkness/brightness on the map.");
            EnableCustoms = Config.Bind(category, "Customs", true, "Enable darkness/brightness on the map.");

            category = "3. Tweaks";
            GlobalRandomOverlookChance = Config.Bind(category, key: "Global Random Overlook Chance", 0.01f, "The chance for all AIs to simply overlook in 1 vision check.");
            FoliageImpactScale = Config.Bind(category, key: "Foliage Impact Scale", 1f, "Scale the strength of extra chance to be overlooked from sneaking around foliages.");
            FinalOffset = Config.Bind(category, key: "Final Offset", 0f, "Modify the final 'time to be seen' seconds. Positive means AIs react slower and vice versa.");

            category = "4. Info";
            ScoreInfo = Config.Bind(category, "Info", true, "The info shown at the upper left corner.");

            category = "5. Performance";
            LessFoliageCheck = Config.Bind(category, "Less Foliage Check", false, "Check surrounding foliage a bit less frequent. May or may not help with CPU usage but slower to update surrounding foliages.");
            LessEquipmentCheck = Config.Bind(category, "Less Equipment Check", false, "Check equipment lights a bit less frequent. May or may not help with CPU usage but slower to update impact from turning on/off lights/lasers.");
            ExperimentalGPUReadback = Config.Bind(category, "Experimental GPU Readback", true, "May increase performance if supported and works.");

            category = "6. Debug";
            DebugInfo = Config.Bind(category, "Debug Info", false, "A lot of gibberish.");
            DebugTexture = Config.Bind(category, "Debug Texture", false, "Shows how the mod observes the player.");
            EnableHideout = Config.Bind(category, "Hideout", false, "Enable darkness/brightness on the map.");
        }

        public static ConfigEntry<bool> ScoreInfo { get; private set; }
        public static ConfigEntry<bool> DebugInfo { get; private set; }
        public static ConfigEntry<bool> DebugTexture { get; private set; }
        public static ConfigEntry<bool> Enabled { get; private set; }
        public static ConfigEntry<float> ScoreOffset { get; private set; }
        public static ConfigEntry<float> DarknessImpactScale { get; private set; }
        public static ConfigEntry<float> BrightnessImpactScale { get; private set; }
        public static ConfigEntry<float> FinalOffset { get; private set; }
        public static ConfigEntry<float> GlobalRandomOverlookChance { get; private set; }
        public static ConfigEntry<float> FoliageImpactScale { get; private set; }
        public static ConfigEntry<bool> LessFoliageCheck { get; private set; }
        public static ConfigEntry<bool> LessEquipmentCheck { get; private set; }
        public static ConfigEntry<bool> ExperimentalGPUReadback { get; private set; }
        public static ConfigEntry<bool> EnableUncalibratedMaps { get; private set; }
        public static ConfigEntry<bool> EnableLighthouse { get; private set; }
        public static ConfigEntry<bool> EnableFactoryDay { get; private set; }
        public static ConfigEntry<bool> EnableFactoryNight { get; private set; }
        public static ConfigEntry<bool> EnableReserve { get; private set; }
        public static ConfigEntry<bool> EnableCustoms { get; private set; }
        public static ConfigEntry<bool> EnableShoreline { get; private set; }
        public static ConfigEntry<bool> EnableWoods { get; private set; }
        public static ConfigEntry<bool> EnableHideout { get; private set; }

        private void Patches()
        {
            new SeenCoefPatch().Enable();

        }

        private void Update()
        {
            GameWorldHandler.Update();
        }
    }
}