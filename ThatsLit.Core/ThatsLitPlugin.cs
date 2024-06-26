using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using DrakiaXYZ.VersionChecker;
using System;
using static ThatsLit.AssemblyInfo;
using ThatsLit.Patches.Vision;
using System.Collections;

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
        public const string ModVersion = "1.383.31";
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
    [BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("bastudio.updatenotifier", BepInDependency.DependencyFlags.SoftDependency)]
    public class ThatsLitPlugin : BaseUnityPlugin
    {
        internal static bool SAINLoaded { get; private set; }
        internal static ManagedStopWatch swUpdate, swGUI, swFoliage, swTerrain, swScoreCalc, swSeenCoef, swEncountering, swExtraVisDis, swNoBushOverride, swBlindFireScatter;
        static ThatsLitPlugin ()
        {
            swUpdate           = new ManagedStopWatch("Update");
            swGUI              = new ManagedStopWatch("GUI");
            swFoliage          = new ManagedStopWatch("Foliage");
            swTerrain          = new ManagedStopWatch("Terrain");
            swScoreCalc          = new ManagedStopWatch("ScoreCalc");
            swSeenCoef         = new ManagedStopWatch("SeenCoef");
            swEncountering     = new ManagedStopWatch("Encountering");
            swExtraVisDis      = new ManagedStopWatch("ExtraVisDis");
            swNoBushOverride   = new ManagedStopWatch("NoBushOverride");
            swBlindFireScatter = new ManagedStopWatch("BlindFireScatter");
        }
        private void Awake()
        {
            if (!VersionChecker.CheckEftVersion(Logger, base.Info, Config))
            {
                throw new Exception("Invalid EFT Version");
            }

            BindConfigs();
            ThatsLitCompat.LoadCompatFiles();
            if (Chainloader.PluginInfos.ContainsKey("me.sol.sain"))
                SAINLoaded = true;
            Patches();
            TryCheckUpdate();
        }

        void Start ()
        {
            if (Chainloader.PluginInfos.ContainsKey("com.fika.core")
             && EnabledLighting.Value
             && !Chainloader.PluginInfos.ContainsKey("bastudio.thatslit.sync"))
            {
                string message = $"[That's Lit] Fika detected, but That's Lit Sync extension not found. Without it, you will lose many fps per player. Get Sync extension from Fika Discord.";
                NotificationManagerClass.DisplayWarningNotification(message, EFT.Communications.ENotificationDurationType.Long);
                Logger.LogError(message);
                // EFT.UI.ConsoleScreen.Log(message); // EXCEPTION and fails plugin loading (Console is not loaded yet)
            }
        }

        public void TryCheckUpdate ()
        {
            var url = "https://raw.githubusercontent.com/No3371/SPT_ThatsLit/main/ThatsLit.Core/.update_notifier";
            if (!Chainloader.PluginInfos.TryGetValue("bastudio.updatenotifier", out var pluginInfo))
            {
                Logger.LogInfo("Update Notifier not found.");
                return;
            }

            BaseUnityPlugin updntf = pluginInfo.Instance;
            updntf.GetType().GetMethod("CheckForUpdate", new Type[] { typeof(BaseUnityPlugin), typeof(string)}).Invoke(updntf, new object[] {this, url});
        }

        private void BindConfigs()
        {
            string category = "0. Readme";
            Config.Bind(category,
                        "Performance (Readme)",
                        true,
                        new ConfigDescription("The mod takes away at least several fps. Actual overhead varies from machine to machine, some lose 5, some lose 20. You can try giving up the brightness module if the performance is not acceptable.",
                                                         null,
                                                         new ConfigurationManagerAttributes() { ReadOnly = true }));
            Config.Bind(category,
                        "Balance (Readme)",
                        true,
                        new ConfigDescription("The mod aims to make AIs reasonable without making it easy, but it requires some proper setup. Besides, SAIN or other mods can change bots, and everyone has different configurations, so you may have different experience than mine with default That's Lit configs. Check \"Recommended Mods\" on the mod page for more info.",
                                                         null,
                                                         new ConfigurationManagerAttributes() { ReadOnly = true }));
            Config.Bind(category,
                        "Mechanics (Readme)",
                        true,
                        new ConfigDescription("The mod tries to make everything as intuitive as possible so you can enjoy human-like AIs by just applying common sense. However, EFT's AIs are never designed to be human-like, the mod basically \"imagine up\" some new systems out of data here and there in the game, there are things can't be done, or can't be very accurate. It's best to read the mod description page if you want to make the most out of That's Lit.",
                                                         null,
                                                         new ConfigurationManagerAttributes() { ReadOnly = true }));

            category = "1. Main";
            EnabledMod = Config.Bind(category, "Enable", true, "Enable the mod. Some features can't be re-enabled in raids.");
            //ScoreOffset = Config.Bind(category, "Score Offset", 0f, "Modify the score ranging from -1 to 1, which reflect how much the player is lit. Starting from -0.4 a

            category = "2. Darkness / Brightness";
            EnabledLighting            = Config.Bind(category, "Enable", true, new ConfigDescription("Enable the module. With this turned off, AIs are not affected by your brightness.", null, new ConfigurationManagerAttributes() { Order                                                              = 100 }));
            DarknessImpactScaleOffset        = Config.Bind(category, "Darkness Impact Offset", 0.5f, new ConfigDescription("Scale how AI noticing players slower due to darkness. Be careful when increasing this as it could easily breaks the combat balance.", new AcceptableValueRange<float>(0, 1.0f), new ConfigurationManagerAttributes() { Order                                           = 95 }));
            BrightnessImpactScaleOffset      = Config.Bind(category, "Brightness Impact Offset", 0.5f, new ConfigDescription("Scale how AI noticing players faster due to brightness. Be careful when increasing this as it could easily breaks the combat balance.", new AcceptableValueRange<float>(0f, 1.0f), new ConfigurationManagerAttributes() { Order                                       = 94 }));
            ExtraVisionDistanceScale     = Config.Bind(category, "Extra Vision Distance Scale", 1f, new ConfigDescription("Scale how AI noticing players from further under some circumstances. This is designed to compensate low night vision distance from SAIN, you may want to set this to 0 if you don't run SAIN.", new AcceptableValueRange<float>(0, 1f), new ConfigurationManagerAttributes() { Order = 93 }));
            EnableFactoryNight         = Config.Bind(category, "Factory (Night)", true, "Enable darkness/brightness on the map.");
            EnableLighthouse           = Config.Bind(category, "Lighthouse", true, "Enable darkness/brightness on the map.");
            EnableShoreline            = Config.Bind(category, "Shoreline", true, "Enable darkness/brightness on the map.");
            EnableReserve              = Config.Bind(category, "Reserve", true, "Enable darkness/brightness on the map.");
            EnableWoods                = Config.Bind(category, "Woods", true, "Enable darkness/brightness on the map.");
            EnableInterchange          = Config.Bind(category, "Interchange", true, "Enable darkness/brightness on the map.");
            EnableCustoms              = Config.Bind(category, "Customs", true, "Enable darkness/brightness on the map.");
            EnableStreets              = Config.Bind(category, "Streets", true, "Enable darkness/brightness on the map.");
            EnableGroundZero              = Config.Bind(category, "Ground Zero", true, "Enable darkness/brightness on the map.");
            // ShadowlessGroundZero       = Config.Bind(category, "ShadowlessGroundZero", true, "The top half of some big buildings in Ground Zero does not have proper colliders and thus mess with Ambience Shadow calculation. If you really feel it's a big problem, enable this to address the issue.");
            VolumetricLightRenderer              = Config.Bind(category, "Observe Volumetric Lights", true, "Let Brightness Module reacts to volumetric lights. Disable this if it cause issues.");
             
            category                   = "3. Encountering Patch";
            EnabledEncountering        = Config.Bind(category,
                                                     "Enable",
                                                     true,
                                                     new ConfigDescription("Enable the module. Encountering Patch nerf bots reaction at the moment they see a player, especially when they are sprinting.", null, new ConfigurationManagerAttributes() { Order = 100 }));
            // VisibilityCancelChance            = Config.Bind(category,
            //                                          "Visibility Cancel Chance",
            //                                          0.6f,
            //                                          new ConfigDescription("Basically, this reduce instant returning fire. When the system set you to be visible to a bot, but the bot is not even facing your way (yes this happens in some situations), at this chance That's Lit will cancel the visibility and instead only tell it it's spotted from roughly your way.", new AcceptableValueRange<float>(0, 1f), new ConfigurationManagerAttributes() { Order = 99 }));

            category                   = "4. Grasses & Foliage";
            EnabledGrasses             = Config.Bind(category, "Enable Grasses", true, new ConfigDescription("Enable the module. This enable grasses to block bot vision.", null, new ConfigurationManagerAttributes() { Order                                                                                    = 100 }));
            EnabledFoliage             = Config.Bind(category, "Enable Foliage", true, new ConfigDescription("Enable the module. This enable foliage to distract distant bots.", null, new ConfigurationManagerAttributes() { Order                                                                                    = 100 }));
            EnabledBushRatting             = Config.Bind(category, "Enable Bush Ratting", true, new ConfigDescription("Enable the module. This enable foliage to distract distant bots.", null, new ConfigurationManagerAttributes() { Order                                                                                    = 100 }));
            FoliageImpactScale         = Config.Bind(category,
                                            "Foliage Impact Scale",
                                            1f,
                                            new ConfigDescription("Scale the strength of extra chance to be overlooked by faraway bots from sneaking around foliages.", new AcceptableValueRange<float>(0, 1f), new ConfigurationManagerAttributes() { Order = 99 }));

            category                   = "5. Tweaks";
            EnableMovementImpact       = Config.Bind(category, "Movement Impact", true, "Should sprinting bots spot player slower & Should moving (esp. sprinting) player get spotted slightly faster. This option is provided because SAIN is including similiar (player side only) feature (though their effectiveness is unknown yet.");
            FinalImpactScaleDelaying        = Config.Bind(category,
                                                    "Final Impact Scale (Slower)",
                                                    1f,
                                                    new ConfigDescription("Scale how much slower bots react because of the mod. 0% = use the original value. *Carefully* adjust this to balance your game to your liking.", new AcceptableValueRange<float>(0, 1f), new ConfigurationManagerAttributes() { Order = 98}));
            FinalImpactScaleFastening         = Config.Bind(category,
                                                     "Final Impact Scale (Faster)",
                                                     1f,
                                                     new ConfigDescription("Scale how much faster bots react because of the mod. 0% = use the original value. *Carefully* adjust this to balance your game to your liking.", new AcceptableValueRange<float>(0, 1f), new ConfigurationManagerAttributes() { Order = 97}));
            FinalOffset                = Config.Bind(category, "Final Offset", 0f, "(Not recommanded because it's easy to mess up the balance, try Final Impact Scale first) Modify the final 'time to be seen' seconds. Positive means AIs react slower and vice versa. Applied after Final Impact Scale.");
            AlternativeReactionFluctuation       = Config.Bind(category, "Alternative Reaction Fluctuation", true, "If Brightness module is disabled, introduce a slight fluctuation to bot reaction time, so rarely you may get lucky or unlucky, may be not noticeable.");
            
            
            category                   = "6. Info";
            ScoreInfo                  = Config.Bind(category, "Lighting Info", true, new ConfigDescription("Display lighting meter.", null));
            WeatherInfo                  = Config.Bind(category, "Weather Info", true, new ConfigDescription("Clear/Cloudy indicator.", null));
            EquipmentInfo                  = Config.Bind(category, "Equipment Info", true, new ConfigDescription("Enabled lights/lasers indicator.", null));
            FoliageInfo                  = Config.Bind(category, "Foliage Info", true, new ConfigDescription("A rough rating of surrounding foliage.", null));
            TerrainInfo                  = Config.Bind(category, "Terrain Info", true
                                                     , new ConfigDescription("A hint about surrounding grasses. Only grasses in direction to the bot doing vision check is applied and there are some more dynamic factors, so this only gives you the rough idea about how dense the surrounding grasses are.", null));
            HideMapTip                  = Config.Bind(category, "Hide Map Tip", false, new ConfigDescription("Hide the reminder about disabled Brightness module.", null));
            InfoOffset                 = Config.Bind(category, "InfoOffset", 0,
                                                   new ConfigDescription("Vertical offset to the top.", new AcceptableValueRange<int>(0, 7)));
            // AlternativeMeterUnicde                  = Config.Bind(category, "Alternative Meter", false, "If somehow the GUI meters unicodes are not rendered on your system, try this options.");


            category                   = "7. Performance";
            ResLevel                 = Config.Bind(category, "Resolution Level", 2,
                                                   new ConfigDescription("Resolution of the observed image by the observer camera, higher level means somewhat higher accuracy. Has an impact on CPU time. Level1 -> 32x32, Level2 -> 64x64... This config is used on raid start.", new AcceptableValueRange<int>(1, 4)));
            FoliageSamples                 = Config.Bind(category, "Foliage Samples", 1,
                                                   new ConfigDescription("How many foliage to check if it's inbetween you and bots, increasing this allows foliage affects multiple bots from different directions. Could slightly an impact on CPU time. This config is used on raid start.", new AcceptableValueRange<int>(1, 5)));

            category                   = "8. Debug";
            DebugInfo                  = Config.Bind(category, "Debug Info (Expensive)", false, "A lot of gibberish.");
            DebugTexture               = Config.Bind(category, "Debug Texture", false, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced                                                                        = true }));
            EnableHideout              = Config.Bind(category, "Hideout", false, "Enable darkness/brightness on the map.");
            EnableBenchmark              = Config.Bind(category, "Benchmark", false, "");
            DebugTerrain               = Config.Bind(category, "Debug Terrain", false, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced                                                                        = true }));
            DebugCompat               = Config.Bind(category, "Debug Compat", false, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced                                                                        = true }));
            DebugProxy               = Config.Bind(category, "Debug Proxy", false, new ConfigDescription("", null, new ConfigurationManagerAttributes() { IsAdvanced                                                                        = true }));

            category                   = "9. Balance";
            IncludeBosses              = Config.Bind(category, "Include Bosses", false, "Should all features from this mod work for boss. Makes bosses EASY.");
            PMCOnlyMode              = Config.Bind(category, "PMC Only Mode", false, "Requested. So the mod only affect PMCs.");
            EnableEquipmentCheck         = Config.Bind(category, "Equipment Check", true, "Whether the mod checks your equipments. Disabling this stops lights/lasers detection and makes stealth EASY.");
            InterruptSAINNoBush              = Config.Bind(category, "Interrupt SAIN No Bush", false, "DO NOT COMPLAIN ABOUT NO BUSH ESP TO Solarint IF YOU HAVE THIS ON.New SAIN No Bush is designed to be aggressive. It can block bot vision even if you are just 2m away and the bot is looking straight at you. This add a chance to turn off SAIN's No Bush ESP at close range.");
            ForceBlindFireScatter              = Config.Bind(category, "Force Blind Fire Scatter", true, "Force a random scatter on bot blind fireing, scaled by distance.");
            BotLookDirectionTweaks              = Config.Bind(category, "Bot Look Direction Tweaks", true, "Try to tell the nearest bot to look towards the player when it makes sense.");
            
        }

        public static ConfigEntry<bool> ScoreInfo { get; private set; }
        public static ConfigEntry<bool> WeatherInfo { get; private set; }
        public static ConfigEntry<bool> EquipmentInfo { get; private set; }
        public static ConfigEntry<bool> TerrainInfo { get; private set; }
        public static ConfigEntry<bool> FoliageInfo { get; private set; }
        public static ConfigEntry<bool> DebugInfo { get; private set; }
        public static ConfigEntry<int> InfoOffset { get; private set; }
        public static ConfigEntry<bool> HideMapTip { get; private set; }
        public static ConfigEntry<bool> DebugTexture { get; private set; }
        public static ConfigEntry<bool> DebugTerrain { get; private set; }
        public static ConfigEntry<bool> DebugCompat { get; private set; }
        public static ConfigEntry<bool> DebugProxy { get; private set; }
        public static ConfigEntry<bool> EnabledMod { get; private set; }
        public static ConfigEntry<bool> EnabledLighting { get; private set; }
        public static ConfigEntry<bool> EnabledEncountering { get; private set; }
        public static ConfigEntry<bool> EnabledFoliage { get; private set; }
        public static ConfigEntry<bool> EnabledGrasses { get; private set; }
        public static ConfigEntry<bool> EnabledBushRatting { get; private set; }
        public static ConfigEntry<bool> EnableMovementImpact { get; private set; }
        public static ConfigEntry<bool> EnableEquipmentCheck { get; private set; }
        public static ConfigEntry<bool> AlternativeReactionFluctuation { get; private set; }
        public static ConfigEntry<float> ScoreOffset { get; private set; }
        public static ConfigEntry<float> DarknessImpactScaleOffset { get; private set; }
        public static ConfigEntry<float> BrightnessImpactScaleOffset { get; private set; }
        public static float DarknessImpactScale => DarknessImpactScaleOffset.Value * 2f;
        public static float BrightnessImpactScale => BrightnessImpactScaleOffset.Value * 2f;
        public static ConfigEntry<float> ExtraDarknessImpactScale { get; private set; }
        public static ConfigEntry<float> ExtraBrightnessImpactScale { get; private set; }
        public static ConfigEntry<float> ExtraVisionDistanceScale { get; private set; }
        public static ConfigEntry<float> FinalOffset { get; private set; }
        public static ConfigEntry<float> FinalImpactScaleDelaying { get; private set; }
        public static ConfigEntry<float> FinalImpactScaleFastening { get; private set; }
        // public static ConfigEntry<float> VisibilityCancelChance { get; private set; }
        public static ConfigEntry<float> FoliageImpactScale { get; private set; }
        public static ConfigEntry<bool> IncludeBosses { get; private set; }
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
        public static ConfigEntry<bool> ShadowlessGroundZero { get; private set; }
        public static ConfigEntry<bool> ShadowlessStreets { get; private set; }
        public static ConfigEntry<bool> EnableBenchmark { get; private set; }
        // public static ConfigEntry<bool> AlternativeMeterUnicde { get; private set; }
        public static ConfigEntry<int> ResLevel { get; private set; }
        public static ConfigEntry<int> FoliageSamples { get; private set; }
        public static ConfigEntry<bool> VolumetricLightRenderer { get; private set; }
        public static ConfigEntry<bool> InterruptSAINNoBush { get; private set; }
        public static ConfigEntry<bool> PMCOnlyMode { get; private set; }
        public static ConfigEntry<bool> ForceBlindFireScatter { get; private set; }
        public static ConfigEntry<bool> BotLookDirectionTweaks { get; private set; }
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
            new InitiateShotMonitor().Enable();
            new BlindFirePatch().Enable();
            if (SAINLoaded)
                new SAINNoBushOverride().Enable();
        }

        private void Update()
        {
            GameWorldHandler.Update();
        }
    }
}