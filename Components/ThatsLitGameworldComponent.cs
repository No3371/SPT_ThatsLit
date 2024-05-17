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

using BaseCellClass = GClass1052;
using CellClass = GClass1053;
using SpatialPartitionClass = GClass1067<GClass1052>;
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
            if (!ThatsLitMainPlayerComponent.CanLoad()) return;
            for (int i = 0; i < GameWorld.AllAlivePlayersList.Count; i++)
            {
                Player player = GameWorld.AllAlivePlayersList[i];
                if (player.IsAI) continue;
                if (!AllThatsLitPlayers.ContainsKey(player))
                {
                    var tlp = player.gameObject.AddComponent<ThatsLitMainPlayerComponent>();
                    AllThatsLitPlayers.Add(player, tlp);
                    tlp.Setup(player);
                    if (player == GameWorld.MainPlayer) MainThatsLitPlayer = tlp;
                }
            }
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
        }

        internal static System.Diagnostics.Stopwatch _benchmarkSWTerrainCheck;
        public GameWorld GameWorld => Singleton<GameWorld>.Instance;
        public ThatsLitMainPlayerComponent MainThatsLitPlayer { get; private set; }
        public Dictionary<IPlayer, ThatsLitMainPlayerComponent> AllThatsLitPlayers { get; private set; }
        public ScoreCalculator ScoreCalculator { get; internal set; }
        public RaidSettings activeRaidSettings;
        public bool IsWinter { get; private set; }
        internal bool skipFoliageCheck, skipDetailCheck;

        void Awake ()
        {
            Singleton<ThatsLitGameworld>.Instance = this;

            var session = (TarkovApplication)Singleton<ClientApplication<ISession>>.Instance;
            if (session == null) throw new Exception("No session!");
            IsWinter = session.Session.IsWinter;
            activeRaidSettings = (RaidSettings)(typeof(TarkovApplication).GetField("_raidSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session));

            switch (activeRaidSettings?.LocationId)
            {
                case "factory4_night":
                case "factory4_day":
                case "laboratory":
                case null:
                    skipFoliageCheck = true;
                    skipDetailCheck = true;
                    break;
                default:
                    skipFoliageCheck = false;
                    skipDetailCheck = !ThatsLitPlugin.EnabledGrasses.Value;
                    break;
            }

            AllThatsLitPlayers = new Dictionary<IPlayer, ThatsLitMainPlayerComponent>();

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
                    if (ThatsLitPlugin.EnableGroundZero.Value) ScoreCalculator = new GroundZeroScoreCalculator();
                    break;
                case "Shoreline":
                    if (ThatsLitPlugin.EnableShoreline.Value) ScoreCalculator = new ShorelineScoreCalculator();
                    break;
                case "laboratory":
                    ScoreCalculator = null;
                    break;
                case null:
                    if (ThatsLitPlugin.EnableHideout.Value) ScoreCalculator = new HideoutScoreCalculator();
                    break;
                default:
                    break;
            }
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
        private static readonly LayerMask foliageLayerMask = 1 << LayerMask.NameToLayer("Foliage") | 1 << LayerMask.NameToLayer("Grass") | 1 << LayerMask.NameToLayer("PlayerSpiritAura");// PlayerSpiritAura is Visceral Bodies compat
        
        internal void UpdateFoliageScore(Vector3 bodyPos, PlayerFoliageProfile player)
        {
            if (player == null) return;
            player.LastCheckedTime = Time.time;
            player.FoliageScore = 0;

            if (Time.time < player.LastCheckedTime + (ThatsLitPlugin.LessFoliageCheck.Value ? 0.7f : 0.35f)) return;
            // Skip if basically standing still
            if ((bodyPos - player.LastCheckedPos).magnitude < 0.05f)
            {
                return;
            }

            Array.Clear(player.Foliage, 0, player.Foliage.Length);
            Array.Clear(player.CastedFoliageColliders, 0, player.CastedFoliageColliders.Length);

            if (skipFoliageCheck) return;

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
            player.LastCheckedPos = bodyPos;
        }

        internal int MaxDetailTypes { get; set; }

        internal bool CheckTerrainDetails(Vector3 position, PlayerTerrainDetailsProfile player)
        {
            if (GPUInstancerDetailManager.activeManagerList?.Count == 0)
            {
                skipDetailCheck = true;
                Logger.LogInfo($"Active detail managers not found, disabling detail check...");
                return false;
            }

            if (player == null || skipDetailCheck) return false;
            if (player.Details5x5 != null) Array.Clear(player.Details5x5, 0, player.Details5x5.Length);
            player.RecentDetailCount3x3 = 0;

            var ray = new Ray(position, Vector3.down);
            if (!Physics.Raycast(ray, out var hit, 100, LayerMaskClass.TerrainMask)) return false;

            var terrain = hit.transform?.GetComponent<Terrain>();
            GPUInstancerDetailManager manager = terrain?.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;

            if (!terrain || !manager || !manager.isInitialized) return false;
            if (!terrainDetailMaps.TryGetValue(terrain, out var detailMap))
            {
                if (gatheringDetailMap == null) gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(terrain));
                return false;
            }

#region BENCHMARK
            if (ThatsLitPlugin.EnableBenchmark.Value && ThatsLitPlugin.DebugInfo.Value)
            {
                if (_benchmarkSWTerrainCheck == null) _benchmarkSWTerrainCheck = new System.Diagnostics.Stopwatch();
                if (_benchmarkSWTerrainCheck.IsRunning)
                {
                    string message = $"[That's Lit] Benchmark stopwatch is not stopped! (TerrainDetails)";
                    NotificationManagerClass.DisplayWarningNotification(message);
                    Logger.LogWarning(message);
                }
                _benchmarkSWTerrainCheck.Start();
            }
            else if (_benchmarkSWTerrainCheck != null)
                _benchmarkSWTerrainCheck = null;
#endregion

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
                var resolution = (manager.prototypeList[d] as GPUInstancerDetailPrototype).detailResolution;
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
                    }
            }
            #region BENCHMARK
            _benchmarkSWTerrainCheck?.Stop();
            #endregion
            return true;

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
            if (allDisabled) skipDetailCheck = true;
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

            if (detailMapData == null) detailMapData = new List<int[,]>(mgr.prototypeList.Count);
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
