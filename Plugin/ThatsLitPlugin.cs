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
            if (!VersionChecker.CheckEftVersion(Logger, Info, Config))
            {
                throw new Exception("Invalid EFT Version");
            }

            //new DefaultBrainsClass();

            BindConfigs();
            Patches();
        }

        private void BindConfigs()
        {
            string category = "Main";
            DebugInfo = Config.Bind(category, "Debug Info", false, "");
            DebugTexture = Config.Bind(category, "Debug Texture", false, "Shows how the mod observes the player.");
            DisableEffect = Config.Bind(category, "Disable Effect", false, "Disable the mod");
            ScoreOffset = Config.Bind(category, key: "Score Offset", 0f, "Modify the score ranging from -1 to 1, which reflect how much the player is lit. Starting from -0.4 and 0.4, up to -1 and 1, the score start to matter. See x^3 in Desmos plotter.");
            ImpactScale = Config.Bind(category, key: "Impact Scale", 1f, "Scale how much the calculation affect time to be seen.");
            ImpactOffset = Config.Bind(category, key: "Impact Offset", 0f, "Modify the final time to be seen.");
        }

        public static ConfigEntry<bool> DebugInfo { get; private set; }
        public static ConfigEntry<bool> DebugTexture { get; private set; }
        public static ConfigEntry<bool> DisableEffect { get; private set; }
        public static ConfigEntry<float> ScoreOffset { get; private set; }
        public static ConfigEntry<float> ImpactScale { get; private set; }
        public static ConfigEntry<float> ImpactOffset { get; private set; }

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