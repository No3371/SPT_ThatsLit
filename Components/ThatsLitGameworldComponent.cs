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

namespace ThatsLit.Components
{
    public class ThatsLitGameworld : MonoBehaviour
    {

        private void Update()
        {
            if (Singleton<GameWorld>.Instantiated && ThatsLitMainPlayer == null && ThatsLitMainPlayerComponent.CanLoad())
                ThatsLitMainPlayer = ComponentHelpers.AddOrDestroyComponent(ThatsLitMainPlayer, GameWorld?.MainPlayer);
        }

        private void OnDestroy()
        {
            try
            {
                ComponentHelpers.DestroyComponent(ThatsLitMainPlayer);
            }
            catch
            {
                Logger.LogError("Dispose Component Error");
            }
        }

        public GameWorld GameWorld => Singleton<GameWorld>.Instance;
        public ThatsLitMainPlayerComponent ThatsLitMainPlayer { get; private set; }
        public RaidSettings activeRaidSettings;
        public bool IsWinter { get; private set; }

        void Awake ()
        {
            var session = (TarkovApplication)Singleton<ClientApplication<ISession>>.Instance;
            if (session == null) throw new Exception("No session!");
            IsWinter = session.Session.IsWinter;
            activeRaidSettings = (RaidSettings)(typeof(TarkovApplication).GetField("_raidSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session));

        }

        Dictionary<Terrain, SpatialPartitionClass> terrainSpatialPartitions = new Dictionary<Terrain, SpatialPartitionClass>();
        Dictionary<Terrain, List<int[,]>> terrainDetailMaps = new Dictionary<Terrain, List<int[,]>>();
        Coroutine gatheringDetailMap;
        bool skipDetailCheck;
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
