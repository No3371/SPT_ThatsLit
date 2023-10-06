using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.Weather;
using GPUInstancer;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ThatsLit.Components
{
    public class ThatsLitMainPlayerComponent : MonoBehaviour
    {
        public bool disabledLit;
        static readonly int RESOLUTION = ThatsLitPlugin.LowResMode.Value? 32 : 64;
        public const int POWER = 3;
        public RenderTexture rt, envRt;
        public Camera cam, envCam;
        public Texture2D envTex, envDebugTex;
        Unity.Collections.NativeArray<Color32> observed;
        public float lastCalcFrom, lastCalcTo, lastScore, lastFactor1, lastFactor2;
        public int calced = 0, calcedLastFrame = 0, encounter;
        public int lockPos = -1;
        public RawImage display;
        public RawImage displayEnv;
        public bool disableVisionPatch;

        public float foliageScore;
        int foliageCount;
        internal Vector2 foliageDir;
        internal float foliageDisH, foliageDisV;
        internal string foliage;
        internal bool foliageCloaking;
        Collider[] collidersCache;
        public LayerMask foliageLayerMask = 1 << LayerMask.NameToLayer("Foliage") | 1 << LayerMask.NameToLayer("Grass")| 1 << LayerMask.NameToLayer("PlayerSpiritAura");
        // PlayerSpiritAura is Visceral Bodies compat

        float awakeAt, lastCheckedLights, lastCheckedFoliages, lastCheckedDetails;
        // Note: If vLight > 0, other counts may be skipped

        public Vector3 envCamOffset = new Vector3(0, 2, 0);

        public RaidSettings activeRaidSettings;
        bool skipFoliageCheck, skipDetailCheck;
        public float fog, rain, cloud;
        public float MultiFrameLitScore { get; private set; }
        public float detailScoreProne, detailScoreCrouch;
        // public Vector3 lastTriggeredDetailCoverDirNearest;
        // public float lastTiltAngle, lastRotateAngle;
        // public float lastNearest;
        // public float lastFinalDetailScoreNearest;
        internal ScoreCalculator scoreCalculator;
        AsyncGPUReadbackRequest gquReq;
        // float benchMark1, benchMark2;
        public void Awake()
        {
            if (!ThatsLitPlugin.EnabledMod.Value)
            {
                this.enabled = false;
                return;
            }

            awakeAt = Time.time;
            collidersCache = new Collider[16];

            Singleton<ThatsLitMainPlayerComponent>.Instance = this;
            MainPlayer = Singleton<GameWorld>.Instance.MainPlayer;

            var session = (TarkovApplication)Singleton<ClientApplication<ISession>>.Instance;
            activeRaidSettings = (RaidSettings)(typeof(TarkovApplication).GetField("_raidSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session));

            if (ThatsLitPlugin.EnabledLighting.Value)
            {
                switch (activeRaidSettings?.LocationId)
                {
                    case "Lighthouse":
                        if (ThatsLitPlugin.EnableLighthouse.Value) scoreCalculator = new LighthouseScoreCalculator();
                        break;
                    case "Woods":
                        if (ThatsLitPlugin.EnableWoods.Value) scoreCalculator = new WoodsScoreCalculator();
                        break;
                    case "factory4_night":
                        if (ThatsLitPlugin.EnableFactoryNight.Value) scoreCalculator = GetInGameDayTime() > 12 ? null : new NightFactoryScoreCalculator();
                        skipFoliageCheck = true;
                        skipDetailCheck = true;
                        break;
                    case "factory4_day":
                        scoreCalculator = null;
                        skipFoliageCheck = true;
                        skipDetailCheck = true;
                        break;
                    case "bigmap": // Customs
                        if (ThatsLitPlugin.EnableCustoms.Value) scoreCalculator = new CustomsScoreCalculator();
                        break;
                    case "RezervBase": // Reserve
                        if (ThatsLitPlugin.EnableReserve.Value) scoreCalculator = new ReserveScoreCalculator();
                        break;
                    case "Interchange":
                        if (ThatsLitPlugin.EnableInterchange.Value) scoreCalculator = new InterchangeScoreCalculator();
                        break;
                    case "TarkovStreets":
                        if (ThatsLitPlugin.EnableStreets.Value) scoreCalculator = new StreetsScoreCalculator();
                        break;
                    case "Shoreline":
                        if (ThatsLitPlugin.EnableShoreline.Value) scoreCalculator = new ShorelineScoreCalculator();
                        break;
                    case "laboratory":
                        // scoreCalculator = new LabScoreCalculator();
                        skipFoliageCheck = true;
                        break;
                    case null:
                        if (ThatsLitPlugin.EnableHideout.Value) scoreCalculator = new HideoutScoreCalculator();
                        skipFoliageCheck = true;
                        skipDetailCheck = true;
                        break;
                    default:
                        break;
                }
            }

            if (scoreCalculator == null)
            {
                disabledLit = true;
                return;
            }

            rt = new RenderTexture(RESOLUTION, RESOLUTION, 0, RenderTextureFormat.ARGB32);
            rt.useMipMap = false;
            rt.filterMode = FilterMode.Point;
            rt.Create();

            //cam = GameObject.Instantiate<Camera>(Singleton<PlayerCameraController>.Instance.Camera);
            cam = new GameObject().AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cam.transform.SetParent(MainPlayer.Transform.Original);

            cam.nearClipPlane = 0.001f;
            cam.farClipPlane = 10f;

            cam.cullingMask = LayerMaskClass.PlayerMask;
            cam.fieldOfView = 44;

            cam.targetTexture = rt;



            if (ThatsLitPlugin.DebugTexture.Value)
            {
                //debugTex = new Texture2D(RESOLUTION, RESOLUTION, TextureFormat.RGBA32, false);
                display = new GameObject().AddComponent<RawImage>();
                display.transform.SetParent(MonoBehaviourSingleton<GameUI>.Instance.RectTransform());
                display.RectTransform().sizeDelta = new Vector2(160, 160);
                display.texture = rt;
                display.RectTransform().anchoredPosition = new Vector2(-720, -360);


                //envRt = new RenderTexture(RESOLUTION, RESOLUTION, 0);
                //envRt.filterMode = FilterMode.Point;
                //envRt.Create();

                //envTex = new Texture2D(RESOLUTION / 2, RESOLUTION / 2);

                //envCam = new GameObject().AddComponent<Camera>();
                //envCam.clearFlags = CameraClearFlags.SolidColor;
                //envCam.backgroundColor = Color.white;
                //envCam.transform.SetParent(MainPlayer.Transform.Original);
                //envCam.transform.localPosition = Vector3.up * 3;

                //envCam.nearClipPlane = 0.01f;

                //envCam.cullingMask = ~LayerMaskClass.PlayerMask;
                //envCam.fieldOfView = 75;

                //envCam.targetTexture = envRt;

                //envDebugTex = new Texture2D(RESOLUTION / 2, RESOLUTION / 2);
                //displayEnv = new GameObject().AddComponent<RawImage>();
                //displayEnv.transform.SetParent(MonoBehaviourSingleton<GameUI>.Instance.RectTransform());
                //displayEnv.RectTransform().sizeDelta = new Vector2(160, 160);
                //displayEnv.texture = envDebugTex;
                //displayEnv.RectTransform().anchoredPosition = new Vector2(-560, -360);
            }
        }


        private void Update()
        {
            if (!ThatsLitPlugin.EnabledMod.Value)
            {
                if (cam?.enabled ?? false) GameObject.Destroy(cam.gameObject);
                if (rt != null) rt.Release();
                if (display?.enabled ?? false) GameObject.Destroy(display);
                this.enabled = false;
                return;
            }

            Vector3 bodyPos = MainPlayer.MainParts[BodyPartType.body].Position;
            if (!skipDetailCheck && Time.time > lastCheckedDetails + 0.5f)
            {
                if (GPUInstancerDetailManager.activeManagerList.Count == 0)
                {
                    skipDetailCheck = true;
                }
                else
                {
                    CheckTerrainDetails();
                    lastCheckedDetails = Time.time;
                }
            }
            if (Time.time > lastCheckedFoliages + (ThatsLitPlugin.LessFoliageCheck.Value ? 0.75f : 0.4f))
            {
                UpdateFoliageScore(bodyPos);
            }
            if (disabledLit)
            {
                return;
            }
            var camPos = 0;
            if (lockPos != -1) camPos = lockPos;
            else camPos = Time.frameCount % 6;
            var camHeight = MainPlayer.IsInPronePose ? 0.45f : 2.2f;
            var targetHeight = MainPlayer.IsInPronePose ? 0.2f : 0.7f;
            var horizontalScale = MainPlayer.IsInPronePose ? 1.2f : 1;
            switch (Time.frameCount % 6)
            {
                case 0:
                {
                    if (MainPlayer.IsInPronePose)
                    {
                        cam.transform.localPosition = new Vector3(0, 2, 0);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position);
                    }
                    else
                    {
                        cam.transform.localPosition = new Vector3(0, camHeight, 0);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position);
                    }
                    break;
                }
                case 1:
                {
                    cam.transform.localPosition = new Vector3(0.7f * horizontalScale, camHeight, 0.7f * horizontalScale);
                    cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                    break;
                }
                case 2:
                {
                    cam.transform.localPosition = new Vector3(0.7f * horizontalScale, camHeight, -0.7f * horizontalScale);
                    cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                    break;
                }
                case 3:
                {
                    if (MainPlayer.IsInPronePose)
                    {
                        cam.transform.localPosition = new Vector3(0, 2f, 0);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position);
                    }
                    else
                    {
                        cam.transform.localPosition = new Vector3(0, -0.5f,  0.35f);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * 1f);
                    }
                    break;
                }
                case 4:
                {
                    cam.transform.localPosition = new Vector3(-0.7f * horizontalScale, camHeight, -0.7f * horizontalScale);
                    cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                    break;
                }
                case 5:
                {
                    cam.transform.localPosition = new Vector3(-0.7f * horizontalScale, camHeight, 0.7f * horizontalScale);
                    cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                    break;
                }
            }

            if (gquReq.done) gquReq = AsyncGPUReadback.Request(rt, 0, req =>
            {
                if (req.hasError)
                    return;
                
                observed = req.GetData<Color32>();
            });

            // if (ThatsLitPlugin.DebugTexture.Value && envCam)
            // {
            //     envCam.transform.localPosition = envCamOffset;
            //     switch (camPos)
            //     {
            //         case 0:
            //             {
            //                 envCam.transform.LookAt(bodyPos + Vector3.left * 25);
            //                 break;
            //             }
            //         case 1:
            //             {
            //                 envCam.transform.LookAt(bodyPos + Vector3.right * 25);
            //                 break;
            //             }
            //         case 2:
            //             {
            //                 envCam.transform.localPosition = envCamOffset;
            //                 envCam.transform.LookAt(bodyPos + Vector3.down * 10);
            //                 break;
            //             }
            //         case 3:
            //             {
            //                 envCam.transform.LookAt(bodyPos + Vector3.back * 25);
            //                 break;
            //             }
            //         case 4:
            //             {
            //                 envCam.transform.LookAt(bodyPos + Vector3.right * 25);
            //                 break;
            //             }
            //     }
            // }

            if (Time.time > lastCheckedLights + (ThatsLitPlugin.LessEquipmentCheck.Value ? 0.6f : 0.33f))
            {
                lastCheckedLights = Time.time;
                Utility.DetermineShiningEquipments(MainPlayer, out var vLight, out var vLaser, out var irLight, out var irLaser, out var vLightSub, out var vLaserSub, out var irLightSub, out var irLaserSub);
                if (scoreCalculator != null)
                {
                    scoreCalculator.vLight = vLight;
                    scoreCalculator.vLaser = vLaser;
                    scoreCalculator.irLight = irLight;
                    scoreCalculator.irLaser = irLaser;
                    scoreCalculator.vLightSub = vLightSub;
                    scoreCalculator.vLaserSub = vLaserSub;
                    scoreCalculator.irLightSub = irLightSub;
                    scoreCalculator.irLaserSub = irLaserSub;
                }
            }
        }

        private void UpdateFoliageScore(Vector3 bodyPos)
        {
            lastCheckedFoliages = Time.time;
            foliageScore = 0;
            foliageDir = Vector2.zero;

            if (!skipFoliageCheck)
            {
                for (int i = 0; i < collidersCache.Length; i++)
                    collidersCache[i] = null;

                int count = Physics.OverlapSphereNonAlloc(bodyPos, 3f, collidersCache, foliageLayerMask);
                float closet = 9999f;
                foliage = null;

                for (int i = 0; i < count; i++)
                {
                    if (collidersCache[i].gameObject.transform.root.gameObject.layer == 8) continue; // Somehow sometimes player spines are tagged PlayerSpiritAura, VB or vanilla?
                    Vector3 dir = (collidersCache[i].transform.position - bodyPos);
                    float dis = dir.magnitude;
                    if (dis < 0.3f) foliageScore += 3f;
                    else if (dis < 0.4f) foliageScore += 2f;
                    else if (dis < 0.5f) foliageScore += 1f;
                    else if (dis < 0.6f) foliageScore += 0.7f;
                    else if (dis < 0.7f) foliageScore += 0.5f;
                    else if (dis < 1f) foliageScore += 0.3f;
                    else if (dis < 2f) foliageScore += 0.15f;
                    else foliageScore += 0.05f;

                    if (dis < closet)
                    {
                        closet = dis;
                        foliageDir = new Vector2(dir.x, dir.z);
                        foliage = collidersCache[i]?.gameObject.transform.parent.gameObject.name;
                    }
                }

                foliageCount = count;

                if (count > 0)
                {
                    foliageScore /= (float) count;
                    foliageDisH = foliageDir.magnitude;
                    foliageDisV = Mathf.Abs(foliageDir.y);
                }
                if (count == 1) foliageScore /= 2f;
                if (count == 2) foliageScore /= 1.5f;
            }
        }

        void LateUpdate ()
        {
            if (disabledLit) return;
            GetWeatherStats(out fog, out rain, out cloud);

            //if (debugTex != null && Time.frameCount % 61 == 0) Graphics.CopyTexture(tex, debugTex);
            if (envDebugTex != null && Time.frameCount % 61 == 0) Graphics.CopyTexture(envTex, envDebugTex);

            MultiFrameLitScore = scoreCalculator?.CalculateMultiFrameScore(observed, cloud, fog, rain, this, GetInGameDayTime(), activeRaidSettings.LocationId) ?? 0;
        }

        private void OnDestroy()
        {
            if (display) GameObject.Destroy(display);
            if (cam) GameObject.Destroy(cam);
            if (rt) rt.Release();

        }

        private void OnGUI()
        {
            if (disabledLit && Time.time - awakeAt < 30f)
            {
                GUILayout.Label("[That's Lit] Lit detection on this map is not supported or disabled in configs.");
                if (!ThatsLitPlugin.DebugInfo.Value) return;
            }
            if (ThatsLitPlugin.DebugInfo.Value || ThatsLitPlugin.ScoreInfo.Value)
            {
                if (!disabledLit) Utility.GUILayoutDrawAsymetricMeter((int) (MultiFrameLitScore / 0.0999f));
                if (!disabledLit) Utility.GUILayoutDrawAsymetricMeter((int)(Mathf.Pow(MultiFrameLitScore, POWER) / 0.0999f));
                if (foliageScore > 0.6f)
                    GUILayout.Label("[FOLIAGE++++++]");
                else if (foliageScore > 0.55f)
                    GUILayout.Label("[FOLIAGE+++++-]");
                else if (foliageScore > 0.5f)
                    GUILayout.Label("[FOLIAGE+++++]");
                else if (foliageScore > 0.45f)
                    GUILayout.Label("[FOLIAGE++++-]");
                else if (foliageScore > 0.4f)
                    GUILayout.Label("[FOLIAGE++++]");
                else if (foliageScore > 0.35f)
                    GUILayout.Label("[FOLIAGE+++-]");
                else if (foliageScore > 0.3f)
                    GUILayout.Label("[FOLIAGE+++]");
                else if (foliageScore > 0.25f)
                    GUILayout.Label("[FOLIAGE++-]");
                else if (foliageScore > 0.2f)
                    GUILayout.Label("[FOLIAGE++]");
                else if (foliageScore > 0.15f)
                    GUILayout.Label("[FOLIAGE+-]");
                else if (foliageScore > 0.1f)
                    GUILayout.Label("[FOLIAGE+]");
                else if (foliageScore > 0.5f)
                    GUILayout.Label("[FOLIAGE-]");
                else if (foliageScore > 0.025f)
                    GUILayout.Label("[FOLIAGE]");

                if (Time.time < awakeAt + 10)
                    GUILayout.Label("[That's Lit HUD] Can be disabled in plugin settings.");
            }
            if (!ThatsLitPlugin.DebugInfo.Value) return;
            scoreCalculator?.CalledOnGUI();
            GUILayout.Label(string.Format("IMPACT: {0:0.00} -> {1:0.00} ({2:0.00} <- {3:0.00} <- {4:0.00}) (SAMPLE)", lastCalcFrom, lastCalcTo, lastFactor2, lastFactor1, lastScore));
            //GUILayout.Label(text: "PIXELS:");
            //GUILayout.Label(lastValidPixels.ToString());
            GUILayout.Label(string.Format("AFFECTED: {0} (+{1}) / ENCOUNTER: {2}", calced, calcedLastFrame, encounter));

            GUILayout.Label(string.Format("FOLIAGE: {0:0.000} ({1}) (H{2:0.00} Y{3:0.00} to {4})", foliageScore, foliageCount, foliageDisH, foliageDisV, foliage));
            
            var poseFactor = MainPlayer.AIData.Player.PoseLevel / MainPlayer.AIData.Player.Physical.MaxPoseLevel * 0.6f + 0.4f; // crouch: 0.4f
            if (MainPlayer.AIData.Player.IsInPronePose) poseFactor -= 0.4f; // prone: 0
            poseFactor += 0.05f; // base -> prone -> 0.05f, crouch -> 0.45f
            // GUILayout.Label(string.Format("POSE: {0:0.000} LOOK: {1} ({2})", poseFactor, MainPlayer.LookDirection, DetermineDir(MainPlayer.LookDirection)));
            // GUILayout.Label(string.Format("{0} {1} {2}", collidersCache[0]?.gameObject.name, collidersCache[1]?.gameObject?.name, collidersCache[2]?.gameObject?.name));
            GUILayout.Label(string.Format("FOG: {0:0.000} / RAIN: {1:0.000} / CLOUD: {2:0.000} / TIME: {3:0.000}", WeatherController.Instance?.WeatherCurve?.Fog ?? 0, WeatherController.Instance?.WeatherCurve?.Rain ?? 0, WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0, GetInGameDayTime()));
            if (scoreCalculator != null) GUILayout.Label(string.Format("LIGHT: [{0}] / LASER: [{1}] / LIGHT2: [{2}] / LASER2: [{3}]", scoreCalculator.vLight? "V" : scoreCalculator.irLight? "I" : "-", scoreCalculator.vLaser ? "V" : scoreCalculator.irLaser ? "I" : "-", scoreCalculator.vLightSub ? "V" : scoreCalculator.irLightSub ? "I" : "-", scoreCalculator.vLaserSub ? "V" : scoreCalculator.irLaserSub ? "I" : "-"));
            // GUILayout.Label(string.Format("{0} ({1})", activeRaidSettings?.LocationId, activeRaidSettings?.SelectedLocation?.Name));
            // GUILayout.Label(string.Format("{0:0.00000}ms / {1:0.00000}ms", benchMark1, benchMark2));
            // GUILayout.Label(string.Format("LAST DETAIL ENEMY DIR: {0:+0.00;-0.00;+0.00} ({1:0.000}) ({2:+0.00;-0.00;+0.00} -> ({3:0.00}m)) {4} {5}", lastTriggeredDetailCoverDirNearest, lastFinalDetailScoreNearest, DetermineDir(lastTriggeredDetailCoverDirNearest), lastNearest, lastTiltAngle, lastRotateAngle));
            // for (int i = GetDetailInfoIndex(2, 2, 0); i < GetDetailInfoIndex(3, 2, 0); i++)
            //     if (detailsHere5x5[i].casted)
            //         GUILayout.Label($"  { detailsHere5x5[i].count } Detail#{i}({ detailsHere5x5[i].name }))");
            // GUILayout.Label($"MID  DETAIL_LOW: { scoreCache[16] } DETAIL_MID: {scoreCache[17]}");
            // GUILayout.Label($"  N  DETAIL_LOW: { scoreCache[0] } DETAIL_MID: {scoreCache[1]}");
            // GUILayout.Label($" NE  DETAIL_LOW: { scoreCache[2] } DETAIL_MID: {scoreCache[3]}");
            // GUILayout.Label($"  E  DETAIL_LOW: { scoreCache[4] } DETAIL_MID: {scoreCache[5]}");
            // GUILayout.Label($" SE  DETAIL_LOW: { scoreCache[6] } DETAIL_MID: {scoreCache[7]}");
            // GUILayout.Label($"  S  DETAIL_LOW: { scoreCache[8] } DETAIL_MID: {scoreCache[9]}");
            // GUILayout.Label($" SW  DETAIL_LOW: { scoreCache[10] } DETAIL_MID: {scoreCache[11]}");
            // GUILayout.Label($"  W  DETAIL_LOW: { scoreCache[12] } DETAIL_MID: {scoreCache[13]}");
            // GUILayout.Label($" NW  DETAIL_LOW: { scoreCache[14] } DETAIL_MID: {scoreCache[15]}");
        }


        public Player MainPlayer { get; private set; }

        float GetInGameDayTime ()
        {
            if (Singleton<GameWorld>.Instance?.GameDateTime == null) return 19f;

            var GameDateTime = Singleton<GameWorld>.Instance.GameDateTime.Calculate();

            float minutes = GameDateTime.Minute / 59f;
            return GameDateTime.Hour + minutes;
        }

        void GetWeatherStats (out float fog, out float rain, out float cloud)
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

        public DetailInfo[] detailsHere5x5 = new DetailInfo[MAX_DETAIL_TYPES * 25]; // MAX_DETAIL_TYPES(24) x 25;
        public struct DetailInfo
        {
            public bool casted;
            public string name;
            public int count;
        }

        Dictionary<Terrain, GClass1079<GClass1064>> terrainSpatialPartitions = new Dictionary<Terrain, GClass1079<GClass1064>>();
        Dictionary<Terrain, List<int[,]>> terrainDetailMaps = new Dictionary<Terrain, List<int[,]>>();
        // GameObject marker;
        // float[] scoreCache = new float[18];
        void CheckTerrainDetails ()
        {
            Array.Clear(detailsHere5x5, 0, detailsHere5x5.Length);
            var ray = new Ray(MainPlayer.MainParts[BodyPartType.head].Position, Vector3.down);
            if (!Physics.Raycast(ray, out var hit, 100, LayerMaskClass.TerrainMask)) return;
            var terrain = hit.transform.GetComponent<Terrain>();
            GPUInstancerDetailManager manager = terrain?.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;

            if (!terrain || !manager || !manager.isInitialized ) return;
            if (!terrainDetailMaps.ContainsKey(terrain))
            {
                if ( gatheringDetailMap == null) gatheringDetailMap = StartCoroutine(AsyncAllTerrainDetailMapGathering(terrain));
                return;
            }
            if (!terrainDetailMaps.ContainsKey(terrain)) return;
            var detailMap = terrainDetailMaps[terrain];

            Vector3 hitRelativePos = hit.point - (terrain.transform.position + terrain.terrainData.bounds.min);
            var currentLocationOnTerrainmap = new Vector2(hitRelativePos.x / terrain.terrainData.size.x, hitRelativePos.z / terrain.terrainData.size.z);
            
            for (int d = 0; d < manager.prototypeList.Count; d++)
            {
                var resolution = (manager.prototypeList[d] as GPUInstancerDetailPrototype).detailResolution;
                Vector2Int resolutionPos = new Vector2Int((int) (currentLocationOnTerrainmap.x * resolution), (int) (currentLocationOnTerrainmap.y * resolution));
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
                        if (!terrainDetailMaps.ContainsKey(neighbor))
                            if (gatheringDetailMap == null)
                                gatheringDetailMap = StartCoroutine(AsyncAllTerrainDetailMapGathering(neighbor));
                        else if (terrainDetailMaps[neighbor].Count > d) // Async job
                            count = terrainDetailMaps[neighbor][d][resolution + posX, posY];
                    }
                    else if (posX >= resolution && terrain.rightNeighbor && posY >= 0 && posY < resolution)
                    {
                        Terrain neighbor = terrain.rightNeighbor;
                        if (!terrainDetailMaps.ContainsKey(neighbor))
                            if (gatheringDetailMap == null)
                                gatheringDetailMap = StartCoroutine(AsyncAllTerrainDetailMapGathering(neighbor));
                        else if (terrainDetailMaps[neighbor].Count > d) // Async job
                            count = terrainDetailMaps[neighbor][d][posX - resolution, posY];
                    }
                    else if (posY >= resolution && terrain.topNeighbor && posX >= 0 && posX < resolution)
                    {
                        Terrain neighbor = terrain.topNeighbor;
                        if (!terrainDetailMaps.ContainsKey(neighbor))
                            if (gatheringDetailMap == null)
                                gatheringDetailMap = StartCoroutine(AsyncAllTerrainDetailMapGathering(neighbor));
                        else if (terrainDetailMaps[neighbor].Count > d) // Async job
                            count = terrainDetailMaps[neighbor][d][posX, posY - resolution];
                    }
                    else if (posY < 0 && terrain.bottomNeighbor && posX >= 0 && posX < resolution)
                    {
                        Terrain neighbor = terrain.bottomNeighbor;
                        if (!terrainDetailMaps.ContainsKey(neighbor))
                            if (gatheringDetailMap == null)
                                gatheringDetailMap = StartCoroutine(AsyncAllTerrainDetailMapGathering(neighbor));
                        else if (terrainDetailMaps[neighbor].Count > d) // Async job
                            count = terrainDetailMaps[neighbor][d][posX, posY + resolution];
                    }
                    else if (posY >= resolution && terrain.topNeighbor.rightNeighbor && posX >= resolution)
                    {
                        Terrain neighbor = terrain.topNeighbor.rightNeighbor;
                        if (!terrainDetailMaps.ContainsKey(neighbor))
                            if (gatheringDetailMap == null)
                                gatheringDetailMap = StartCoroutine(AsyncAllTerrainDetailMapGathering(neighbor));
                        else if (terrainDetailMaps[neighbor].Count > d) // Async job
                            count = terrainDetailMaps[neighbor][d][posX - resolution, posY - resolution];
                    }
                    else if (posY >= resolution && terrain.topNeighbor.leftNeighbor && posX < 0)
                    {
                        Terrain neighbor = terrain.topNeighbor.leftNeighbor;
                        if (!terrainDetailMaps.ContainsKey(neighbor))
                            if (gatheringDetailMap == null)
                                gatheringDetailMap = StartCoroutine(AsyncAllTerrainDetailMapGathering(neighbor));
                        else if (terrainDetailMaps[neighbor].Count > d) // Async job
                            count = terrainDetailMaps[neighbor][d][posX + resolution, posY - resolution];
                    }
                    else if (posY < 0 && terrain.bottomNeighbor.rightNeighbor && posX >= resolution)
                    {
                        Terrain neighbor = terrain.bottomNeighbor.rightNeighbor;
                        if (!terrainDetailMaps.ContainsKey(neighbor))
                            if (gatheringDetailMap == null)
                                gatheringDetailMap = StartCoroutine(AsyncAllTerrainDetailMapGathering(neighbor));
                        else if (terrainDetailMaps[neighbor].Count > d) // Async job
                            count = terrainDetailMaps[neighbor][d][posX - resolution, posY + resolution];
                    }
                    else if (posY < 0 && terrain.bottomNeighbor.leftNeighbor && posX < 0)
                    {
                        Terrain neighbor = terrain.bottomNeighbor.leftNeighbor;
                        if (!terrainDetailMaps.ContainsKey(neighbor))
                            if (gatheringDetailMap == null)
                                gatheringDetailMap = StartCoroutine(AsyncAllTerrainDetailMapGathering(neighbor));
                        else if (terrainDetailMaps[neighbor].Count > d) // Async job
                            count = terrainDetailMaps[neighbor][d][posX + resolution, posY + resolution];
                    }
                    else if (detailMap.Count > d) // Async job
                    {
                        count = detailMap[d][posX, posY];
                    }

                    detailsHere5x5[GetDetailInfoIndex(x, y, d)] = new DetailInfo()
                    {
                        casted = true,
                        name = manager.prototypeList[d].name,
                        count = count,
                    };
                }
            }

            // scoreCache[16] = 0;
            // scoreCache[17] = 0;
            // foreach (var pos in IterateDetailIndex3x3)
            // {
            //     for (int i = 0; i < MAX_DETAIL_TYPES; i++)
            //     {
            //         var info = detailsHere5x5[pos*MAX_DETAIL_TYPES + i];
            //         GetDetailCoverScoreByName(info.name, info.count, out var s1, out var s2);
            //         scoreCache[16] += s1;
            //         scoreCache[17] += s2;
            //     }
            // }
            // CalculateDetailScore(Vector3.forward, 31, 0, out scoreCache[0], out scoreCache[1]);
            // CalculateDetailScore(Vector3.forward + Vector3.right, 31, 0, out scoreCache[2], out scoreCache[3]);
            // CalculateDetailScore(Vector3.right, 31, 0, out scoreCache[4], out scoreCache[5]);
            // CalculateDetailScore(Vector3.right + Vector3.back, 31, 0, out scoreCache[6], out scoreCache[7]);
            // CalculateDetailScore(Vector3.back, 31, 0, out scoreCache[8], out scoreCache[9]);
            // CalculateDetailScore(Vector3.back + Vector3.left, 31, 0, out scoreCache[10], out scoreCache[11]);
            // CalculateDetailScore(Vector3.left, 31, 0, out scoreCache[12], out scoreCache[13]);
            // CalculateDetailScore(Vector3.left + Vector3.forward, 31, 0, out scoreCache[14], out scoreCache[15]);

        }
        
        Coroutine gatheringDetailMap;
        IEnumerator AsyncAllTerrainDetailMapGathering (Terrain priority = null)
        {
            // EFT.UI.ConsoleScreen.Log($"JOB: Staring gathering terrain details..." );
            
            if (priority && !terrainDetailMaps.ContainsKey(priority))
            {
                var mgr = priority.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;
                terrainDetailMaps[priority] = new List<int[,]>(mgr.prototypeList.Count);
                yield return AsyncTerrainDetailMapGathering(priority, terrainDetailMaps[priority]);
            }
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (!terrainDetailMaps.ContainsKey(terrain))
                {
                    var mgr = terrain.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;
                    terrainDetailMaps[terrain] = new List<int[,]>(mgr.prototypeList.Count);
                    yield return AsyncTerrainDetailMapGathering(terrain, terrainDetailMaps[terrain]);
                }
            }
        }
        IEnumerator AsyncTerrainDetailMapGathering (Terrain terrain, List<int[,]> detailMapData)
        {
            var mgr = terrain.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;
            if (mgr == null || !mgr.isInitialized) yield break;
            if (!terrainSpatialPartitions.ContainsKey(terrain))
            {
                terrainSpatialPartitions[terrain] = AccessTools.Field(typeof(GPUInstancerDetailManager), "spData").GetValue(mgr) as GClass1079<GClass1064>;
            }
            if (!terrainSpatialPartitions.TryGetValue(terrain, out var spData)) yield break;
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
                        GClass1064 cell;
                        if (spData.GetCell(GClass1064.CalculateHash(terrainCellX, 0, terrainCellY), out cell))
                        {
                            GClass1065 gclass1065 = (GClass1065) cell;
                            if (gclass1065.detailMapData != null)
                            {
                                for (int cellResX = 0; cellResX < resolutionPerCell; ++cellResX)
                                {
                                    for (int cellResY = 0; cellResY < resolutionPerCell; ++cellResY)
                                        detailLayer[cellResX + terrainCellX * resolutionPerCell, cellResY + terrainCellY * resolutionPerCell] = gclass1065.detailMapData[layer][cellResX + cellResY * resolutionPerCell];
                                }
                            }
                        }

                        yield return waitNextFrame;
                    }
                }
            }
        }

        int GetDetailInfoIndex (int x5x5, int y5x5, int detailId) => (y5x5 * 5 + x5x5) * MAX_DETAIL_TYPES + detailId;

        string DetermineDir (Vector3 dir)
        {
            var dirFlat = (new Vector2 (dir.x, dir.z)).normalized;
            var angle = Vector2.SignedAngle(Vector2.up, dirFlat);
            if (angle >= -22.5f && angle <= 22.5f)
            {
                return "N";
            }
            else if (angle >= 22.5f && angle <= 67.5f)
            {
                return "NE";
            }
            else if (angle >= 67.5f && angle <= 112.5f)
            {
                return "E";
            }
            else if (angle >= 112.5f && angle <= 157.5f)
            {
                return "SE";
            }
            else if (angle >= 157.5f && angle <= 180f || angle >= -180f && angle <= -157.5f)
            {
                return "S";
            }
            else if (angle >= -157.5f && angle <= -112.5f)
            {
                return "SW";
            }
            else if (angle >= -112.5f && angle <= -67.5f)
            {
                return "W";
            }
            else if (angle >= -67.5f && angle <= -22.5f)
            {
                return "NW";
            }
            else return "?";
        }

        public void CalculateDetailScore (Vector3 enemyDirection, float dis, float verticalAxisAngle, out float scoreLow, out float scoreMid)
        {
            scoreLow = scoreMid = 0;
            var dirFlat = (new Vector2 (enemyDirection.x, enemyDirection.z)).normalized;
            var angle = Vector2.SignedAngle(Vector2.up, dirFlat);
            IEnumerable<int> it;
            if (dis < 15f || verticalAxisAngle < -10f)
                it = IterateDetailIndex3x3;
            else if (angle >= -22.5f && angle <= 22.5f)
            {
                it = IterateDetailIndex3x3N;
            }
            else if (angle >= 22.5f && angle <= 67.5f)
            {
                it = IterateDetailIndex3x3NE;
            }
            else if (angle >= 67.5f && angle <= 112.5f)
            {
                it = IterateDetailIndex3x3E;
            }
            else if (angle >= 112.5f && angle <= 157.5f)
            {
                it = IterateDetailIndex3x3SE;
            }
            else if (angle >= 157.5f && angle <= 180f || angle >= -180f && angle <= -157.5f)
            {
                it = IterateDetailIndex3x3S;
            }
            else if (angle >= -157.5f && angle <= -112.5f)
            {
                it = IterateDetailIndex3x3SW;
            }
            else if (angle >= -112.5f && angle <= -67.5f)
            {
                it = IterateDetailIndex3x3W;
            }
            else if (angle >= -67.5f && angle <= -22.5f)
            {
                it = IterateDetailIndex3x3NW;
            }
            else throw new Exception($"[That's Lit] Invalid angle to enemy: {angle}");

            foreach (var pos in it)
            {
                for (int i = 0; i < MAX_DETAIL_TYPES; i++)
                {
                    var info = detailsHere5x5[pos*MAX_DETAIL_TYPES + i];
                    if (!info.casted) continue;
                    Utility.CalculateDetailScore(info.name, info.count, out var s1, out var s2);
                    scoreLow += s1;
                    scoreMid += s2;
                }
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
        IEnumerable<int> IterateDetailIndex3x3 => IterateIndex3x3In5x5(0, 0);
        /// <param name="xOffset">WestSide(-x) => -1, EstSide(+x) => 1</param>
        /// <param name="yOffset"></param>
        /// <returns></returns>
        IEnumerable<int> IterateIndex3x3In5x5 (int xOffset, int yOffset)
        {
            yield return 5*(1 + yOffset) + 1 + xOffset;
            yield return 5*(1 + yOffset) + 2 + xOffset;
            yield return 5*(1 + yOffset) + 3 + xOffset;
            
            yield return 5*(2 + yOffset) + 1 + xOffset;
            yield return 5*(2 + yOffset) + 2 + xOffset;
            yield return 5*(2 + yOffset) + 3 + xOffset;
            
            yield return 5*(3 + yOffset) + 1 + xOffset;
            yield return 5*(3 + yOffset) + 2 + xOffset;
            yield return 5*(3 + yOffset) + 3 + xOffset;
        }

        const int MAX_DETAIL_TYPES = 24;
    }
}