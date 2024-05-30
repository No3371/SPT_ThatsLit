using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using DrakiaXYZ.VersionChecker;
using System;
using UnityEngine;
using ThatsLit;
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
using Fika.Core.Coop.Matchmaker;

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

        public const int TarkovVersion = 29197;
        public const string EscapeFromTarkov = "EscapeFromTarkov.exe";
        public const string ModName = "That's Lit Sync";
        public const string ModVersion = "1.383.02";
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

    [BepInPlugin("bastudio.thatslit.sync", ModName, ModVersion)]
    [BepInDependency(SPTGUID, SPTVersion)]
    [BepInDependency("bastudio.thatslit", "1.383.08")]
    [BepInDependency("com.fika.core", "0.0.0")]
    [BepInProcess(EscapeFromTarkov)]
    public class ThatsLitSyncPlugin : BaseUnityPlugin
    {
        NetDataWriter writer = new NetDataWriter();
        public static Dictionary<int, Player> ActivePlayers = new Dictionary<int, Player>();
        void Awake()
        {
            if (!VersionChecker.CheckEftVersion(Logger, base.Info, Config))
            {
                throw new Exception("Invalid EFT Version");
            }

            FikaEventDispatcher.SubscribeEvent<FikaGameCreatedEvent>(OnFikaGameCreated);
            FikaEventDispatcher.SubscribeEvent<FikaServerCreatedEvent>(OnFikaServerCreated);
            FikaEventDispatcher.SubscribeEvent<FikaClientCreatedEvent>(OnFikaClientCreated);

            ThatsLitAPI.OnBeforePlayerSetupDirect += OnBeforePlayerSetupDirect;
            ThatsLitAPI.OnPlayerBrightnessScoreCalculatedDirect += OnPlayerBrightnessScoreCalculatedDirect;
        }

        void Update ()
        {
            var tlWorld = Singleton<ThatsLitGameworld>.Instance;
            if (tlWorld == null || Time.frameCount % 20 == 0) return;
            foreach (var p in tlWorld.AllThatsLitPlayers)
            {
                if (p.Value.Player is ObservedCoopPlayer)
                {
                    ThatsLitAPI.ToggleBrightnessProxyDirect(p.Value, true);
                }
            }
        }

        void OnFikaGameCreated (FikaGameCreatedEvent ev)
        {
            ActivePlayers.Clear();
        }

        void OnFikaServerCreated (FikaServerCreatedEvent ev)
        {
            ev.Server.packetProcessor.SubscribeNetSerializable<ScorePacket, NetPeer>((packet, peer) => {
                if (!ActivePlayers.TryGetValue(packet.netId, out var player))
                    return;
                
                ThatsLitAPI.TrySetPlayerScore(player, packet.score, packet.ambienceScore);
                Singleton<FikaServer>.Instance.SendDataToAll(writer, ref packet, DeliveryMethod.Unreliable);
            });
        }

        void OnFikaClientCreated(FikaClientCreatedEvent ev)
        {
            ev.Client.packetProcessor.SubscribeNetSerializable<ScorePacket>((packet) => {
                if (!ActivePlayers.TryGetValue(packet.netId, out var player))
                    return;
                
                ThatsLitAPI.TrySetPlayerScore(player, packet.score, packet.ambienceScore);
            });
        }

        void OnBeforePlayerSetupDirect(ThatsLitPlayer player)
        {
            if (player.Player is CoopPlayer coopPlayer == false || player.Player.IsAI)
                return;

            ActivePlayers.Add(coopPlayer.NetId, coopPlayer);
            if (player.Player is ObservedCoopPlayer)
            {
                ThatsLitAPI.ToggleBrightnessProxyDirect(player, true);
            }
        }

        void OnPlayerBrightnessScoreCalculatedDirect(ThatsLitPlayer player, float score, float ambScore)
        {
            if (MatchmakerAcceptPatches.IsServer || MatchmakerAcceptPatches.IsSinglePlayer) return;
            if (player.Player is CoopPlayer coopPlayer && player.Player.IsYourPlayer)
            {
                var packet = new ScorePacket(coopPlayer.NetId, score, ambScore);
                Singleton<FikaClient>.Instance.SendData(writer, ref packet, DeliveryMethod.Unreliable);
            }
        }
    }
}