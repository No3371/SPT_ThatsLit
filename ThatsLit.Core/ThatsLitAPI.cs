using System;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace ThatsLit
{
    public static class ThatsLitAPI
    {
        public static bool IsBrightnessProxyDirect (ThatsLitPlayer player)
        {
            return player.PlayerLitScoreProfile?.IsProxy ?? false;
        }
        public static bool IsBrightnessProxy (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return false;
            return tlp.PlayerLitScoreProfile?.IsProxy ?? false;
        }

        public static void ToggleBrightnessProxyDirect (ThatsLitPlayer player, bool toggle)
        {
            if (player.PlayerLitScoreProfile == null)
                player.PlayerLitScoreProfile = new PlayerLitScoreProfile(player);
            if (player.PlayerLitScoreProfile.IsProxy == toggle)
                return;
            player.ToggleBrightnessProxy(toggle);
        }

        public static void ToggleBrightnessProxy (Player player, bool toggle)
        {
            if (Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return;

            ToggleBrightnessProxyDirect(tlp, toggle);
        }
        public static Action<ThatsLitPlayer> OnBeforePlayerSetupDirect;
        public static void TrySetProxyBrightnessScoreDirect(ThatsLitPlayer player, float score, float ambienceScore)
        {
            if (player.PlayerLitScoreProfile?.IsProxy != true) return;

            player.PlayerLitScoreProfile.frame0.multiFrameLitScore = score;
            player.PlayerLitScoreProfile.frame0.ambienceScore = ambienceScore;
        }
        public static Action<Player> OnBeforePlayerSetup;
        public static void TrySetProxyBrightnessScore(Player player, float score, float ambienceScore)
        {
            if (Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return;

            TrySetProxyBrightnessScoreDirect(tlp, score, ambienceScore);
        }

        public static float GetBrightnessScore (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return 0;

            return GetBrightnessScoreDirect(tlp);
        }

        public static float GetAmbienceBrightnessScore (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return 0;

            return GetAmbienceBrightnessScoreDirect(tlp);
        }

        public static float GetBrightnessScoreDirect (ThatsLitPlayer player)
        {
            return player.PlayerLitScoreProfile?.frame0.multiFrameLitScore ?? 0;
        }

        public static float GetAmbienceBrightnessScoreDirect (ThatsLitPlayer player)
        {
            return player.PlayerLitScoreProfile?.frame0.ambienceScore ?? 0;
        }

        public static float GetFoliageScore (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return 0;

            return GetFoliageScoreDirect(tlp);
        }

        public static float GetFoliageScoreDirect (ThatsLitPlayer player)
        {
            return player.Foliage?.FoliageScore ?? 0;
        }
        public static Func<Player, bool> ShouldSetupPlayer;
        public static Action<ThatsLitGameworld> OnGameWorldSetup;
        public static Action OnGameWorldDestroyed;
        public static Action OnMainPlayerGUI;
        public static Action<ThatsLitPlayer, float, float> OnPlayerBrightnessScoreCalculatedDirect;
        public static Action<Player, float, float> OnPlayerBrightnessScoreCalculated;
        public static Action<ThatsLitPlayer> OnPlayerSurroundingTerrainSampledDirect;
        public static Action<Player> OnPlayerSurroundingTerrainSampled;
        public static int GetTerrainDetailCount3x3Direct (ThatsLitPlayer player)
        {
            return player.TerrainDetails?.RecentDetailCount3x3 ?? 0;
        }
        public static int GetTerrainDetailCount5x5Direct (ThatsLitPlayer player)
        {
            return player.TerrainDetails?.RecentDetailCount5x5 ?? 0;
        }
        public static int GetTerrainDetailCount3x3 (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return 0;

            return GetTerrainDetailCount3x3Direct(tlp);
        }
        public static int GetTerrainDetailCount5x5 (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return 0;

            return GetTerrainDetailCount5x5Direct(tlp);
        }
        public static float GetTerrainDetailScoreCenter3x3Direct (ThatsLitPlayer player)
        {
            if (player.TerrainDetails == null || player.Player == null)
                return 0f;
            var score = Singleton<ThatsLitGameworld>.Instance.CalculateDetailScore(player.TerrainDetails, Vector3.zero, 0, 0);
            if (player.Player.IsInPronePose)
                return score.prone;

            var pf = Utility.GetPoseFactor(player.Player.PoseLevel, player.Player.Physical.MaxPoseLevel, player.Player.IsInPronePose);
            return Utility.GetPoseWeightedRegularTerrainScore(pf, score);
        }
        public static LightAndLaserState GetLightAndLaserStateDirect (ThatsLitPlayer player)
        {
            return player.LightAndLaserState;
        }
        public static bool AnyVisibleLight (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.AnyVisibleLight;
            return false;
        }
        public static bool AnyVisibleLaser (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.AnyVisibleLaser;
            return false;
        }
        public static bool AnyIRLight (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.AnyIRLight;
            return false;
        }
        public static bool AnyIRLaser (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.AnyIRLaser;
            return false;
        }
        public static bool AnyVisibleMain (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.AnyVisibleMain;
            return false;
        }
        public static bool AnyVisibleSub (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.AnyVisibleSub;
            return false;
        }
        public static bool AnyIRMain (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.AnyIRMain;
            return false;
        }
        public static bool AnyIRSub (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.AnyIRSub;
            return false;
        }
        public static float GetMainVisibleLight (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.deviceStateCache.light;
            return 0;
        }
        public static float GetMainVisibleLaser (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.deviceStateCache.laser;
            return 0;
        }
        public static float GetMainIRLight (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.deviceStateCache.irLight;
            return 0;
        }
        public static float GetMainIRLaser (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.deviceStateCache.irLaser;
            return 0;
        }
        public static float GetSheathedVisibleLight (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.deviceStateCacheSub.light;
            return 0;
        }
        public static float GetSheathedVisibleLaser (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.deviceStateCacheSub.laser;
            return 0;
        }
        public static float GetSheathedIRLight (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.deviceStateCacheSub.irLight;
            return 0;
        }
        public static float GetSheathedIRLaser (Player player)
        {
            if (Singleton<ThatsLitGameworld>.Instance.AllThatsLitPlayers.TryGetValue(player, out ThatsLitPlayer tlp))
                return tlp.LightAndLaserState.deviceStateCacheSub.irLaser;
            return 0;
        }
    }

}
