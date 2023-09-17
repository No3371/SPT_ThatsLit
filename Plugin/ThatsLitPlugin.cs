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
        public const string ModVersion = "1.0.0";

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
            //ScoreOffset = Config.Bind(category, key: "Score Offset", 0f, "Modify the score ranging from -1 to 1, which reflect how much the player is lit. Starting from -0.4 and 0.4, up to -1 and 1, the score start to matter. See x^3 in Desmos plotter.");
            FinalOffset = Config.Bind(category, key: "Final Offset", 0f, "Modify the final 'time to be seen' seconds. Positive means slower and vice versa.");

            category = "2. Darkness / Brightness";
            DarknessImpactScale = Config.Bind(category, key: "Darkness Impact Scale", 1f, "Scale the strength of increment of 'time to be seen' threshold.");
            BrightnessImpactScale = Config.Bind(category, key: "Brightness Impact Scale", 1f, "Scale the strength of decreament of 'time to be seen' threshold.");

            category = "3. Tweaks";
            GlobalRandomOverlookChance = Config.Bind(category, key: "Global Random Overlook Chance", 0.01f, "The chance for all AIs to simply overlook in 1 vision check.");
            FoliageImpactScale = Config.Bind(category, key: "Foliage Impact Scale", 1f, "Scale the strength of extra chance to be overlooked from sneaking around foliages.");

            category = "4. Info";
            Info = Config.Bind(category, "Info", true, "The info shown at the upper left corner.");

            category = "5. Debug";
            DebugInfo = Config.Bind(category, "Debug Info", false, "A lot of gibberish.");
            DebugTexture = Config.Bind(category, "Debug Texture", false, "Shows how the mod observes the player.");
        }

        public static ConfigEntry<bool> Info { get; private set; }
        public static ConfigEntry<bool> DebugInfo { get; private set; }
        public static ConfigEntry<bool> DebugTexture { get; private set; }
        public static ConfigEntry<bool> Enabled { get; private set; }
        public static ConfigEntry<float> ScoreOffset { get; private set; }
        public static ConfigEntry<float> DarknessImpactScale { get; private set; }
        public static ConfigEntry<float> BrightnessImpactScale { get; private set; }
        public static ConfigEntry<float> FinalOffset { get; private set; }
        public static ConfigEntry<float> GlobalRandomOverlookChance { get; private set; }
        public static ConfigEntry<float> FoliageImpactScale { get; private set; }

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