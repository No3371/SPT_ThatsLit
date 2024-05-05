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
        public const string Description = "One step closer to fair gameplay, by giving AIs non-perfect vision and reactions. Because we too deserve grasses, bushes and nights.";
        public const string Configuration = SPTVersion;
        public const string Company = "";
        public const string Product = ModName;
        public const string Copyright = "Copyright © 2024 BA";
        public const string Trademark = "";
        public const string Culture = "";

        public const int TarkovVersion = 29197;
        public const string EscapeFromTarkov = "EscapeFromTarkov.exe";
        public const string ModName = "That's Lit";
        public const string ModVersion = "1.380.20";
        public const string SPTGUID = "com.spt-aki.core";
        public const string SPTVersion = "3.8.0";
        private static long modVersionComparable;

        public static long ModVersionComparable
        {
            get
            {
                if (modVersionComparable == 0)
                {
                    var splitted = ModVersion.Split('.');
                    modVersionComparable = int.Parse(splitted[0]) * 1_000000_000 + int.Parse(splitted[1]) * 1_000000 + int.Parse(splitted[2]);
                }
                return modVersionComparable;
            }
        }
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
            string category = "0. Readme";
            Config.Bind(category,
                        "Performance",
                        true,
                        new ConfigDescription("The mod takes away at least several fps. Actual overhead varies from machine to machine, some lose 5, some lose 20. You can try giving up the brightness module if the performance is not acceptable.",
                                                         null,
                                                         new ConfigurationManagerAttributes() { ReadOnly = true }));
            Config.Bind(category,
                        "Balance",
                        true,
                        new ConfigDescription("The mod aims to make AIs reasonable without making it easy. However, SAIN or other mods can change bots, and everyone has different configurations, so you may have different experience than mine with default That's Lit configs. Check \"Recommended Mods\" on the mod page for more info.",
                                                         null,
                                                         new ConfigurationManagerAttributes() { ReadOnly = true }));
            Config.Bind(category,
                        "Mechanics",
                        true,
                        new ConfigDescription("The mod tries to make everything as intuitive as possible so you can enjoy human-like AIs by just applying common sense. However, EFT's AIs are never designed to be human-like, the mod basically \"imagine up\" some new systems out of data here and there in the game, there are things can't be done, or can't be very accurate. It's best to read the mod description page if you want to make the most out of That's Lit.",
                                                         null,
                                                         new ConfigurationManagerAttributes() { ReadOnly = true }));

            category = "1. Main";
            EnabledMod = Config.Bind(category, "Enable", true, "Enable the mod. Most features can't be re-enabled in raids.");
            //ScoreOffset = Config.Bind(category, "Score Offset", 0f, "Modify the score ranging from -1 to 1, which reflect how much the player is lit. Starting from -0.4 a

            category = "2. Darkness / Brightness";
            EnabledLighting            = Config.Bind(category, "Enable", true, new ConfigDescription("Enable the module. With this turned off, AIs are not affected by your brightness.", null, new ConfigurationManagerAttributes() { Order                                                              = 100 }));
            DarknessImpactScale        = Config.Bind(category, "Darkness Impact Scale", 1f, new ConfigDescription("Scale how AI noticing players slower due to darkness.", new AcceptableValueRange<float>(0, 1.0f), new ConfigurationManagerAttributes() { Order                                           = 95 }));
            BrightnessImpactScale      = Config.Bind(category, "Brightness Impact Scale", 1f, new ConfigDescription("Scale how AI noticing players faster due to brightness.", new AcceptableValueRange<float>(0f, 1.0f), new ConfigurationManagerAttributes() { Order                                       = 94 }));
            LitVisionDistanceScale     = Config.Bind(category, "Lit Vision Distance Scale", 1f, new ConfigDescription("Scale how AI noticing players from further under some circumstances. This is designed to compensate low night vision distance from SAIN, you may want to set this to 0 if you don't run SAIN.", new AcceptableValueRange<float>(0, 1f), new ConfigurationManagerAttributes() { Order = 93 }));
            EnableFactoryNight         = Config.Bind(category, "Factory (Night)", true, "Enable darkness/brightness on the map.");
            EnableLighthouse           = Config.Bind(category, "Lighthouse", true, "Enable darkness/brightness on the map.");
            EnableShoreline            = Config.Bind(category, "Shoreline", true, "Enable darkness/brightness on the map.");
            EnableReserve              = Config.Bind(category, "Reserve", true, "Enable darkness/brightness on the map.");
            EnableWoods                = Config.Bind(category, "Woods", true, "Enable darkness/brightness on the map.");
            EnableInterchange          = Config.Bind(category, "Interchange", true, "Enable darkness/brightness on the map.");
            EnableCustoms              = Config.Bind(category, "Customs", true, "Enable darkness/brightness on the map.");
            EnableStreets              = Config.Bind(category, "Streets", true, "Enable darkness/brightness on the map.");
            EnableGroundZero              = Config.Bind(category, "Ground Zero", true, "Enable darkness/brightness on the map.");

            category                   = "3. Encountering Patch";
            EnabledEncountering        = Config.Bind(category,
                                                     "Enable",
                                                     true,
                                                     new ConfigDescription("Enable the module. This randomly nerf AIs a bit at the moment they encounter you, especially when they are sprinting.", null, new ConfigurationManagerAttributes() { Order = 100 }));
            VagueHintChance            = Config.Bind(category,
                                                     "Vague Hint Chance",
                                                     0.6f,
                                                     new ConfigDescription("The chance to cancel a bot's visual confirmation on you and instead only tell it it's spotted from roughly your direction, when the system say you are visible to it but it's not even facing your way.", new AcceptableValueRange<float>(0, 1f), new ConfigurationManagerAttributes() { Order = 99 }));

            category                   = "4. Grasses";
            EnabledGrasses             = Config.Bind(category, "Enable", true, new ConfigDescription("Enable the module. This enable grasses to block bot vision.", null, new ConfigurationManagerAttributes() { Order                                                                                    = 100 }));

            category                   = "5. Tweaks";
            GlobalRandomOverlookChance = Config.Bind(category,
                                                     "Global Random Overlook Chance",
                                                     0.01f,
                                                     new ConfigDescription("The chance for all AIs to simply overlook in 1 vision check.", new AcceptableValueRange<float>(0, 1f), new ConfigurationManagerAttributes() { Order = 100 }));
            FoliageImpactScale         = Config.Bind(category,
                                                     "Foliage Impact Scale",
                                                     1f,
                                                     new ConfigDescription("Scale the strength of extra chance to be overlooked from sneaking around foliages.", new AcceptableValueRange<float>(0, 1f), new ConfigurationManagerAttributes() { Order = 99 }));
            FinalImpactScale         = Config.Bind(category,
                                                     "Final Impact Scale",
                                                     1f,
                                                     new ConfigDescription("Scale the buff/nerf to bots from the mod. 0% = use the original value. Adjust this to balance your game to your liking. This is mainly provided for people whose game somehow becomes too easy with the mod.", new AcceptableValueRange<float>(0, 1f), new ConfigurationManagerAttributes() { Order = 98}));
            FinalOffset                = Config.Bind(category, "Final Offset", 0f, "(Not recommanded because it's easy to mess up the balance, try Final Impact Scale first) Modify the final 'time to be seen' seconds. Positive means AIs react slower and vice versa. Applied after Final Impact Scale.");
            EnableMovementImpact       = Config.Bind(category, "MovementImpact", true, "Should sprinting bots spot player slower & Should moving (esp. sprinting) player get spotted slightly faster. This option is provided because SAIN is including similiar (player side only) feature (though their effectiveness is unknown yet.");
            AlternativeReactionFluctuation       = Config.Bind(category, "Alternative Reaction Fluctuation", true, "If Brightness module is disabled, introduce a slight fluctuation to bot reaction time, so rarely you may get lucky or unlucky, may be not noticeable.");
            
            
            category                   = "6. Info";
            ScoreInfo                  = Config.Bind(category, "Lighting Info", true, "Shown at the upper left corner.");
            FoliageInfo                  = Config.Bind(category, "Foliage Info", true, "Gives a hint about surrounding foliage.");
            TerrainInfo                  = Config.Bind(category, "Terrain Info", true, "Gives a hint about surrounding grasses. Only grasses in direction to the bot doing vision check is applied and there are some more dynamic factors, so this only gives you the rough idea about how dense the surrounding grasses are.");
            HideMapTip                  = Config.Bind(category, "Hide Map Tip", false, "Hide the reminder about disabled lit detection.");

            category                   = "7. Performance";
            LessFoliageCheck           = Config.Bind(category, "Less Foliage Check", false, "Check surrounding foliage a bit less frequent. May or may not help with CPU usage but slower to update surrounding foliages.");
            LessEquipmentCheck         = Config.Bind(category, "Less Equipment Check", false, "Check equipment lights a bit less frequent. May or may not help with CPU usage but slower to update impact from turning on/off lights/lasers.");
            ResLevel                 = Config.Bind(category, "Resolustion Level", 2,
                                                   new ConfigDescription("Resolution of the observed image by the observer camera, higher level means somewhat higher accuracy. Has an impact on CPU time. Level1 -> 32x32, Level2 -> 64x64... This config is used on raid start.", new AcceptableValueRange<int>(1, 4)));
            FoliageSamples                 = Config.Bind(category, "Foliage Samples", 1,
                                                   new ConfigDescription("How many foliage to check if it's inbetween you and bots, increasing this allows foliage affects multiple bots from different directions. Could slightly an impact on CPU time. This config is used on raid start.", new AcceptableValueRange<int>(1, 5)));

            category                   = "8. Debug";
            DebugInfo                  = Config.Bind(category, "Debug Info (Expensive)", false, "A lot of gibberish.");
            DebugTexture               = Config.Bind(category, "Debug Texture", false, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced                                                                        = true }));
            DebugSlowTexture               = Config.Bind(category, "Slow Texture", false, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced                                                                        = true }));
            EnableHideout              = Config.Bind(category, "Hideout", false, "Enable darkness/brightness on the map.");
            EnableBenchmark              = Config.Bind(category, "Benchmark", false, "");

            category                   = "9. Balance";
            IncludeBosses              = Config.Bind(category, "Include Bosses", false, "Should all features from this mod work for boss. Makes bosses EASY.");
            EnableEquipmentCheck         = Config.Bind(category, "Equipment Check", true, "Whether the mod checks your equipments. Disabling this stops lights/lasers detection and makes stealth EASY.");
            // ExtraDarknessImpactScale        = Config.Bind(category, "Darkness Impact Scale", 0f, new ConfigDescription("Additionaly scale how AI noticing players slower due to darkness. If 100% is not enough for you.", new AcceptableValueRange<float>(0, 1.0f), new ConfigurationManagerAttributes() { Order                                           = 95 }));
            // ExtraBrightnessImpactScale      = Config.Bind(category, "Brightness Impact Scale", 0f, new ConfigDescription("Additionaly Scale how AI noticing players faster due to brightness. If 100% is not enough for you.", new AcceptableValueRange<float>(0f, 1.0f), new ConfigurationManagerAttributes() { Order                                       = 94 }));
            
        }

        public static ConfigEntry<bool> ScoreInfo { get; private set; }
        public static ConfigEntry<bool> TerrainInfo { get; private set; }
        public static ConfigEntry<bool> FoliageInfo { get; private set; }
        public static ConfigEntry<bool> DebugInfo { get; private set; }
        public static ConfigEntry<bool> HideMapTip { get; private set; }
        public static ConfigEntry<bool> DebugTexture { get; private set; }
        public static ConfigEntry<bool> DebugSlowTexture { get; private set; }
        public static ConfigEntry<bool> EnabledMod { get; private set; }
        public static ConfigEntry<bool> EnabledLighting { get; private set; }
        public static ConfigEntry<bool> EnabledEncountering { get; private set; }
        public static ConfigEntry<bool> EnabledGrasses { get; private set; }
        public static ConfigEntry<bool> EnableMovementImpact { get; private set; }
        public static ConfigEntry<bool> EnableEquipmentCheck { get; private set; }
        public static ConfigEntry<bool> AlternativeReactionFluctuation { get; private set; }
        public static ConfigEntry<float> ScoreOffset { get; private set; }
        public static float CombinedDarknessImpactScale { get => DarknessImpactScale.Value + ExtraDarknessImpactScale.Value;}
        public static float CombinedBrightnessImpactScale { get => BrightnessImpactScale.Value + ExtraBrightnessImpactScale.Value;}
        public static ConfigEntry<float> DarknessImpactScale { get; private set; }
        public static ConfigEntry<float> BrightnessImpactScale { get; private set; }
        public static ConfigEntry<float> ExtraDarknessImpactScale { get; private set; }
        public static ConfigEntry<float> ExtraBrightnessImpactScale { get; private set; }
        public static ConfigEntry<float> LitVisionDistanceScale { get; private set; }
        public static ConfigEntry<float> FinalOffset { get; private set; }
        public static ConfigEntry<float> FinalImpactScale { get; private set; }
        public static ConfigEntry<float> VagueHintChance { get; private set; }
        public static ConfigEntry<float> GlobalRandomOverlookChance { get; private set; }
        public static ConfigEntry<float> FoliageImpactScale { get; private set; }
        public static ConfigEntry<bool> IncludeBosses { get; private set; }
        public static ConfigEntry<bool> LessFoliageCheck { get; private set; }
        public static ConfigEntry<bool> LessEquipmentCheck { get; private set; }
        public static ConfigEntry<bool> EnableLighthouse { get; private set; }
        public static ConfigEntry<bool> EnableFactoryNight { get; private set; }
        public static ConfigEntry<bool> EnableReserve { get; private set; }
        public static ConfigEntry<bool> EnableCustoms { get; private set; }
        public static ConfigEntry<bool> EnableShoreline { get; private set; }
        public static ConfigEntry<bool> EnableInterchange { get; private set; }
        public static ConfigEntry<bool> EnableStreets { get; private set; }
        public static ConfigEntry<bool> EnableGroundZero { get; private set; }
        public static ConfigEntry<bool> EnableWoods { get; private set; }
        public static ConfigEntry<bool> EnableHideout { get; private set; }
        public static ConfigEntry<bool> EnableBenchmark { get; private set; }
        public static ConfigEntry<int> ResLevel { get; private set; }
        public static ConfigEntry<int> FoliageSamples { get; private set; }
        // public static ConfigEntry<bool> DevMode { get; private set; }
        // public static ConfigEntry<bool> DevModeInvisible { get; private set; }
        // public static ConfigEntry<bool> NoGPUReq { get; private set; }
        // public static ConfigEntry<float> OverrideMinBaseAmbienceScore { get; private set; }
        // public static ConfigEntry<float> OverrideMaxBaseAmbienceScore { get; private set; }
        // public static ConfigEntry<float> OverrideMinAmbienceLum { get; private set; }
        // public static ConfigEntry<float> OverrideMaxAmbienceLum { get; private set; }
        // public static ConfigEntry<float> OverridePixelLumScoreScale { get; private set; }
        // public static ConfigEntry<float> OverrideMaxSunLightScore { get; private set; }
        // public static ConfigEntry<float> OverrideMaxMoonLightScore { get; private set; }
        // public static ConfigEntry<float> OverrideScore0 { get; private set; }
        // public static ConfigEntry<float> OverrideScore1 { get; private set; }
        // public static ConfigEntry<float> OverrideScore2 { get; private set; }
        // public static ConfigEntry<float> OverrideScore3 { get; private set; }
        // public static ConfigEntry<float> OverrideScore4 { get; private set; }
        // public static ConfigEntry<float> OverrideScore5 { get; private set; }
        // public static ConfigEntry<float> OverrideScore6 { get; private set; }
        // public static ConfigEntry<float> OverrideThreshold0 { get; private set; }
        // public static ConfigEntry<float> OverrideThreshold1 { get; private set; }
        // public static ConfigEntry<float> OverrideThreshold2 { get; private set; }
        // public static ConfigEntry<float> OverrideThreshold3 { get; private set; }
        // public static ConfigEntry<float> OverrideThreshold4 { get; private set; }
        // public static ConfigEntry<float> OverrideThreshold5 { get; private set; }

        private void Patches()
        {
            new SeenCoefPatch().Enable();
            new EncounteringPatch().Enable();
            new ExtraVisibleDistancePatch().Enable();
            // new ShadowMaskExtractorPatch().Enable();
            // new DebugCountId().Enable();
        }

        private void Update()
        {
            GameWorldHandler.Update();
        }
    }
}