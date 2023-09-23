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

            BindConfigs();
            Patches();
        }

        private void BindConfigs()
        {
            string category = "1. Main";
            EnabledMod = Config.Bind(category, "Enable", true, "Enable the mod. Can not be turned back on in-raid.");
            EnableUncalibratedMaps = Config.Bind(category, "Enable Uncalibrated Maps", false, "Every map need specific parameter to prevent incorrect lighting detection, by default That's Lit is disabled on maps not yet calibrated.");
            //ScoreOffset = Config.Bind(category, key: "Score Offset", 0f, "Modify the score ranging from -1 to 1, which reflect how much the player is lit. Starting from -0.4 a

            category = "2. Darkness / Brightness";
            EnabledLighting = Config.Bind(category, "Enable", true, "Enable the module. With this turned off, AIs are only affected by randomness and foliages.");
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
            LowResMode = Config.Bind(category, "Low Res Mode", false, "Can reduce CPU time of calculation, may or may not lower the lighting detection accuracy.");

            category = "6. Debug";
            DebugInfo = Config.Bind(category, "Debug Info", false, "A lot of gibberish.");
            DebugTexture = Config.Bind(category, "Debug Texture", false, "Shows how the mod observes the player.");
            EnableHideout = Config.Bind(category, "Hideout", false, "Enable darkness/brightness on the map.");
            DevMode = Config.Bind(category, "Dev Mode", false, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            DevModeInvisible = Config.Bind(category, "Dev Mode Invisible", false, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideMinBaseAmbienceScore = Config.Bind(category, "MinBaseAmbienceScore", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideMaxBaseAmbienceScore = Config.Bind(category, "MaxBaseAmbienceScore", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideMinAmbienceLum = Config.Bind(category, "MinAmbienceLum", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideMaxAmbienceLum = Config.Bind(category, "MaxAmbienceLum", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverridePixelLumScoreScale = Config.Bind(category, "PixelLumScoreScale", 1f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideMaxSunLightScore = Config.Bind(category, "MaxSunLightScore", 1f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideMaxMoonLightScore = Config.Bind(category, "MaxMoonLightScore", 1f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideScore0 = Config.Bind(category, "Score0", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideScore1 = Config.Bind(category, "Score1", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideScore2 = Config.Bind(category, "Score2", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideScore3 = Config.Bind(category, "Score3", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideScore4 = Config.Bind(category, "Score4", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideScore5 = Config.Bind(category, "Score5", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideScore6 = Config.Bind(category, "Score6", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideThreshold0 = Config.Bind(category, "Threshold0", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideThreshold1 = Config.Bind(category, "Threshold1", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideThreshold2 = Config.Bind(category, "Threshold2", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideThreshold3 = Config.Bind(category, "Threshold3", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideThreshold4 = Config.Bind(category, "Threshold4", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
            OverrideThreshold5 = Config.Bind(category, "Threshold5", 0f, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced = true }));
        }

        public static ConfigEntry<bool> ScoreInfo { get; private set; }
        public static ConfigEntry<bool> DebugInfo { get; private set; }
        public static ConfigEntry<bool> DebugTexture { get; private set; }
        public static ConfigEntry<bool> EnabledMod { get; private set; }
        public static ConfigEntry<bool> EnabledLighting { get; private set; }
        public static ConfigEntry<float> ScoreOffset { get; private set; }
        public static ConfigEntry<float> DarknessImpactScale { get; private set; }
        public static ConfigEntry<float> BrightnessImpactScale { get; private set; }
        public static ConfigEntry<float> FinalOffset { get; private set; }
        public static ConfigEntry<float> GlobalRandomOverlookChance { get; private set; }
        public static ConfigEntry<float> FoliageImpactScale { get; private set; }
        public static ConfigEntry<bool> LessFoliageCheck { get; private set; }
        public static ConfigEntry<bool> LessEquipmentCheck { get; private set; }
        public static ConfigEntry<bool> EnableUncalibratedMaps { get; private set; }
        public static ConfigEntry<bool> EnableLighthouse { get; private set; }
        public static ConfigEntry<bool> EnableFactoryDay { get; private set; }
        public static ConfigEntry<bool> EnableFactoryNight { get; private set; }
        public static ConfigEntry<bool> EnableReserve { get; private set; }
        public static ConfigEntry<bool> EnableCustoms { get; private set; }
        public static ConfigEntry<bool> EnableShoreline { get; private set; }
        public static ConfigEntry<bool> EnableWoods { get; private set; }
        public static ConfigEntry<bool> EnableHideout { get; private set; }
        public static ConfigEntry<bool> LowResMode { get; private set; }
        public static ConfigEntry<bool> DevMode { get; private set; }
        public static ConfigEntry<bool> DevModeInvisible { get; private set; }
        public static ConfigEntry<float> OverrideMinBaseAmbienceScore { get; private set; }
        public static ConfigEntry<float> OverrideMaxBaseAmbienceScore { get; private set; }
        public static ConfigEntry<float> OverrideMinAmbienceLum { get; private set; }
        public static ConfigEntry<float> OverrideMaxAmbienceLum { get; private set; }
        public static ConfigEntry<float> OverridePixelLumScoreScale { get; private set; }
        public static ConfigEntry<float> OverrideMaxSunLightScore { get; private set; }
        public static ConfigEntry<float> OverrideMaxMoonLightScore { get; private set; }
        public static ConfigEntry<float> OverrideScore0 { get; private set; }
        public static ConfigEntry<float> OverrideScore1 { get; private set; }
        public static ConfigEntry<float> OverrideScore2 { get; private set; }
        public static ConfigEntry<float> OverrideScore3 { get; private set; }
        public static ConfigEntry<float> OverrideScore4 { get; private set; }
        public static ConfigEntry<float> OverrideScore5 { get; private set; }
        public static ConfigEntry<float> OverrideScore6 { get; private set; }
        public static ConfigEntry<float> OverrideThreshold0 { get; private set; }
        public static ConfigEntry<float> OverrideThreshold1 { get; private set; }
        public static ConfigEntry<float> OverrideThreshold2 { get; private set; }
        public static ConfigEntry<float> OverrideThreshold3 { get; private set; }
        public static ConfigEntry<float> OverrideThreshold4 { get; private set; }
        public static ConfigEntry<float> OverrideThreshold5 { get; private set; }

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