// #define DEBUG_DETAILS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.EnvironmentEffect;
using EFT.UI;
using EFT.Weather;
using GPUInstancer;
using HarmonyLib;
using ThatsLit.Patches.Vision;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

using BaseCellClass = GClass1052;
using CellClass = GClass1053;
using SpatialPartitionClass = GClass1067<GClass1052>;

namespace ThatsLit.Components
{
    // TODO:
    // # Experiment: Full body lit check
    // Assign player thorax to layer N and set ShadowCastingMode on
    // un-cull N for all Lights
    // Cull N for FPS cam
    // Un-Cull N for TL cam
    public class ThatsLitMainPlayerComponent : MonoBehaviour
    {
        public static bool IsDebugSampleFrame { get; set; }
        public bool disabledLit;
        readonly int RESOLUTION = 32 * ThatsLitPlugin.ResLevel.Value;
        public const int POWER = 3;
        // public RenderTexture rt;
        public CustomRenderTexture rt;
        Texture slowRT;
        public Camera cam, envCam;
        // public Texture2D envTex, envDebugTex;
        Unity.Collections.NativeArray<Color32> observed;
        public float lastCalcFrom, lastCalcTo, lastScore, lastFactor1, lastFactor2, rawTerrainScoreSample;
        public int calced = 0, calcedLastFrame = 0, encounter;
        public int lockPos = -1;
        public RawImage display;
        public RawImage displayEnv;

        public float foliageScore;
        internal int foliageCount;
        // internal Vector2 foliageDir;
        // internal float foliageDisH, foliageDisV;
        // internal string foliage;
        internal FoliageInfo[] foliage;
        internal bool isFoliageSorted;
        internal Vector3 lastFoliageCheckPos;
        internal bool foliageCloaking;
        Collider[] collidersCache;
        public LayerMask foliageLayerMask = 1 << LayerMask.NameToLayer("Foliage") | 1 << LayerMask.NameToLayer("Grass") | 1 << LayerMask.NameToLayer("PlayerSpiritAura");
        // PlayerSpiritAura is Visceral Bodies compat

        float awakeAt, lastCheckedLights, lastCheckedFoliages, lastCheckedDetails = 10;
        // Note: If vLight > 0, other counts may be skipped

        // public Vector3 envCamOffset = new Vector3(0, 2, 0);

        public RaidSettings activeRaidSettings;
        internal bool skipFoliageCheck, skipDetailCheck;
        public float fog, rain, cloud;
        public float MultiFrameLitScore { get; private set; }
        public Vector3 lastTriggeredDetailCoverDirNearest;
        public float lastTiltAngle, lastRotateAngle, lastDisFactorNearest;
        public float lastNearest;
        public float lastFinalDetailScoreNearest;
        internal int recentDetailCount3x3;
        internal ScoreCalculator scoreCalculator;
        AsyncGPUReadbackRequest gquReq;
        internal float lastOutside;
        internal bool isWinterCache;
        /// <summary>
        /// 0~10
        /// </summary>
        internal float ambienceShadownRating;
        internal float AmbienceShadowFactor => Mathf.Pow(ambienceShadownRating / 10f, 2); 
        internal float bunkerTimeClamped;
        internal float lastInBunkerTime, lastOutBunkerTime;
        internal Vector3 lastInBunderPos;

        public static bool CanLoad ()
        {
            var session = (TarkovApplication)Singleton<ClientApplication<ISession>>.Instance;
            if (session == null) throw new Exception("No session!");
            var raidSettings = (RaidSettings)(typeof(TarkovApplication).GetField("_raidSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session));

            bool result = CameraClass.Instance.OpticCameraManager.Camera != null;
            switch (raidSettings?.LocationId)
            {
                case "factory4_day":
                case "laboratory":
                case null:
                    return true;
                case "Lighthouse":
                    return ThatsLitPlugin.EnableLighthouse.Value & result;
                case "Woods":
                    return ThatsLitPlugin.EnableWoods.Value & result;
                case "factory4_night":
                    return ThatsLitPlugin.EnableFactoryNight.Value & result;
                case "bigmap": // Customs
                    return ThatsLitPlugin.EnableCustoms.Value & result;
                case "RezervBase": // Reserve
                    return ThatsLitPlugin.EnableReserve.Value & result;
                case "Interchange":
                    return ThatsLitPlugin.EnableInterchange.Value & result;
                case "TarkovStreets":
                    return ThatsLitPlugin.EnableStreets.Value & result;
                case "Sandbox": // GZ
                    return ThatsLitPlugin.EnableGroundZero.Value & result;
                case "Shoreline":
                    return ThatsLitPlugin.EnableShoreline.Value & result;
                default:
                    return result;
            }
        }
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
            if (foliage == null) foliage = new FoliageInfo[16];

            Singleton<ThatsLitMainPlayerComponent>.Instance = this;
            MainPlayer = Singleton<GameWorld>.Instance.MainPlayer;

            var session = (TarkovApplication)Singleton<ClientApplication<ISession>>.Instance;
            if (session == null) throw new Exception("No session!");
            isWinterCache = session.Session.IsWinter;
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

            if (ThatsLitPlugin.EnabledLighting.Value) EnableBrightness();

        }

        void EnableBrightness ()
        {
            if (scoreCalculator == null)
            switch (activeRaidSettings?.LocationId)
            {
                case "Lighthouse":
                    if (ThatsLitPlugin.EnableLighthouse.Value) scoreCalculator = new LighthouseScoreCalculator();
                    break;
                case "Woods":
                    if (ThatsLitPlugin.EnableWoods.Value) scoreCalculator = new WoodsScoreCalculator();
                    break;
                case "factory4_night":
                    if (ThatsLitPlugin.EnableFactoryNight.Value) scoreCalculator = new NightFactoryScoreCalculator();
                    break;
                case "factory4_day":
                    scoreCalculator = null;
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
                case "Sandbox":
                    if (ThatsLitPlugin.EnableGroundZero.Value) scoreCalculator = new GroundZeroScoreCalculator();
                    break;
                case "Shoreline":
                    if (ThatsLitPlugin.EnableShoreline.Value) scoreCalculator = new ShorelineScoreCalculator();
                    break;
                case "laboratory":
                    scoreCalculator = null;
                    break;
                case null:
                    if (ThatsLitPlugin.EnableHideout.Value) scoreCalculator = new HideoutScoreCalculator();
                    break;
                default:
                    break;
            }

            if (scoreCalculator == null)
            {
                disabledLit = true;
                return;
            }

            if (rt == null)
            {
                // rt = new RenderTexture(RESOLUTION, RESOLUTION, 0, RenderTextureFormat.ARGB32);
                rt = new CustomRenderTexture(RESOLUTION, RESOLUTION, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                rt.depth = 0;
                rt.doubleBuffered = true;
                rt.useMipMap = false;
                rt.filterMode = FilterMode.Point;
                rt.Create();
            }

            if (cam == null)
            {
                cam = GameObject.Instantiate<Camera>(CameraClass.Instance.OpticCameraManager.Camera);
                cam.gameObject.name = "That's Lit Camera";
                foreach (var c in cam.gameObject.GetComponents<MonoBehaviour>())
                switch (c) {
                    case VolumetricLightRenderer volumetricLightRenderer:
                    case AreaLightManager areaLightManager:
                        break;
                    default:
                        MonoBehaviour.Destroy(c);
                        break;
                }
                cam.gameObject.name = "[That's Lit CAM]";
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color (0, 0, 0, 0);

                cam.transform.SetParent(MainPlayer.Transform.Original);

                cam.nearClipPlane = 0.001f;
                cam.farClipPlane = 5f;

                cam.cullingMask = LayerMaskClass.PlayerMask;
                cam.fieldOfView = 44;

                cam.targetTexture = rt;
            }
            else cam.enabled = true;


            if (ThatsLitPlugin.DebugTexture.Value)
            {
                if (slowRT == null) slowRT = new Texture2D(RESOLUTION, RESOLUTION, TextureFormat.RGBA32, false);
                if (display == null)
                {
                    display = new GameObject().AddComponent<RawImage>();
                    display.transform.SetParent(MonoBehaviourSingleton<GameUI>.Instance.RectTransform());
                    display.RectTransform().sizeDelta = new Vector2(160, 160);
                    display.texture = slowRT;
                    display.RectTransform().anchoredPosition = new Vector2(-720, -360);
                }
                else display.enabled = true;
            }

            disabledLit = scoreCalculator == null;
        }

        void DisableBrightness ()
        {
            if (cam) cam.enabled = false;
            if (display) display.enabled = false;
            disabledLit = true;
        }

        internal static System.Diagnostics.Stopwatch _benchmarkSW, _benchmarkSWGUI, _benchmarkSWFoliageCheck, _benchmarkSWTerrainCheck;
        static readonly LayerMask ambienceRaycastMask = (1 << LayerMask.NameToLayer("Terrain")) | (1 << LayerMask.NameToLayer("HighPolyCollider")) | (1 << LayerMask.NameToLayer("Grass")) | (1 << LayerMask.NameToLayer("Foliage"));

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

            #region BENCHMARK
            if (ThatsLitPlugin.EnableBenchmark.Value && ThatsLitPlugin.DebugInfo.Value)
            {
                if (_benchmarkSW == null)
                {
                    _benchmarkSW = new System.Diagnostics.Stopwatch();
                }
                if (_benchmarkSW.IsRunning)
                {
                    string message = $"[That's Lit] Benchmark stopwatch is not stopped! (Update)";
                    NotificationManagerClass.DisplayWarningNotification(message);
                    Logger.LogWarning(message);
                }
                _benchmarkSW.Start();
            }
            else if (_benchmarkSW != null)
                _benchmarkSW = null;
            #endregion

            IsDebugSampleFrame = ThatsLitPlugin.DebugInfo.Value && Time.frameCount % 47 == 0;


            Vector3 bodyPos = MainPlayer.MainParts[BodyPartType.body].Position;
            Vector3 headPos = MainPlayer.MainParts[BodyPartType.head].Position;
            Vector3 lhPos = MainPlayer.MainParts[BodyPartType.leftArm].Position;
            Vector3 rhPos = MainPlayer.MainParts[BodyPartType.rightArm].Position;
            Vector3 lPos = MainPlayer.MainParts[BodyPartType.leftLeg].Position;
            Vector3 rPos = MainPlayer.MainParts[BodyPartType.rightLeg].Position;

            if (!MainPlayer.AIData.IsInside) lastOutside = Time.time;
            
            if (EnvironmentManager.Instance.InBunker && lastOutBunkerTime >= lastInBunkerTime)
            {
                lastInBunkerTime = Time.time;
                lastInBunderPos = bodyPos;
            }

            if (!EnvironmentManager.Instance.InBunker && lastOutBunkerTime < lastInBunkerTime)
            {
                lastOutBunkerTime = Time.time;
            }

            if (lastOutBunkerTime < lastInBunkerTime && bodyPos.SqrDistance(lastInBunderPos) > 2.25f) bunkerTimeClamped += Time.deltaTime;
            else bunkerTimeClamped -= Time.deltaTime * 5;

            bunkerTimeClamped = Mathf.Clamp(bunkerTimeClamped, 0, 10);


            if (!skipDetailCheck && Time.time > lastCheckedDetails + 0.5f)
            {
                if (GPUInstancerDetailManager.activeManagerList?.Count == 0)
                {
                    skipDetailCheck = true;
                    Logger.LogInfo($"Active detail managers not found, disabling detail check...");
                }
                else
                {
                    CheckTerrainDetails();
                    lastCheckedDetails = Time.time;
                    if (ThatsLitPlugin.TerrainInfo.Value)
                    {
                        var score = CalculateDetailScore(Vector3.zero, 0, 0);
                        terrainScoreHintProne = score.prone;
                        terrainScoreHintRegular = score.regular;

                        var pf = (MainPlayer.PoseLevel / MainPlayer.AIData.Player.Physical.MaxPoseLevel) * 0.6f + 0.4f;
                        terrainScoreHintRegular /= (pf + 0.1f + 0.25f * Mathf.InverseLerp(0.45f, 0.55f, pf));
                    }
                }
            }

#region BENCHMARK
            if (ThatsLitPlugin.EnableBenchmark.Value && ThatsLitPlugin.DebugInfo.Value)
            {
                if (_benchmarkSWFoliageCheck == null) _benchmarkSWFoliageCheck = new System.Diagnostics.Stopwatch();
                if (_benchmarkSWFoliageCheck.IsRunning)
                {
                    string message = $"[That's Lit] Benchmark stopwatch is not stopped! (Foliage Check)";
                    NotificationManagerClass.DisplayWarningNotification(message);
                    Logger.LogWarning(message);
                }
                _benchmarkSWFoliageCheck.Start();
            }
            else if (_benchmarkSWFoliageCheck != null)
                _benchmarkSWFoliageCheck = null;
#endregion

            if (!isFoliageSorted) isFoliageSorted = SlicedBubbleSort(foliage, foliageCount * 3 / 2, foliageCount);
            if (Time.time > lastCheckedFoliages + (ThatsLitPlugin.LessFoliageCheck.Value ? 0.7f : 0.35f))
            {
                UpdateFoliageScore(bodyPos);
            }

#region BENCHMARK
            _benchmarkSWFoliageCheck?.Stop();
#endregion

            if (disabledLit && ThatsLitPlugin.EnabledLighting.Value)
            {
                EnableBrightness();
#region BENCHMARK
                _benchmarkSW?.Stop();
#endregion
                return;
            }
            else if (!disabledLit && !ThatsLitPlugin.EnabledLighting.Value)
            {
                DisableBrightness();
#region BENCHMARK
                _benchmarkSW?.Stop();
#endregion
                return;
            }

            if (disabledLit)
            {
#region BENCHMARK
                _benchmarkSW?.Stop();
#endregion
                return;
            }

            if (gquReq.done) gquReq = AsyncGPUReadback.Request(rt, 0, req =>
            {
                if (!req.hasError)
                {
                    observed.Dispose();
                    observed = req.GetData<Color32>();
                    scoreCalculator?.PreCalculate(observed, GetInGameDayTime());
                }
            });

            var camPos = 0;
            if (lockPos != -1) camPos = lockPos;
            else camPos = Time.frameCount % 6;
            var camHeight = MainPlayer.IsInPronePose ? 0.45f : 2.2f * (0.6f + 0.4f * MainPlayer.PoseLevel);
            var targetHeight = MainPlayer.IsInPronePose ? 0.2f : 0.7f;
            var horizontalScale = MainPlayer.IsInPronePose ? 1.2f : 0.8f;
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
                        cam.transform.localPosition = new Vector3(horizontalScale, camHeight, horizontalScale);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
                case 2:
                    {
                        cam.transform.localPosition = new Vector3(horizontalScale, camHeight, -horizontalScale);
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
                            cam.transform.localPosition = new Vector3(0, -0.5f, 0.35f);
                            cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * 1f);
                        }
                        break;
                    }
                case 4:
                    {
                        cam.transform.localPosition = new Vector3(-horizontalScale, camHeight, -horizontalScale);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
                case 5:
                    {
                        cam.transform.localPosition = new Vector3(-horizontalScale, camHeight, horizontalScale);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
            }

            if (ThatsLitPlugin.DebugTexture.Value && Time.frameCount % 61 == 0 && display?.enabled == true) Graphics.CopyTexture(rt, slowRT);

            // Ambient shadow
            if (TOD_Sky.Instance != null)
            {
                Ray ray = default;
                Vector3 ambienceDir = scoreCalculator.CalculateSunLightTimeFactor(activeRaidSettings.LocationId, GetInGameDayTime()) > 0.05f ? TOD_Sky.Instance.LocalSunDirection : TOD_Sky.Instance.LightDirection;
                switch (Time.frameCount % 5)
                {
                    case 0:
                        {
                            ray = new Ray(headPos, ambienceDir);
                            break;
                        }
                    case 1:
                        {
                            ray = new Ray(lPos, ambienceDir);
                            break;
                        }
                    case 2:
                        {
                            ray = new Ray(rPos, ambienceDir);
                            break;
                        }
                    case 3:
                        {
                            ray = new Ray(lhPos, ambienceDir);
                            break;
                        }
                    case 4:
                        {
                            ray = new Ray(rhPos, ambienceDir);
                            break;
                        }
                }

                var casted1 = RaycastIgnoreGlass(ray, 2000, ambienceRaycastMask, out var highPolyHit, out var lastPenetrated); // Anything high poly that is not glass
                
                    // if (Time.frameCount % 213 == 0)
                    //     EFT.UI.ConsoleScreen.Log($"---");
                // Fuck GZ buildings (low poly)
                bool casted2 = false;
                
                // if (!casted1) casted2 = RaycastIgnoreGlass(ray, 1250, ambienceRaycastMask_lowPriority, out var lowPolyHit, out var _, 0, 3) && lastPenetrated.distance < lowPolyHit.distance; // high/low poly any non-glass that is further than 0 or casted glass
                if (casted1 || casted2)
                {
                    ambienceShadownRating += 10f * Time.deltaTime;
                }
                else ambienceShadownRating -= 22f * Time.deltaTime;
                ambienceShadownRating = Mathf.Clamp(ambienceShadownRating, 0, 10f);
            }

            if (OverheadHaxCast(bodyPos, out var haxHit))
            {
                overheadHaxRating += Time.timeScale * Mathf.Clamp01(10f - haxHit.distance);
            }
            else 
                overheadHaxRating -= Time.timeScale * 2f;
            overheadHaxRating = Mathf.Clamp(overheadHaxRating, 0f, 10f);

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

            if (ThatsLitPlugin.EnableEquipmentCheck.Value && Time.time > lastCheckedLights + (ThatsLitPlugin.LessEquipmentCheck.Value ? 0.6f : 0.33f))
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

            #region BENCHMARK
            _benchmarkSW?.Stop();
            #endregion
        }

        void LateUpdate()
        {
            if (disabledLit) return;
            GetWeatherStats(out fog, out rain, out cloud);

            //if (debugTex != null && Time.frameCount % 61 == 0) Graphics.CopyTexture(tex, debugTex);
            // if (envDebugTex != null && Time.frameCount % 61 == 0) Graphics.CopyTexture(envTex, envDebugTex);

            if (!observed.IsCreated) return;
            MultiFrameLitScore = 0;
            if (ThatsLitPlugin.EnabledLighting.Value && !disabledLit)
                MultiFrameLitScore = scoreCalculator?.CalculateMultiFrameScore(observed, cloud, fog, rain, this, GetInGameDayTime(), activeRaidSettings.LocationId) ?? 0;
            observed.Dispose();
        }
        internal float overheadHaxRating;
        internal float OverheadHaxRatingFactor => overheadHaxRating / 10f;
        private bool OverheadHaxCast (Vector3 from, out RaycastHit hit)
        {
            Vector3 cast = new Vector3(0, 1, 0);
            int slice = Time.frameCount % 6;
            int expansion = (Time.frameCount % 24) / 6;
            cast = Quaternion.Euler((expansion + 1) * 10, 0, 0) * cast;
            cast = Quaternion.Euler(0, slice * -60f, 0) * cast;
            
            var ray = new Ray(from, cast);
            return RaycastIgnoreGlass(ray, 25, ambienceRaycastMask, out hit, out var lp);
        }
        private bool RaycastIgnoreGlass (Ray ray, float distance, LayerMask mask, out RaycastHit hit, out RaycastHit lastPenetrated, int depth = 0, int maxDepth = 10)
        {
            lastPenetrated = default;
            hit = default;
            if (distance < 0 || depth++ >= maxDepth) return false;

            if (Physics.Raycast(ray, out hit, distance, mask))
            {
                // if (Time.frameCount % 213 == 0)
                //     EFT.UI.ConsoleScreen.Log($"{hit.collider?.name}");
                BallisticCollider c = hit.collider?.gameObject?.GetComponent<BallisticCollider>();
                if (c == null) hit.collider?.transform?.parent?.GetComponent<BallisticCollider>();
                if ((c == null || (c?.TypeOfMaterial) != MaterialType.Glass) && (c?.TypeOfMaterial) != MaterialType.GlassShattered)
                {
                    // if (Time.frameCount % 213 == 0)
                    //     EFT.UI.ConsoleScreen.Log($"{hit.distance:0.0}m -------> {hit.collider?.name}");
                    return true; // no ballistic or not glass
                }
                else // Glass
                {
                    lastPenetrated = hit;
                    ray.origin = hit.point + ray.direction.normalized * 0.1f;
                    hit.distance += 0.1f;
                    var layer = hit.collider.gameObject.layer;
                    if (hit.collider?.gameObject) hit.collider.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast"); // ignore raycast

                    // if (Time.frameCount % 213 == 0)
                    //     EFT.UI.ConsoleScreen.Log($"-- #{depth} {lastPenetrated.collider?.gameObject.name} +{hit.distance}m -->");

                    RaycastHit hit2 = default;
                    if (RaycastIgnoreGlass(ray, distance - hit.distance, mask, out hit2, out lastPenetrated, depth))
                    {
                        if (hit.collider?.gameObject) hit.collider.gameObject.layer = layer; // ignore raycast
                        hit.distance += hit2.distance;
                        // if (Time.frameCount % 213 == 0)
                        //     EFT.UI.ConsoleScreen.Log($"{hit.distance:0.0}m -------> {hit.collider?.name}");
                        return true; // Solid hit
                    }

                    if (hit.collider?.gameObject) hit.collider.gameObject.layer = layer; // ignore raycast

                    // if (Time.frameCount % 213 == 0)
                    //     EFT.UI.ConsoleScreen.Log($"--- #{depth} - - - -  -  -  -    -    -    -");
                    return false; // Pen quota used up and no solid hit
                }
            }
            return false; // nothing
        }

        /// <summary>
        /// This get called every frame. It performs bubble sort on the "foliage" array by the element's dis (float).
        /// The sort is sliced by frame, and only "step" steps are performed every frame.
        /// if any swap is performed in steps, set the local sorted bool to false;
        /// </summary>
        /// <param name="step">Steps to perform this frame</param>
        private bool SlicedBubbleSort<T>(T[] subject, int step, int valid) where T : IComparable<T>
        {
            var sorted = true;

            // Iterate through the array from 0 ~ N-1 as long as step is not used up, swap if left is bigger
            // if a full loop is done and no swap has been performed, set isFoliageSorted to true and return
            // if a full loop is done and any swap has been performed, loop again
            int right = Mathf.Min(valid - 1, subject.Length - 1);
            while (step > 0)
            {
                for (int i = 0; i < right; i++)
                {
                    if (subject[i].CompareTo(subject[i + 1]) > 0)
                    {
                        var temp = subject[i];
                        subject[i] = subject[i + 1];
                        subject[i + 1] = temp;
                        sorted = false;
                        step--;
                    }

                    if (i == right - 1)
                    {
                        if (sorted)
                        {
                            return true;
                        }

                        i = -1;
                        sorted = true;
                    }
                }
            }

            return false;
        }

        private void UpdateFoliageScore(Vector3 bodyPos)
        {
            lastCheckedFoliages = Time.time;
            foliageScore = 0;

            // Skip if basically standing still
            if ((bodyPos - lastFoliageCheckPos).magnitude < 0.05f)
            {
                return;
            }

            Array.Clear(foliage, 0, foliage.Length);
            Array.Clear(collidersCache, 0, collidersCache.Length);

            if (skipFoliageCheck) return;

            int castedCount = Physics.OverlapSphereNonAlloc(bodyPos, 4f, collidersCache, foliageLayerMask);
            int validCount = 0;

            for (int i = 0; i < castedCount; i++)
            {
                Collider casted = collidersCache[i];
                if (casted.gameObject.transform.root.gameObject.layer == 8) continue; // Somehow sometimes player spines are tagged PlayerSpiritAura, VB or vanilla?
                if (casted.gameObject.GetComponent<Terrain>()) continue; // Somehow sometimes terrains can be casted
                Vector3 bodyToFoliage = casted.transform.position - bodyPos;

                float dis = bodyToFoliage.magnitude;
                if (dis < 0.25f) foliageScore += 1f;
                else if (dis < 0.35f) foliageScore += 0.9f;
                else if (dis < 0.5f) foliageScore += 0.8f;
                else if (dis < 0.6f) foliageScore += 0.7f;
                else if (dis < 0.7f) foliageScore += 0.5f;
                else if (dis < 1f) foliageScore += 0.3f;
                else if (dis < 2f) foliageScore += 0.2f;
                else foliageScore += 0.1f;

                string fname = casted?.transform.parent.gameObject.name;
                if (string.IsNullOrWhiteSpace(fname)) continue;

                if (ThatsLitPlugin.FoliageSamples.Value == 1 && (foliage[0] == default || dis < foliage[0].dis)) // don't bother
                {
                    foliage[0] = new FoliageInfo(fname, new Vector3(bodyToFoliage.x, bodyToFoliage.z), dis);
                    validCount = 1;
                    continue;
                }
                else foliage[validCount] = new FoliageInfo(fname, new Vector3(bodyToFoliage.x, bodyToFoliage.z), dis);
                validCount++;
            }

            for (int j = 0; j < validCount; j++)
            {
                var f = foliage[j];
                f.name = Regex.Replace(f.name, @"(.+?)\s?(\(\d+\))?", "$1");
                f.dis = f.dir.magnitude; // Use horizontal distance to replace casted 3D distance
                foliage[j] = f;
            }
            isFoliageSorted = false;
            if (foliage.Length == 1 || validCount == 1)
            {
                isFoliageSorted = true;
            }

            switch (castedCount)
            {
                case 1:
                    foliageScore /= 3.3f;
                    break;
                case 2:
                    foliageScore /= 2.8f;
                    break;
                case 3:
                    foliageScore /= 2.3f;
                    break;
                case 4:
                    foliageScore /= 1.8f;
                    break;
                case 5:
                case 6:
                    foliageScore /= 1.2f;
                    break;
                case 11:
                case 12:
                case 13:
                    foliageScore /= 1.15f;
                    break;
                case 14:
                case 15:
                case 16:
                    foliageScore /= 1.25f;
                    break;
            }

            foliageCount = validCount;

            lastFoliageCheckPos = bodyPos;
        }

        void HandleConfigEvents (bool enable)
        {
            if (enable)
                ThatsLitPlugin.DebugTexture.SettingChanged += HandleDebugTextureSettingChanged;
            else
                ThatsLitPlugin.DebugTexture.SettingChanged -= HandleDebugTextureSettingChanged;
        }

        private void HandleDebugTextureSettingChanged(object sender, EventArgs e)
        {
            if (display) display.enabled = ThatsLitPlugin.DebugTexture.Value;
        }

        private void OnDestroy()
        {
            if (display) GameObject.Destroy(display);
            if (cam) GameObject.Destroy(cam);
            if (rt) rt.Release();
        }
        float litFactorSample, ambScoreSample;
        float benchmarkSampleSeenCoef, benchmarkSampleEncountering, benchmarkSampleExtraVisDis, benchmarkSampleScoreCalculator, benchmarkSampleUpdate, benchmarkSampleFoliageCheck, benchmarkSampleTerrainCheck, benchmarkSampleGUI;
        int guiFrame;
        string infoCache1, infoCache2, infoCacheBenchmark;
        private void OnGUI()
        {
            #region BENCHMARK
            if (ThatsLitPlugin.EnableBenchmark.Value && ThatsLitPlugin.DebugInfo.Value)
            {
                if (_benchmarkSWGUI == null) _benchmarkSWGUI = new System.Diagnostics.Stopwatch();
                if (_benchmarkSWGUI.IsRunning)
                {
                    string message = $"[That's Lit] Benchmark stopwatch is not stopped! (GUI)";
                    NotificationManagerClass.DisplayWarningNotification(message);
                    Logger.LogWarning(message);
                }
                _benchmarkSWGUI.Start();
            }
            else if (_benchmarkSWGUI != null)
                _benchmarkSWGUI = null;
            #endregion
            bool skip = false;
            if (disabledLit && Time.time - awakeAt < 30f)
            {
                if (!ThatsLitPlugin.HideMapTip.Value) GUILayout.Label("  [That's Lit] Lit detection on this map is not supported or disabled in configs.");
                if (!ThatsLitPlugin.DebugInfo.Value) skip = true;
            }
            if (!skip)
            {
                if (ThatsLitPlugin.DebugInfo.Value || ThatsLitPlugin.ScoreInfo.Value)
                {
                    if (!disabledLit) Utility.GUILayoutDrawAsymetricMeter((int)(MultiFrameLitScore / 0.0999f), ThatsLitPlugin.AlternativeMeterUnicde.Value);
                    if (!disabledLit) Utility.GUILayoutDrawAsymetricMeter((int)(Mathf.Pow(MultiFrameLitScore, POWER) / 0.0999f), ThatsLitPlugin.AlternativeMeterUnicde.Value);
                    if (foliageScore > 0 && ThatsLitPlugin.FoliageInfo.Value)
                        Utility.GUILayoutFoliageMeter((int)(foliageScore / 0.0999f), ThatsLitPlugin.AlternativeMeterUnicde.Value);
                    if (!skipDetailCheck && terrainScoreHintProne > 0.0998f && ThatsLitPlugin.TerrainInfo.Value)
                        if (MainPlayer.IsInPronePose) Utility.GUILayoutTerrainMeter((int)(terrainScoreHintProne / 0.0999f), ThatsLitPlugin.AlternativeMeterUnicde.Value);
                        else Utility.GUILayoutTerrainMeter((int)(terrainScoreHintRegular / 0.0999f));
                    if (Time.time < awakeAt + 10)
                        GUILayout.Label("  [That's Lit HUD] Can be disabled in plugin settings.");

                    if (!disabledLit)
                    {
                        if (cloud <= -1.1f)
                            GUILayout.Label("  CLEAR ☀☀☀");
                        else if (cloud <= -0.7f)
                            GUILayout.Label("  CLEAR ☀☀");
                        else if (cloud <= -0.25f)
                            GUILayout.Label("  CLEAR ☀");
                        else if (cloud >= 1.1f)
                            GUILayout.Label("  CLOUDY ☁☁☁");
                        else if (cloud >= 0.7f)
                            GUILayout.Label("  CLOUDY ☁☁");
                        else if (cloud >= 0.25f)
                            GUILayout.Label("  CLOUDY ☁");
                    }
                }
            }
            if (!ThatsLitPlugin.DebugInfo.Value) skip = true;
            if (!skip)
            {
                scoreCalculator?.CalledOnGUI(guiFrame < Time.frameCount);
                if (IsDebugSampleFrame)
                {
                    litFactorSample = scoreCalculator?.litScoreFactor ?? 0;
                    ambScoreSample = scoreCalculator?.frame0.ambienceScore ?? 0;
                    if (ThatsLitPlugin.EnableBenchmark.Value && guiFrame < Time.frameCount) // The trap here is OnGUI is called multiple times per frame, make sure to reset the stopwatches only once
                    {
                        if (SeenCoefPatch._benchmarkSW != null) benchmarkSampleSeenCoef = (SeenCoefPatch._benchmarkSW.ElapsedMilliseconds / 47f);
                        if (EncounteringPatch._benchmarkSW != null) benchmarkSampleEncountering = (EncounteringPatch._benchmarkSW.ElapsedMilliseconds / 47f);
                        if (ExtraVisibleDistancePatch._benchmarkSW != null) benchmarkSampleExtraVisDis = (ExtraVisibleDistancePatch._benchmarkSW.ElapsedMilliseconds / 47f);
                        if (ScoreCalculator._benchmarkSW != null) benchmarkSampleScoreCalculator = (ScoreCalculator._benchmarkSW.ElapsedMilliseconds / 47f);
                        if (_benchmarkSW != null) benchmarkSampleUpdate = (_benchmarkSW.ElapsedMilliseconds / 47f);
                        if (_benchmarkSWGUI != null) benchmarkSampleGUI = (_benchmarkSWGUI.ElapsedMilliseconds / 47f);
                        if (_benchmarkSWFoliageCheck != null) benchmarkSampleFoliageCheck = (_benchmarkSWFoliageCheck.ElapsedMilliseconds / 47f);
                        if (_benchmarkSWTerrainCheck != null) benchmarkSampleTerrainCheck = (_benchmarkSWTerrainCheck.ElapsedMilliseconds / 47f);
                        SeenCoefPatch._benchmarkSW?.Reset();
                        EncounteringPatch._benchmarkSW?.Reset();
                        ExtraVisibleDistancePatch._benchmarkSW?.Reset();
                        ScoreCalculator._benchmarkSW?.Reset();
                        _benchmarkSW?.Reset();
                        _benchmarkSWGUI?.Reset();
                        _benchmarkSWFoliageCheck?.Reset();
                        _benchmarkSWTerrainCheck?.Reset();
                    }
                }
                // GUILayout.Label(string.Format(" IMPACT: {0:0.000} -> {1:0.000} ({2:0.000} <- {3:0.000} <- {4:0.000}) AMB: {5:0.00} LIT: {6:0.00} (SAMPLE)", lastCalcFrom, lastCalcTo, lastFactor2, lastFactor1, lastScore, ambScoreSample, litFactorSample));
                //GUILayout.Label(text: "PIXELS:");
                //GUILayout.Label(lastValidPixels.ToString());
                // GUILayout.Label(string.Format(" AFFECTED: {0} (+{1}) / ENCOUNTER: {2}", calced, calcedLastFrame, encounter));

                // GUILayout.Label(string.Format(" FOLIAGE: {0:0.000} ({1}) (H{2:0.00} Y{3:0.00} to {4})", foliageScore, foliageCount, foliageDisH, foliageDisV, foliage));

                var poseFactor = MainPlayer.PoseLevel / MainPlayer.Physical.MaxPoseLevel * 0.6f + 0.4f; // crouch: 0.4f
                if (MainPlayer.IsInPronePose) poseFactor -= 0.4f; // prone: 0
                poseFactor += 0.05f; // base -> prone -> 0.05f, crouch -> 0.45f
                                     // GUILayout.Label(string.Format(" POSE: {0:0.000} LOOK: {1} ({2})", poseFactor, MainPlayer.LookDirection, DetermineDir(MainPlayer.LookDirection)));
                                     // GUILayout.Label(string.Format(" {0} {1} {2}", collidersCache[0]?.gameObject.name, collidersCache[1]?.gameObject?.name, collidersCache[2]?.gameObject?.name));
                float fog = WeatherController.Instance?.WeatherCurve?.Fog ?? 0;
                float rain = WeatherController.Instance?.WeatherCurve?.Rain ?? 0;
                float cloud = WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0;
                if (guiFrame < Time.frameCount) infoCache1 = $"  IMPACT: {lastCalcFrom:0.000} -> {lastCalcTo:0.000} ({lastFactor2:0.000} <- {lastFactor1:0.000} <- {lastScore:0.000}) AMB: {ambScoreSample:0.00} LIT: {litFactorSample:0.00} (SAMPLE)\n  AFFECTED: {calced} (+{calcedLastFrame}) / ENCOUNTER: {encounter}\n  TERRAIN: { terrainScoreHintProne :0.000}/{ terrainScoreHintRegular :0.000} 3x3:( { recentDetailCount3x3 } ) (score-{ scoreCalculator?.detailBonusSmooth:0.00})  FOLIAGE: {foliageScore:0.000} ({foliageCount}) (H{foliage?[0].dis:0.00} to {foliage?[0].name})\n  FOG: {fog:0.000} / RAIN: {rain:0.000} / CLOUD: {cloud:0.000} / TIME: {GetInGameDayTime():0.000} / WINTER: {isWinterCache}\n  POSE: {poseFactor} SPEED: { MainPlayer.Velocity.magnitude :0.000}  INSIDE: { Time.time - lastOutside:0.000}  AMB: { ambienceShadownRating:0.000}  OVH: { overheadHaxRating:0.000}  BNKR: { bunkerTimeClamped:0.000}";
                GUILayout.Label(infoCache1);
                // GUILayout.Label(string.Format(" FOG: {0:0.000} / RAIN: {1:0.000} / CLOUD: {2:0.000} / TIME: {3:0.000} / WINTER: {4}", WeatherController.Instance?.WeatherCurve?.Fog ?? 0, WeatherController.Instance?.WeatherCurve?.Rain ?? 0, WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0, GetInGameDayTime(), isWinterCache));
                if (scoreCalculator != null) GUILayout.Label(string.Format("  LIGHT: [{0}] / LASER: [{1}] / LIGHT2: [{2}] / LASER2: [{3}]", scoreCalculator.vLight ? "V" : scoreCalculator.irLight ? "I" : "-", scoreCalculator.vLaser ? "V" : scoreCalculator.irLaser ? "I" : "-", scoreCalculator.vLightSub ? "V" : scoreCalculator.irLightSub ? "I" : "-", scoreCalculator.vLaserSub ? "V" : scoreCalculator.irLaserSub ? "I" : "-"));
                // GUILayout.Label(string.Format(" {0} ({1})", activeRaidSettings?.LocationId, activeRaidSettings?.SelectedLocation?.Name));
                // GUILayout.Label(string.Format(" {0:0.00000}ms / {1:0.00000}ms", benchMark1, benchMark2));
                // if (Time.frameCount % 2997 == 0)
                //         if (guiFrame < Time.frameCount && DebugCountId.idCount != null) EFT.UI.ConsoleScreen.Log($"[That's Lit Bot Id Observation] { DebugCountId.idCount[0] } / { DebugCountId.idCount[1] } / { DebugCountId.idCount[2] } / { DebugCountId.idCount[3] } / { DebugCountId.idCount[4] } / { DebugCountId.idCount[5] } / { DebugCountId.idCount[6] } / { DebugCountId.idCount[7] } / { DebugCountId.idCount[8] } / { DebugCountId.idCount[9] }");
                
                if (ThatsLitPlugin.EnableBenchmark.Value)
                {
                    if (guiFrame < Time.frameCount) infoCacheBenchmark = $"  Update: {benchmarkSampleUpdate,8:0.000}\n    Foliage: {benchmarkSampleFoliageCheck,8:0.000}\n    Terrain: {benchmarkSampleTerrainCheck,8:0.000}\n  SeenCoef: {benchmarkSampleSeenCoef,8:0.000}\n  Encountering: {benchmarkSampleEncountering,8:0.000}\n  ExtraVisDis: {benchmarkSampleExtraVisDis,8:0.000}\n  ScoreCalculator: {benchmarkSampleScoreCalculator,8:0.000}\n  Info(+Debug): {benchmarkSampleGUI,8:0.000} ms";
                    GUILayout.Label(infoCacheBenchmark);
                    if (Time.frameCount % 6000 == 0)
                        if (guiFrame < Time.frameCount) EFT.UI.ConsoleScreen.Log(infoCacheBenchmark);
                }
#if DEBUG_DETAILS
            if (detailsHere5x5 != null)
            {
                infoCache2 = $"DETAIL (SAMPLE): {lastFinalDetailScoreNearest:+0.00;-0.00;+0.00} ({lastDisFactorNearest:0.000}df) 3x3: {recentDetailCount3x3}\n  {Utility.DetermineDir(lastTriggeredDetailCoverDirNearest)} {lastNearest:0.00}m {lastTiltAngle} {lastRotateAngle}";
                GUILayout.Label(infoCache2);
                // GUILayout.Label(string.Format(" DETAIL (SAMPLE): {0:+0.00;-0.00;+0.00} ({1:0.000}df) 3x3: {2}", arg0: lastFinalDetailScoreNearest, lastDisFactorNearest, recentDetailCount3x3));
                // GUILayout.Label(string.Format(" {0} {1:0.00}m {2} {3}", Utility.DetermineDir(lastTriggeredDetailCoverDirNearest), lastNearest, lastTiltAngle, lastRotateAngle));
                for (int i = GetDetailInfoIndex(2, 2, 0); i < GetDetailInfoIndex(3, 2, 0); i++) // List the underfoot
                    if (detailsHere5x5[i].casted)
                        GUILayout.Label($"  { detailsHere5x5[i].count } Detail#{i}({ detailsHere5x5[i].name }))");
                Utility.GUILayoutDrawAsymetricMeter((int)(lastFinalDetailScoreNearest / 0.0999f));
            }
#endif
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
            #region BENCHMARK
            _benchmarkSWGUI?.Stop();
            #endregion
            guiFrame = Time.frameCount;
        }


        public Player MainPlayer { get; private set; }

        float GetInGameDayTime()
        {
            if (Singleton<GameWorld>.Instance?.GameDateTime == null) return 19f;

            var GameDateTime = Singleton<GameWorld>.Instance.GameDateTime.Calculate();

            float minutes = GameDateTime.Minute / 59f;
            return GameDateTime.Hour + minutes;
        }

        void GetWeatherStats(out float fog, out float rain, out float cloud)
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

        public DetailInfo[] detailsHere5x5;
        public struct DetailInfo
        {
            public bool casted;
            public string name;
            public int count;
        }

        Dictionary<Terrain, SpatialPartitionClass> terrainSpatialPartitions = new Dictionary<Terrain, SpatialPartitionClass>();
        Dictionary<Terrain, List<int[,]>> terrainDetailMaps = new Dictionary<Terrain, List<int[,]>>();
        float terrainScoreHintProne, terrainScoreHintRegular;
        internal Vector3 lastTerrainCheckPos;
        void CheckTerrainDetails()
        {
            Vector3 position = MainPlayer.MainParts[BodyPartType.body].Position;
            if ((position - lastTerrainCheckPos).magnitude < 0.15f) return;

            Array.Clear(detailScoreCache, 0, detailScoreCache.Length);
            if (detailsHere5x5 != null) Array.Clear(detailsHere5x5, 0, detailsHere5x5.Length);
            recentDetailCount3x3 = 0;
            var ray = new Ray(position, Vector3.down);
            if (!Physics.Raycast(ray, out var hit, 100, LayerMaskClass.TerrainMask)) return;
            var terrain = hit.transform?.GetComponent<Terrain>();
            GPUInstancerDetailManager manager = terrain?.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;

            if (!terrain || !manager || !manager.isInitialized) return;
            if (!terrainDetailMaps.TryGetValue(terrain, out var detailMap))
            {
                if (gatheringDetailMap == null) gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(terrain));
                return;
            }

            #region BENCHMARK
            if (ThatsLitPlugin.EnableBenchmark.Value && ThatsLitPlugin.DebugInfo.Value)
            {
                if (_benchmarkSWTerrainCheck == null) _benchmarkSWTerrainCheck = new System.Diagnostics.Stopwatch();
                if (_benchmarkSWTerrainCheck.IsRunning)
                {
                    string message = $"[That's Lit] Benchmark stopwatch is not stopped! (TerrainCheck)";
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

            if (detailsHere5x5 == null) // Initialize
            {
                foreach (var mgr in GPUInstancerDetailManager.activeManagerList)
                {
                    if (MAX_DETAIL_TYPES < mgr.prototypeList.Count)
                    {
                        MAX_DETAIL_TYPES = mgr.prototypeList.Count + 2;
                    }
                }
                detailsHere5x5 = new DetailInfo[MAX_DETAIL_TYPES * 5 * 5];
                Logger.LogInfo($"Set MAX_DETAIL_TYPES to {MAX_DETAIL_TYPES}");
            }

            if (MAX_DETAIL_TYPES < manager.prototypeList.Count)
            {
                MAX_DETAIL_TYPES = manager.prototypeList.Count + 2;
                detailsHere5x5 = new DetailInfo[MAX_DETAIL_TYPES * 5 * 5];
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

                        detailsHere5x5[GetDetailInfoIndex(x, y, d)] = new DetailInfo()
                        {
                            casted = true,
                            name = manager.prototypeList[d].name,
                            count = count,
                        };

                        if (x >= 1 && x <= 3 && y >= 1 && y <= 3) recentDetailCount3x3 += count;
                    }
            }

            lastTerrainCheckPos = position;
            #region BENCHMARK
            _benchmarkSWTerrainCheck?.Stop();
            #endregion
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

        // Layout: [block of all detail types at the pos] * maxX * maxY 
        int GetDetailInfoIndex(int x5x5, int y5x5, int detailId) => (y5x5 * 5 + x5x5) * MAX_DETAIL_TYPES + detailId;

        TerrainDetailScore[] detailScoreCache = new TerrainDetailScore[20];
        /// <summary>
        /// Calculate new or retrieve cached score for the specified enemy dir, dis, vertical vision angle
        /// </summary>
        public TerrainDetailScore CalculateDetailScore(Vector3 enemyDirection, float dis, float verticalAxisAngle)
        {    
            int dir = 5;
            IEnumerable<int> it = null;
            TerrainDetailScore cache = default;
            float scaling = 1f;
            
            
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

            if (detailsHere5x5 == null) return cache; // Could be resizing?

            foreach (var pos in it)
            {
                for (int i = 0; i < MAX_DETAIL_TYPES; i++)
                {
                    var info = detailsHere5x5[pos * MAX_DETAIL_TYPES + i];
                    if (!info.casted) continue;
                    Utility.CalculateDetailScore(info.name, info.count, out var s1, out var s2);
                    s1 *= scaling;
                    s2 *= scaling;
                    cache.prone += s1;
                    cache.regular += s2;
                }
            }

            cache.cached = true;
            detailScoreCache[dir] = cache;
            return cache;

            bool TryGetCache(int index, out TerrainDetailScore cache)
            {
                cache = detailScoreCache[index];
                return cache.cached;
            }
        }

        public TerrainDetailScore CalculateCenterDetailScore(bool unscaled = false)
        {
            TerrainDetailScore cache = default;
            float scaling = unscaled? 1f : 9f;
            if (TryGetCache(0, out cache))  return cache;

            if (detailsHere5x5 == null) return cache; // Could be resizing?

            for (int i = 0; i < MAX_DETAIL_TYPES; i++)
            {
                var info = detailsHere5x5[(2*5+2) * MAX_DETAIL_TYPES + i];
                if (!info.casted) continue;
                Utility.CalculateDetailScore(info.name, info.count, out var s1, out var s2);
                s1 *= scaling;
                s2 *= scaling;
                cache.prone += s1;
                cache.regular += s2;
            }

            cache.cached = true;
            detailScoreCache[0] = cache;
            return cache;

            bool TryGetCache(int index, out TerrainDetailScore cache)
            {
                cache = detailScoreCache[index];
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

        int MAX_DETAIL_TYPES = 20;
    }

    public struct TerrainDetailScore
    {
        public bool cached;
        public float prone;
        public float regular;

        public TerrainDetailScore(bool item1, float item2, float item3)
        {
            cached = item1;
            prone = item2;
            regular = item3;
        }

        public override bool Equals(object obj)
        {
            return obj is TerrainDetailScore other &&
                   cached == other.cached &&
                   prone == other.prone &&
                   regular == other.regular;
        }

        public override int GetHashCode()
        {
            int hashCode = 1044908159;
            hashCode = hashCode * -1521134295 + cached.GetHashCode();
            hashCode = hashCode * -1521134295 + prone.GetHashCode();
            hashCode = hashCode * -1521134295 + regular.GetHashCode();
            return hashCode;
        }

        public void Deconstruct(out bool item1, out float item2, out float item3)
        {
            item1 = cached;
            item2 = prone;
            item3 = regular;
        }

        public static implicit operator (bool, float, float)(TerrainDetailScore value)
        {
            return (value.cached, value.prone, value.regular);
        }

        public static implicit operator TerrainDetailScore((bool, float, float, float, float) value)
        {
            return new TerrainDetailScore(value.Item1, value.Item2, value.Item3);
        }
    }
}