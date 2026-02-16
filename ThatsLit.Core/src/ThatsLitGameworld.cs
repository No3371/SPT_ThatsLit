using Comfort.Common;
using EFT;
using ThatsLit.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using System.Collections;

using BaseCellClass = GClass1187;
using CellClass = GClass1188;
using SpatialPartitionClass = GClass1202<GClass1187>;
using System.Reflection;
using GPUInstancer;
using HarmonyLib;
using System.Diagnostics;
using System.Text.RegularExpressions;
using EFT.Weather;

namespace ThatsLit
{
    public class ThatsLitGameworld : MonoBehaviour
    {
        private void Update()
        {
            if (!Singleton<GameWorld>.Instantiated) return;
            if (!ThatsLitPlayer.CanLoad()) return;
            for (int i = 0; i < GameWorld.AllAlivePlayersList.Count; i++)
            {
                Player player = GameWorld.AllAlivePlayersList[i];
                if (player.IsAI) continue;
                if (!AllThatsLitPlayers.ContainsKey(player))
                {
                    TrySetupPlayer(player);
                }
            }
        }

        internal void TrySetupPlayer (Player player)
        {
            if (ThatsLitAPI.ShouldSetupPlayer != null && ThatsLitAPI.ShouldSetupPlayer.Invoke(player) == false)
            {
                return;
            }
            var tlp = player.gameObject.AddComponent<ThatsLitPlayer>();
            AllThatsLitPlayers.Add(player, tlp);
            tlp.Player = player;
            ThatsLitAPI.OnBeforePlayerSetup?.Invoke(player);
            ThatsLitAPI.OnBeforePlayerSetupDirect?.Invoke(tlp);
            if (ThatsLitPlugin.DebugProxy.Value)
                ThatsLitAPI.ToggleBrightnessProxyDirect(tlp, true);
            tlp.Setup(this);
            if (player == GameWorld.MainPlayer) MainThatsLitPlayer = tlp;
        }
        private void OnDestroy()
        {
            try
            {
            
                foreach (var p in AllThatsLitPlayers)
                {
                    ComponentHelpers.DestroyComponent(p.Value);
                }
            }
            catch
            {
                Logger.LogError("Dispose Component Error");
            }

            if (Singleton<ThatsLitGameworld>.Instance == this)
                Singleton<ThatsLitGameworld>.Instance = null;
            ThatsLitAPI.OnGameWorldDestroyed?.Invoke();
        }
        public GameWorld GameWorld => Singleton<GameWorld>.Instance;
        public ThatsLitPlayer MainThatsLitPlayer { get; private set; }
        public Dictionary<IPlayer, ThatsLitPlayer> AllThatsLitPlayers { get; private set; }
        public ScoreCalculator ScoreCalculator { get; internal set; }
        public RaidSettings activeRaidSettings;
        public bool IsWinter { get; private set; }
        internal bool foliageUnavailable, terrainDetailsUnavailable;
        internal Dictionary<(string, string), DoubleIdThrottler> doubleIdThrottlers;
        internal Dictionary<string, SingleIdThrottler> singleIdThrottlers;

        internal struct DoubleIdThrottler
        {
        }
        internal struct SingleIdThrottler
        {
            internal float lastAddedDangerPoint;
            internal float lastSideLook, lastForceLook;
        }

        void Awake ()
        {
            foliageLayerMask = 1 << LayerMask.NameToLayer("Foliage") | 1 << LayerMask.NameToLayer("Grass") | 1 << LayerMask.NameToLayer("PlayerSpiritAura");
            singleIdThrottlers = new();
            doubleIdThrottlers = new();

            Singleton<ThatsLitGameworld>.Instance = this;

            var session = (TarkovApplication)Singleton<ClientApplication<ISession>>.Instance;
            if (session == null) throw new Exception("No session!");
            IsWinter = session.Session.Season == ESeason.Winter;
            activeRaidSettings = (RaidSettings)(typeof(TarkovApplication).GetField("_raidSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session));

            switch (activeRaidSettings?.LocationId)
            {
                case "factory4_night":
                case "factory4_day":
                case "laboratory":
                case null:
                    foliageUnavailable = true;
                    terrainDetailsUnavailable = true;
                    break;
                default:
                    foliageUnavailable = false;
                    terrainDetailsUnavailable = false;
                    break;
            }

            AllThatsLitPlayers = new Dictionary<IPlayer, ThatsLitPlayer>();

            switch (activeRaidSettings?.LocationId)
            {
                case "Lighthouse":
                    if (ThatsLitPlugin.EnableLighthouse.Value) ScoreCalculator = new LighthouseScoreCalculator();
                    break;
                case "Woods":
                    if (ThatsLitPlugin.EnableWoods.Value) ScoreCalculator = new WoodsScoreCalculator();
                    break;
                case "factory4_night":
                    if (ThatsLitPlugin.EnableFactoryNight.Value) ScoreCalculator = new NightFactoryScoreCalculator();
                    break;
                case "factory4_day":
                    ScoreCalculator = null;
                    break;
                case "bigmap": // Customs
                    if (ThatsLitPlugin.EnableCustoms.Value) ScoreCalculator = new CustomsScoreCalculator();
                    break;
                case "RezervBase": // Reserve
                    if (ThatsLitPlugin.EnableReserve.Value) ScoreCalculator = new ReserveScoreCalculator();
                    break;
                case "Interchange":
                    if (ThatsLitPlugin.EnableInterchange.Value) ScoreCalculator = new InterchangeScoreCalculator();
                    break;
                case "TarkovStreets":
                    if (ThatsLitPlugin.EnableStreets.Value) ScoreCalculator = new StreetsScoreCalculator();
                    break;
                case "Sandbox":
                case "Sandbox_high":
                    if (ThatsLitPlugin.EnableGroundZero.Value) ScoreCalculator = new GroundZeroScoreCalculator();
                    break;
                case "Shoreline":
                    if (ThatsLitPlugin.EnableShoreline.Value) ScoreCalculator = new ShorelineScoreCalculator();
                    break;
                case "laboratory":
                    // if (ThatsLitPlugin.EnableLab.Value) ScoreCalculator = new LabScoreCalculator();
                    break;
                case null:
                    if (ThatsLitPlugin.EnableHideout.Value) ScoreCalculator = new HideoutScoreCalculator();
                    break;
                default:
                    break;
            }

            ThatsLitAPI.OnGameWorldSetup?.Invoke(this);
        }


        internal void GetWeatherStats(out float fog, out float rain, out float cloud)
        {
            if (WeatherController.Instance?.WeatherCurve == null)
            {
                fog = rain = cloud = 0;
                return;
            }

            fog = WeatherController.Instance.WeatherCurve.Fog;
            rain = WeatherController.Instance.WeatherCurve.Rain;
            cloud = WeatherController.Instance.WeatherCurve.Cloudiness;
        }
        private LayerMask foliageLayerMask;// PlayerSpiritAura is Visceral Bodies compat
        
        internal void UpdateFoliageScore(Vector3 bodyPos, PlayerFoliageProfile player)
        {
            if (player == null) return;

            if (Time.time < player.LastCheckedTime + 0.45f)
                return;
            // Skip if basically standing still
            if ((bodyPos - player.LastCheckedPos).magnitude < 0.05f)
                return;

            player.FoliageScore = 0;
            player.LastCheckedTime = Time.time;
            player.LastCheckedPos = bodyPos;

            Array.Clear(player.Foliage, 0, player.Foliage.Length);
            Array.Clear(player.CastedFoliageColliders, 0, player.CastedFoliageColliders.Length);

            if (foliageUnavailable) return;

            int castedCount = Physics.OverlapSphereNonAlloc(bodyPos, 4f, player.CastedFoliageColliders, foliageLayerMask);
            int validCount = 0;

            for (int i = 0; i < castedCount; i++)
            {
                Collider casted = player.CastedFoliageColliders[i];
                if (casted.gameObject.transform.root.gameObject.layer == 8) continue; // Somehow sometimes player spines are tagged PlayerSpiritAura, VB or vanilla?
                if (casted.gameObject.GetComponent<Terrain>()) continue; // Somehow sometimes terrains can be casted
                Vector3 bodyToFoliage = casted.transform.position - bodyPos;

                float dis = bodyToFoliage.magnitude;
                if (dis < 0.25f) player.FoliageScore += 1f;
                else if (dis < 0.35f) player.FoliageScore += 0.9f;
                else if (dis < 0.5f) player.FoliageScore += 0.8f;
                else if (dis < 0.6f) player.FoliageScore += 0.7f;
                else if (dis < 0.7f) player.FoliageScore += 0.5f;
                else if (dis < 1f) player.FoliageScore += 0.3f;
                else if (dis < 2f) player.FoliageScore += 0.2f;
                else player.FoliageScore += 0.1f;

                string fname = casted?.transform.parent.gameObject.name;
                if (string.IsNullOrWhiteSpace(fname)) continue;

                if (ThatsLitPlugin.FoliageSamples.Value == 1 && (player.Foliage[0] == default || dis < player.Foliage[0].dis)) // don't bother
                {
                    player.Foliage[0] = new FoliageInfo(fname, new Vector3(bodyToFoliage.x, bodyToFoliage.z), dis);
                    validCount = 1;
                    continue;
                }
                else player.Foliage[validCount] = new FoliageInfo(fname, new Vector3(bodyToFoliage.x, bodyToFoliage.z), dis);
                validCount++;
            }

            for (int j = 0; j < validCount; j++)
            {
                var f = player.Foliage[j];
                f.name = Regex.Replace(f.name, @"(.+?)\s?(\(\d+\))?", "$1");
                f.dis = f.dir.magnitude; // Use horizontal distance to replace casted 3D distance
                player.Foliage[j] = f;
            }
            player.IsFoliageSorted = false;
            if (player.Foliage.Length == 1 || validCount == 1)
            {
                player.IsFoliageSorted = true;
            }

            switch (castedCount)
            {
                case 1:
                    player.FoliageScore /= 3.3f;
                    break;
                case 2:
                    player.FoliageScore /= 2.8f;
                    break;
                case 3:
                    player.FoliageScore /= 2.3f;
                    break;
                case 4:
                    player.FoliageScore /= 1.8f;
                    break;
                case 5:
                case 6:
                    player.FoliageScore /= 1.2f;
                    break;
                case 11:
                case 12:
                case 13:
                    player.FoliageScore /= 1.15f;
                    break;
                case 14:
                case 15:
                case 16:
                    player.FoliageScore /= 1.25f;
                    break;
            }

            player.FoliageCount = validCount;
        }

        internal int MaxDetailTypes { get; set; }


        /// <summary>
        /// Calculate new or retrieve cached score for the specified enemy dir, dis, vertical vision angle
        /// </summary>
        public TerrainDetailScore CalculateDetailScore(PlayerTerrainDetailsProfile player, Vector3 enemyDirection, float dis, float verticalAxisAngle)
        {
            int dir = 5;
            IEnumerable<int> it = null;
            TerrainDetailScore cache = default;
            float scaling = 1f;

            if (player?.Details5x5 == null) return cache; // Could be resizing? 

            if (enemyDirection == Vector3.zero) // This should never happens for actual enemies
            {
                if (TryGetCache(dir = 5, out cache)) return cache;
                it = IterateDetailIndex3x3;
            }
            else if (verticalAxisAngle < -20f) // Looking down and ignore distance and direction
            {
                if (dis >= 10)
                {
                    if (TryGetCache(dir = 5, out cache)) return cache;
                    it = IterateDetailIndex3x3;
                }
                else
                {
                    if (TryGetCache(dir = 15, out cache)) return cache; // scaled down mid 3x3
                    it = IterateDetailIndex3x3;
                    scaling = 4f/9f;
                }
            }
            else if (dis < 10f && verticalAxisAngle < -10f) // Very close and not looking down
            {
                var dirFlat = new Vector2(enemyDirection.x, enemyDirection.z).normalized;
                var angle = Vector2.SignedAngle(Vector2.up, dirFlat);
                if (angle >= -22.5f && angle <= 22.5f)
                {
                    if (TryGetCache(dir = 10 + 8, out cache))  return cache;
                    it = IterateDetailIndex2x3InversedTN;
                }
                else if (angle >= 22.5f && angle <= 67.5f)
                {
                    if (TryGetCache(dir = 10 + 9, out cache))  return cache;
                    it = IterateDetailIndex2x2NE;
                }
                else if (angle >= 67.5f && angle <= 112.5f)
                {
                    if (TryGetCache(dir = 10 + 6, out cache))  return cache;
                    it = IterateDetailIndex2x3InversedTE;
                }
                else if (angle >= 112.5f && angle <= 157.5f)
                {
                    if (TryGetCache(dir = 10 + 3, out cache))  return cache;
                    it = IterateDetailIndex2x2SE;
                }
                else if (angle >= 157.5f && angle <= 180f || angle >= -180f && angle <= -157.5f)
                {
                    if (TryGetCache(dir = 10 + 2, out cache))  return cache;
                    it = IterateDetailIndex2x3InversedTS;
                }
                else if (angle >= -157.5f && angle <= -112.5f)
                {
                    if (TryGetCache(dir = 10 + 1, out cache))  return cache;
                    it = IterateDetailIndex2x2SW;
                }
                else if (angle >= -112.5f && angle <= -67.5f)
                {
                    if (TryGetCache(dir = 10 + 4, out cache))  return cache;
                    it = IterateDetailIndex2x3InversedTW;
                }
                else if (angle >= -67.5f && angle <= -22.5f)
                {
                    if (TryGetCache(dir = 10 + 7, out cache))  return cache;
                    it = IterateDetailIndex2x2NW;
                }

                scaling = 9f/4f;
            }
            else
            {
                var dirFlat = new Vector2(enemyDirection.x, enemyDirection.z).normalized;
                var angle = Vector2.SignedAngle(Vector2.up, dirFlat);
                if (angle >= -22.5f && angle <= 22.5f)
                {
                    if (TryGetCache(dir = 8, out cache))  return cache;
                    it = IterateDetailIndex3x3N;
                }
                else if (angle >= 22.5f && angle <= 67.5f)
                {
                    if (TryGetCache(dir = 9, out cache))  return cache;
                    it = IterateDetailIndex3x3NE;
                }
                else if (angle >= 67.5f && angle <= 112.5f)
                {
                    if (TryGetCache(dir = 6, out cache))  return cache;
                    it = IterateDetailIndex3x3E;
                }
                else if (angle >= 112.5f && angle <= 157.5f)
                {
                    if (TryGetCache(dir = 3, out cache))  return cache;
                    it = IterateDetailIndex3x3SE;
                }
                else if (angle >= 157.5f && angle <= 180f || angle >= -180f && angle <= -157.5f)
                {
                    if (TryGetCache(dir = 2, out cache))  return cache;
                    it = IterateDetailIndex3x3S;
                }
                else if (angle >= -157.5f && angle <= -112.5f)
                {
                    if (TryGetCache(dir = 1, out cache))  return cache;
                    it = IterateDetailIndex3x3SW;
                }
                else if (angle >= -112.5f && angle <= -67.5f)
                {
                    if (TryGetCache(dir = 4, out cache))  return cache;
                    it = IterateDetailIndex3x3W;
                }
                else if (angle >= -67.5f && angle <= -22.5f)
                {
                    if (TryGetCache(dir = 7, out cache))  return cache;
                    it = IterateDetailIndex3x3NW;
                }
                else throw new Exception($"[That's Lit] Invalid angle to enemy: {angle}");
            }

            foreach (var pos in it)
            {
                for (int i = 0; i < MaxDetailTypes; i++)
                {
                    var info = player.Details5x5[pos * MaxDetailTypes + i];
                    if (!info.casted) continue;
                    Utility.CalculateDetailScore(info.name, info.count, out var s1, out var s2);
                    s1 *= scaling;
                    s2 *= scaling;
                    cache.prone += s1;
                    cache.regular += s2;
                }
            }

            cache.cached = true;
            player.detailScoreCache[dir] = cache;
            return cache;

            bool TryGetCache(int index, out TerrainDetailScore cache)
            {
                cache = player.detailScoreCache[index];
                return cache.cached;
            }
        }

        public TerrainDetailScore CalculateCenterDetailScore(PlayerTerrainDetailsProfile player, bool unscaled = false)
        {
            TerrainDetailScore cache = default;
            float scaling = unscaled? 1f : 9f;
            if (TryGetCache(0, out cache))  return cache;

            if (player?.Details5x5 == null) return cache; // Could be resizing?

            for (int i = 0; i < MaxDetailTypes; i++)
            {
                var info = player.Details5x5[(2*5+2) * MaxDetailTypes + i];
                if (!info.casted) continue;
                Utility.CalculateDetailScore(info.name, info.count, out var s1, out var s2);
                s1 *= scaling;
                s2 *= scaling;
                cache.prone += s1;
                cache.regular += s2;
            }

            cache.cached = true;
            player.detailScoreCache[0] = cache;
            return cache;

            bool TryGetCache(int index, out TerrainDetailScore cache)
            {
                cache = player.detailScoreCache[index];
                return cache.cached;
            }
        }
        IEnumerable<int> IterateDetailIndex3x3N => IterateIndex3x3In5x5(0, 1);
        IEnumerable<int> IterateDetailIndex3x3E => IterateIndex3x3In5x5(1, 0);
        IEnumerable<int> IterateDetailIndex3x3W => IterateIndex3x3In5x5(-1, 0);
        IEnumerable<int> IterateDetailIndex3x3S => IterateIndex3x3In5x5(0, -1);
        IEnumerable<int> IterateDetailIndex3x3NE => IterateIndex3x3In5x5(1, 1);
        IEnumerable<int> IterateDetailIndex3x3NW => IterateIndex3x3In5x5(-1, 1);
        IEnumerable<int> IterateDetailIndex3x3SE => IterateIndex3x3In5x5(1, -1);
        IEnumerable<int> IterateDetailIndex3x3SW => IterateIndex3x3In5x5(-1, -1);
        IEnumerable<int> IterateDetailIndex2x3InversedTN => IterateIndexCrossShape3x3In5x5Center(horizontal: true, N: true);
        IEnumerable<int> IterateDetailIndex2x3InversedTE => IterateIndexCrossShape3x3In5x5Center(vertical: true, W: true);
        IEnumerable<int> IterateDetailIndex2x3InversedTW => IterateIndexCrossShape3x3In5x5Center(vertical: true, W: true);
        IEnumerable<int> IterateDetailIndex2x3InversedTS => IterateIndexCrossShape3x3In5x5Center(horizontal: true, S: true);
        IEnumerable<int> IterateDetailIndex2x2NE => IterateIndex2x2In5x5(2, 1);
        IEnumerable<int> IterateDetailIndex2x2NW => IterateIndex2x2In5x5(1, 1);
        IEnumerable<int> IterateDetailIndex2x2SE => IterateIndex2x2In5x5(2, 2);
        IEnumerable<int> IterateDetailIndex2x2SW => IterateIndex2x2In5x5(1, 2);
        IEnumerable<int> IterateDetailIndex3x3 => IterateIndex3x3In5x5(0, 0);

        /// xOffset and yOffset decides the center of the 3x3
        IEnumerable<int> IterateIndex3x3In5x5(int xOffset, int yOffset)
        {
            yield return 5 * (1 + yOffset) + 1 + xOffset;
            yield return 5 * (1 + yOffset) + 2 + xOffset;
            yield return 5 * (1 + yOffset) + 3 + xOffset;

            yield return 5 * (2 + yOffset) + 1 + xOffset;
            yield return 5 * (2 + yOffset) + 2 + xOffset;
            yield return 5 * (2 + yOffset) + 3 + xOffset;

            yield return 5 * (3 + yOffset) + 1 + xOffset;
            yield return 5 * (3 + yOffset) + 2 + xOffset;
            yield return 5 * (3 + yOffset) + 3 + xOffset;
        }

        /// xOffset and yOffset decides the LT corner of the 2x2
        IEnumerable<int> IterateIndex2x2In5x5(int xOffset, int yOffset)
        {
            if (xOffset > 3 || yOffset > 3) throw new Exception("[That's Lit] Terrain detail grid access out of Bound");
            yield return 5 * yOffset + xOffset;
            yield return 5 * yOffset + xOffset + 1;

            yield return 5 * (yOffset + 1) + xOffset;
            yield return 5 * (yOffset + 1) + xOffset + 1;
        }

        IEnumerable<int> IterateIndexCrossShape3x3In5x5Center(bool horizontal = false, bool vertical = false, bool N = false, bool E = false, bool S = false, bool W = false)
        {
            // yield return 5 * 1 + 1;
            if (vertical || N) yield return 5 * 1 + 2;
            // yield return 5 * 1 + 3;

            if (horizontal || W) yield return 5 * 2 + 1;
            if (horizontal || vertical) yield return 5 * 2 + 2;
            if (horizontal || E) yield return 5 * 2 + 3;

            // yield return 5 * 3 + 1;
            if (vertical || S) yield return 5 * 3 + 2;
            // yield return 5 * 3 + 3;
        }

        internal void CheckTerrainDetails(Vector3 position, PlayerTerrainDetailsProfile player)
        {
            if (!terrainDetailsUnavailable && GPUInstancerDetailManager.activeManagerList?.Count == 0)
            {
                terrainDetailsUnavailable = true;
                Logger.LogInfo($"Active detail managers not found, disabling detail check...");
                return;
            }

            if (player == null || terrainDetailsUnavailable)
                return ;

            if ((position - player.LastCheckedPos).magnitude < 0.1f)
                return ;

            Array.Clear(player.detailScoreCache, 0, player.detailScoreCache.Length);
            if (player.Details5x5 != null)
                Array.Clear(player.Details5x5, 0, player.Details5x5.Length);
            player.RecentDetailCount3x3 = 0;
            player.RecentDetailCount5x5 = 0;
            player.LastCheckedTime = Time.time;
            player.LastCheckedPos = position;

            var ray = new Ray(position, Vector3.down);
            if (!Physics.Raycast(ray, out var hit, 100, LayerMaskClass.TerrainMask)) return ;

            var terrain = hit.transform?.GetComponent<Terrain>();
            GPUInstancerDetailManager manager = terrain?.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;

            if (!terrain || !manager || !manager.isInitialized) return ;
            if (!terrainDetailMaps.TryGetValue(terrain, out var detailMap))
            {
                if (gatheringDetailMap == null)
                    gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(terrain));
                return;
            }

            Vector3 hitRelativePos = hit.point - (terrain.transform.position + terrain.terrainData.bounds.min);
            var currentLocationOnTerrainmap = new Vector2(hitRelativePos.x / terrain.terrainData.size.x, hitRelativePos.z / terrain.terrainData.size.z);

            if (player.Details5x5 == null) // Initialize
            {
                foreach (var mgr in GPUInstancerDetailManager.activeManagerList)
                {
                    if (MaxDetailTypes < mgr.prototypeList.Count)
                    {
                        MaxDetailTypes = mgr.prototypeList.Count + 2;
                    }
                }
                player.Details5x5 = new CastedDetailInfo[MaxDetailTypes * 5 * 5];
                Logger.LogInfo($"Set MaxDetailTypes to {MaxDetailTypes}");
            }

            if (MaxDetailTypes < manager.prototypeList.Count)
            {
                MaxDetailTypes = manager.prototypeList.Count + 2;
                player.Details5x5 = new CastedDetailInfo[MaxDetailTypes * 5 * 5];
            }

            for (int d = 0; d < manager.prototypeList.Count; d++)
            {
                GPUInstancerDetailPrototype gPUInstancerDetailPrototype = manager.prototypeList[d] as GPUInstancerDetailPrototype;
                if (gPUInstancerDetailPrototype == null) continue;
                var resolution = gPUInstancerDetailPrototype.detailResolution;
                Vector2Int resolutionPos = new Vector2Int((int)(currentLocationOnTerrainmap.x * resolution), (int)(currentLocationOnTerrainmap.y * resolution));
                // EFT.UI.ConsoleScreen.Log($"JOB: Calculating score for detail#{d} at detail pos ({resolutionPos.x},{resolutionPos.y})" );
                for (int x = 0; x < 5; x++)
                    for (int y = 0; y < 5; y++)
                    {
                        var posX = resolutionPos.x - 2 + x;
                        var posY = resolutionPos.y - 2 + y;
                        int count = 0;

                        if (posX < 0 && terrain.leftNeighbor && posY >= 0 && posY < resolution)
                        {
                            Terrain neighbor = terrain.leftNeighbor;
                            if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
                                if (gatheringDetailMap == null)
                                    gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
                                else if (neighborDetailMap.Count > d) // Async job
                                    count = neighborDetailMap[d][resolution + posX, posY];
                        }
                        else if (posX >= resolution && terrain.rightNeighbor && posY >= 0 && posY < resolution)
                        {
                            Terrain neighbor = terrain.rightNeighbor;
                            if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
                                if (gatheringDetailMap == null)
                                    gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
                                else if (neighborDetailMap.Count > d) // Async job
                                    count = neighborDetailMap[d][posX - resolution, posY];
                        }
                        else if (posY >= resolution && terrain.topNeighbor && posX >= 0 && posX < resolution)
                        {
                            Terrain neighbor = terrain.topNeighbor;
                            if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
                                if (gatheringDetailMap == null)
                                    gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
                                else if (neighborDetailMap.Count > d) // Async job
                                    count = neighborDetailMap[d][posX, posY - resolution];
                        }
                        else if (posY < 0 && terrain.bottomNeighbor && posX >= 0 && posX < resolution)
                        {
                            Terrain neighbor = terrain.bottomNeighbor;
                            if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
                                if (gatheringDetailMap == null)
                                    gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
                                else if (neighborDetailMap.Count > d) // Async job
                                    count = neighborDetailMap[d][posX, posY + resolution];
                        }
                        else if (posY >= resolution && terrain.topNeighbor.rightNeighbor && posX >= resolution)
                        {
                            Terrain neighbor = terrain.topNeighbor.rightNeighbor;
                            if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
                                if (gatheringDetailMap == null)
                                    gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
                                else if (neighborDetailMap.Count > d) // Async job
                                    count = neighborDetailMap[d][posX - resolution, posY - resolution];
                        }
                        else if (posY >= resolution && terrain.topNeighbor.leftNeighbor && posX < 0)
                        {
                            Terrain neighbor = terrain.topNeighbor.leftNeighbor;
                            if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
                                if (gatheringDetailMap == null)
                                    gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
                                else if (neighborDetailMap.Count > d) // Async job
                                    count = neighborDetailMap[d][posX + resolution, posY - resolution];
                        }
                        else if (posY < 0 && terrain.bottomNeighbor.rightNeighbor && posX >= resolution)
                        {
                            Terrain neighbor = terrain.bottomNeighbor.rightNeighbor;
                            if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
                                if (gatheringDetailMap == null)
                                    gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
                                else if (neighborDetailMap.Count > d) // Async job
                                    count = neighborDetailMap[d][posX - resolution, posY + resolution];
                        }
                        else if (posY < 0 && terrain.bottomNeighbor.leftNeighbor && posX < 0)
                        {
                            Terrain neighbor = terrain.bottomNeighbor.leftNeighbor;
                            if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
                                if (gatheringDetailMap == null)
                                    gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
                                else if (neighborDetailMap.Count > d) // Async job
                                    count = neighborDetailMap[d][posX + resolution, posY + resolution];
                        }
                        else if (detailMap.Count > d) // Async job
                        {
                            count = detailMap[d][posX, posY];
                        }

                        player.Details5x5[player.GetDetailInfoIndex(x, y, d, MaxDetailTypes)] = new CastedDetailInfo()
                        {
                            casted = true,
                            name = manager.prototypeList[d].name,
                            count = count,
                        };

                        if (x >= 1 && x <= 3 && y >= 1 && y <= 3) player.RecentDetailCount3x3 += count;
                        player.RecentDetailCount5x5 += count;
                    }
            }
            return;

        }
        Dictionary<Terrain, SpatialPartitionClass> terrainSpatialPartitions = new Dictionary<Terrain, SpatialPartitionClass>();
        Dictionary<Terrain, List<int[,]>> terrainDetailMaps = new Dictionary<Terrain, List<int[,]>>();
        Coroutine gatheringDetailMap;
        IEnumerator BuildAllTerrainDetailMapCoroutine(Terrain priority = null)
        {
            yield return new WaitForSeconds(1); // Grass Cutter
            Logger.LogInfo($"[{ activeRaidSettings.LocationId }] Starting building terrain detail maps at { Time.time }...");
            bool allDisabled = true;
            var mgr = priority.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;
            if (mgr != null && mgr.enabled)
            {
                allDisabled = false;
                if (!terrainDetailMaps.ContainsKey(priority))
                {
                    terrainDetailMaps[priority] = new List<int[,]>(mgr.prototypeList.Count);
                    yield return BuildTerrainDetailMapCoroutine(priority, terrainDetailMaps[priority]);

                }
            }
            else terrainDetailMaps[priority] = null;
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                mgr = terrain.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;
                if (mgr != null && mgr.enabled)
                {
                    allDisabled = false;
                    if (!terrainDetailMaps.ContainsKey(terrain))
                    {
                        terrainDetailMaps[terrain] = new List<int[,]>(mgr.prototypeList.Count);
                        yield return BuildTerrainDetailMapCoroutine(terrain, terrainDetailMaps[terrain]);

                    }
                }
                else terrainDetailMaps[terrain] = null;
            }
            if (allDisabled) terrainDetailsUnavailable = true;
            Logger.LogInfo($"[{ activeRaidSettings.LocationId }] Finished building terrain detail maps at { Time.time }... (AllDisabled: {allDisabled})");
        }
        IEnumerator BuildTerrainDetailMapCoroutine(Terrain terrain, List<int[,]> detailMapData)
        {
            var mgr = terrain.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;
            if (mgr == null || !mgr.isInitialized) yield break;
            float time = Time.time;
            Logger.LogInfo($"[{activeRaidSettings.LocationId }] Starting building detail map of {terrain.name} at {time}...");
            if (!terrainSpatialPartitions.TryGetValue(terrain, out var spData))
            {
                spData = terrainSpatialPartitions[terrain] = AccessTools.Field(typeof(GPUInstancerDetailManager), "spData").GetValue(mgr) as SpatialPartitionClass;
            }
            if (spData == null)
            {
                terrainSpatialPartitions.Remove(terrain);
            }
            var waitNextFrame = new WaitForEndOfFrame();

            if (detailMapData == null)
                detailMapData = new List<int[,]>(mgr.prototypeList.Count);
            else detailMapData.Clear();
            for (int layer = 0; layer < mgr.prototypeList.Count; ++layer)
            {
                var prototype = mgr.prototypeList[layer] as GPUInstancerDetailPrototype;
                if (prototype == null) detailMapData.Add(null);
                int[,] detailLayer = new int[prototype.detailResolution, prototype.detailResolution];
                detailMapData.Add(detailLayer);
                var resolutionPerCell = prototype.detailResolution / spData.cellRowAndCollumnCountPerTerrain;
                for (int terrainCellX = 0; terrainCellX < spData.cellRowAndCollumnCountPerTerrain; ++terrainCellX)
                {
                    for (int terrainCellY = 0; terrainCellY < spData.cellRowAndCollumnCountPerTerrain; ++terrainCellY)
                    {
                        BaseCellClass abstractCell;
                        if (spData.GetCell(BaseCellClass.CalculateHash(terrainCellX, 0, terrainCellY), out abstractCell))
                        {
                            CellClass cell = (CellClass)abstractCell;
                            if (cell.detailMapData != null)
                            {
                                for (int cellResX = 0; cellResX < resolutionPerCell; ++cellResX)
                                {
                                    for (int cellResY = 0; cellResY < resolutionPerCell; ++cellResY)
                                        detailLayer[cellResX + terrainCellX * resolutionPerCell, cellResY + terrainCellY * resolutionPerCell] = cell.detailMapData[layer][cellResX + cellResY * resolutionPerCell];
                                }
                            }
                        }

                        yield return waitNextFrame;
                    }
                }
            }
            Logger.LogInfo($"[{ activeRaidSettings.LocationId }] Finished building detail map of {terrain.name} at { Time.time }... Costed { Time.time - time }");
        }
    }

}
