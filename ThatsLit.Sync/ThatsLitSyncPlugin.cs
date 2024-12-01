﻿using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using System;
using UnityEngine;
using static ThatsLit.Sync.AssemblyInfo;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Modding;
using Fika.Core.Coop.Players;
using LiteNetLib;
using Comfort.Common;
using LiteNetLib.Utils;
using System.Collections.Generic;
using EFT;
using Fika.Core.Coop.Utils;

namespace ThatsLit.Sync
{
    public static class AssemblyInfo
    {
        public const string Title = ModName;
        public const string Description = "The plugin syncs That's Lit player states between players.";
        public const string Configuration = SPTVersion;
        public const string Company = "";
        public const string Product = ModName;
        public const string Copyright = "Copyright © 2024 BA";
        public const string Trademark = "";
        public const string Culture = "";

        public const int TarkovVersion = 33420;
        public const string EscapeFromTarkov = "EscapeFromTarkov.exe";
        public const string ModName = "That's Lit Sync";
        public const string ModVersion = "1.3100.0";
        public const string SPTGUID = "com.SPT.core";
        public const string SPTVersion = "3.10.0";
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

    [BepInPlugin("bastudio.thatslit.sync", ModName, ModVersion)]
    [BepInDependency(SPTGUID, SPTVersion)]
    [BepInDependency("bastudio.thatslit", "1.3100.0")]
    [BepInDependency("com.fika.core", "0.0.0")]
    [BepInProcess(EscapeFromTarkov)]
    [DefaultExecutionOrder(100)]
    [BepInDependency("bastudio.updatenotifier", BepInDependency.DependencyFlags.SoftDependency)]
    public class ThatsLitSyncPlugin : BaseUnityPlugin
    {
        public static Dictionary<int, Player> ActivePlayers = new Dictionary<int, Player>();
        public static ConfigEntry<bool> ShowInfo;
        public static ConfigEntry<bool> DebugLog;
        public static ConfigEntry<bool> LogPackets;
        float lastScore, lastAmbscore, lastSent;
        void Awake()
        {
            ShowInfo = Config.Bind<bool>("Main", "Show Info", true);
            LogPackets = Config.Bind<bool>("Main", "LogPackets", false);
            DebugLog = Config.Bind<bool>("Main", "DebugLog", false);

            FikaEventDispatcher.SubscribeEvent<FikaGameCreatedEvent>(OnFikaGameCreated);
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent >(OnFikaNetworkManagerCreated);

            ThatsLitAPI.OnGameWorldDestroyed += OnGameWorldDestroyed;
            ThatsLitAPI.ShouldSetupPlayer += ShouldSetupPlayer;
            ThatsLitAPI.OnBeforePlayerSetupDirect += OnBeforePlayerSetupDirect;
            ThatsLitAPI.OnPlayerBrightnessScoreCalculatedDirect += OnPlayerBrightnessScoreCalculatedDirect;
            ThatsLitAPI.OnMainPlayerGUI += OnMainPlayerGUI;

            TryCheckUpdate();
        }

        public void TryCheckUpdate ()
        {
            var url = "https://raw.githubusercontent.com/No3371/SPT_ThatsLit/main/ThatsLit.Sync/.update_notifier";
            if (!Chainloader.PluginInfos.TryGetValue("bastudio.updatenotifier", out var pluginInfo))
            {
                Logger.LogInfo("Update Notifier not found.");
                return;
            }

            BaseUnityPlugin updntf = pluginInfo.Instance;
            updntf.GetType().GetMethod("CheckForUpdate", new Type[] { typeof(BaseUnityPlugin), typeof(string)}).Invoke(updntf, new object[] {this, url});
        }

        void Update ()
        {
            var tlWorld = Singleton<ThatsLitGameworld>.Instance;
            if (tlWorld == null || Time.frameCount % 20 > 0) return;
            foreach (var p in tlWorld.AllThatsLitPlayers)
            {
                if (p.Value == null || p.Value.Player.IsYourPlayer) continue;
                if (p.Value.Player is ObservedCoopPlayer
                 && !ThatsLitAPI.IsBrightnessProxy(p.Value.Player))
                {
                    ThatsLitAPI.ToggleBrightnessProxyDirect(p.Value, true);
                    Logger.LogError($"[That's Lit Sync] Brightness proxy for player {p.Value.Player.Profile.Nickname} was not properly enabled");
                }
            }
        }

        bool ShouldSetupPlayer (Player player)
        {
            // Logger.LogInfo($"[That's Lit Sync] Setting up player: {player.gameObject.name} ({player.Profile.Nickname})?");
            if (player.IsYourPlayer)
            {
                if (Application.isBatchMode)
                {
                    // Logger.LogInfo($"[That's Lit Sync] This player {player.gameObject.name} ({player.Profile.Nickname}) is running in batch mode, skipping...");
                    return false;
                }
                return true; // Local
            }
            if (player is ObservedCoopPlayer coopPlayer && !coopPlayer.IsObservedAI)
            {
                return true; // Non AI remote players
            }
            if (player.IsAI || player.AIData.BotOwner != null || !player.gameObject.name.StartsWith("Player_"))
            {
                return false;
            }
            return false;
        }

        void OnMainPlayerGUI ()
        {
            if (!ShowInfo.Value)
                return;
            var tlWorld = Singleton<ThatsLitGameworld>.Instance;
            if (tlWorld == null)
                return;
            if (tlWorld.AllThatsLitPlayers.Count <= 1)
                return;
            GUILayout.Label($"  [That's Lit Sync]");
            if (!ThatsLitPlugin.EnabledLighting.Value)
            {
                GUILayout.Label("  !Brightness Module OFF!");
            }
            // GUILayout.Label($"  Tracking {ActivePlayers.Count} players");
            foreach (var p in tlWorld.AllThatsLitPlayers)
            {
                if (p.Value == null) continue;
                if (p.Value.Player is ObservedCoopPlayer coopPlayer)
                {
                    if (ThatsLitAPI.IsBrightnessProxy(p.Value.Player))
                        GUILayout.Label($"  {p.Value.Player.Profile.Nickname}");
                    else
                        GUILayout.Label($"  {p.Value.Player.Profile.Nickname} !Non-Proxy!");
                    Utility.GUILayoutDrawAsymetricMeter((int)(ThatsLitAPI.GetBrightnessScoreDirect(p.Value) / 0.0999f));
                }
            }
        }


        private void OnGameWorldDestroyed()
        {
            ActivePlayers.Clear();
        }

        void OnFikaGameCreated (FikaGameCreatedEvent ev)
        {
            if (!ThatsLitPlugin.EnabledLighting.Value)
            {
                string message = $"[That's Lit Sync] Brightness is disabled! Will always send neutral score.";
                NotificationManagerClass.DisplayWarningNotification(message);
                Logger.LogError(message);
                EFT.UI.ConsoleScreen.Log(message);
            }
        }

        void OnFikaNetworkManagerCreated (FikaNetworkManagerCreatedEvent ev)
        {
            switch (ev.Manager)
            {
                case FikaServer server:
                    server.RegisterPacket<ScorePacket, NetPeer>(HandlePacketServer);
                break;
                case FikaClient client:
                    client.RegisterPacket<ScorePacket>(HandlePacketClient);
                break;
            }
        }

        void HandlePacketServer (ScorePacket packet, NetPeer peer)
        {
            if (!ActivePlayers.TryGetValue(packet.netId, out var player)
                || player.IsYourPlayer)
                return;

            ThatsLitAPI.TrySetProxyBrightnessScore(player, packet.score, packet.ambienceScore);
            BroadcastScore(ref packet, peer);
            if (LogPackets.Value)
                Logger.LogInfo($"[That's Lit Sync] [Redirect] Broadcasting #{ packet.netId } {packet.score}/{packet.ambienceScore} at f{Time.frameCount}");
        }

        void HandlePacketClient (ScorePacket packet)
        {
            if (!ActivePlayers.TryGetValue(packet.netId, out var player)
             || player.IsYourPlayer) // Don't take broadcasted back packet
                return;
            
            if (LogPackets.Value) Logger.LogInfo($"[That's Lit Sync] Received #{ packet.netId } {packet.score}/{packet.ambienceScore} at f{Time.frameCount}");
            ThatsLitAPI.TrySetProxyBrightnessScore(player, packet.score, packet.ambienceScore);
        }

        void OnBeforePlayerSetupDirect(ThatsLitPlayer player)
        {
            if (DebugLog.Value) Logger.LogInfo($"[That's Lit Sync] Player setup attempt: { player.Player.Profile.Nickname }");

            CoopPlayer coopPlayer = player.Player as CoopPlayer;
            if (coopPlayer == null) // Not Fika types
                return;

            // Dont't use IsAI as it's not set by Fika
            if (player.Player is CoopBot) // Bots on server (local)
                return;

            if (player.Player is ObservedCoopPlayer observed && observed.IsObservedAI) // Remote bots on clients
                return;

            if (DebugLog.Value)
                Logger.LogInfo($"[That's Lit Sync] Player setup condition passed: { player.Player.Profile.Nickname } #{ coopPlayer.NetId } at f{Time.frameCount}");
            ActivePlayers.Add(coopPlayer.NetId, coopPlayer);
            if (!player.Player.IsYourPlayer)
            {
                ThatsLitAPI.ToggleBrightnessProxyDirect(player, true);
                if (DebugLog.Value)
                    Logger.LogInfo($"[That's Lit Sync] Remote Player set to proxy: { player.Player.Profile.Nickname } #{ coopPlayer.NetId } at f{Time.frameCount}");
            }
            else if (DebugLog.Value)
                Logger.LogInfo($"[That's Lit Sync] Local player setup: { player.Player.Profile.Nickname } #{ coopPlayer.NetId } at f{Time.frameCount}");
            
        }

        void OnPlayerBrightnessScoreCalculatedDirect(ThatsLitPlayer player, float score, float ambScore)
        {
            if (FikaBackendUtils.IsSinglePlayer) return;

            CoopPlayer coopPlayer = player.Player as CoopPlayer;
            if (FikaBackendUtils.IsServer && coopPlayer != null && coopPlayer.IsYourPlayer)
            {
                var packet = new ScorePacket(coopPlayer.NetId, score, ambScore);
                if (LogPackets.Value) Logger.LogInfo($"[That's Lit Sync] [On Calc] Broadcasting #{ coopPlayer.NetId } {score}/{ambScore} at f{Time.frameCount}");
                BroadcastScore(ref packet);
            }
            else if (FikaBackendUtils.IsClient && coopPlayer != null && coopPlayer.IsYourPlayer)
            {
                var packet = new ScorePacket(coopPlayer.NetId, score, ambScore);
                if (Mathf.Abs(lastScore - score) + Mathf.Abs(lastAmbscore - ambScore) > (score < -0.7f? 0.015f : 0.03f) || Time.time - lastSent > 1f)
                {
                    Singleton<FikaClient>.Instance.SendData(ref packet, DeliveryMethod.Unreliable);
                    lastSent = Time.time;
                    lastScore = score;
                    lastAmbscore = ambScore;
                    if (LogPackets.Value) Logger.LogInfo($"[That's Lit Sync] [On Calc] Uploading #{ coopPlayer.NetId } {score}/{ambScore} at f{Time.frameCount}");
                }
                if (LogPackets.Value) Logger.LogInfo($"[That's Lit Sync] [On Calc] Uploading throttled for #{ coopPlayer.NetId } {score}/{ambScore} at f{Time.frameCount}");
            }
        }

        void BroadcastScore (ref ScorePacket packet, NetPeer peer)
        {
            Singleton<FikaServer>.Instance.SendDataToAll(ref packet, DeliveryMethod.Unreliable, peer);
        }
    }
}