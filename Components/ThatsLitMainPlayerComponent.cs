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

namespace ThatsLit
{
    // TODO:
    // # Experiment: Full body lit check
    // Assign player thorax to layer N and set ShadowCastingMode on
    // un-cull N for all Lights
    // Cull N for FPS cam
    // Un-Cull N for TL cam
    //
    // # Reduce stealth when near stationary weapons not in extreame darkness
    // # Cast for EFT.Interactive.StationaryWeapon/AGSMachinery/Utes on layer Interactive
    public class ThatsLitMainPlayerComponent : MonoBehaviour
    {
        public const int DEBUG_INTERVAL = 61;
        public static bool IsDebugSampleFrame { get => ThatsLitPlugin.DebugInfo.Value && Time.frameCount % DEBUG_INTERVAL == 0; }
        readonly int RESOLUTION = 32 * ThatsLitPlugin.ResLevel.Value;
        public const int POWER = 3;
        public CustomRenderTexture rt;
        Texture slowRT;
        public Camera cam, envCam;
        Unity.Collections.NativeArray<Color32> observed;
        public RawImage display;
        public PlayerDebugInfo DebugInfo { get; internal set; }
        public LightAndLaserState LightAndLaserState { get; internal set; }
        float awakeAt;
        float lastCheckedLights;
        public static RaidSettings ActiveRaidSettings => Singleton<ThatsLitGameworld>.Instance.activeRaidSettings;
        public Player Player { get; internal set; }
        public float fog, rain, cloud;
        AsyncGPUReadbackRequest gquReq;
        internal float lastOutside;
        /// <summary>
        /// 0~10
        /// </summary>
        internal float ambienceShadownRating;
        internal float AmbienceShadowFactor => Mathf.Pow(ambienceShadownRating / 10f, 2); 
        internal float bunkerTimeClamped;
        internal float lastInBunkerTime, lastOutBunkerTime;
        internal Vector3 lastInBunderPos;
        internal PlayerTerrainDetailsProfile TerrainDetails;
        internal PlayerFoliageProfile Foliage;
        internal PlayerLitScoreProfile PlayerLitScoreProfile { get; set;}
        static readonly LayerMask ambienceRaycastMask = (1 << LayerMask.NameToLayer("Terrain")) | (1 << LayerMask.NameToLayer("HighPolyCollider")) | (1 << LayerMask.NameToLayer("Grass")) | (1 << LayerMask.NameToLayer("Foliage"));
        internal delegate bool CheckStimEffectProxy (EFT.HealthSystem.EStimulatorBuffType buff);
        internal CheckStimEffectProxy CheckEffectDelegate
        {
            get
            {
                if (checkEffectDelegate == null)
                {
                    var methodInfo = ReflectionHelper.FindMethodByArgTypes(Player.ActiveHealthController.GetType(), new Type[] { typeof(EFT.HealthSystem.EStimulatorBuffType) }, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    checkEffectDelegate = (CheckStimEffectProxy) methodInfo.CreateDelegate(typeof(CheckStimEffectProxy));
                }
                return checkEffectDelegate;
            }
        }
        private CheckStimEffectProxy checkEffectDelegate;
        public static bool CanLoad ()
        {
            bool result = CameraClass.Instance.OpticCameraManager.Camera != null;
            switch (ActiveRaidSettings?.LocationId)
            {
                case "factory4_day":
                case "laboratory":
                case null:
                    result = true;
                    break;
                case "Lighthouse":
                    result = ThatsLitPlugin.EnableLighthouse.Value & result;
                    break;
                case "Woods":
                    result = ThatsLitPlugin.EnableWoods.Value & result;
                    break;
                case "factory4_night":
                    result = ThatsLitPlugin.EnableFactoryNight.Value & result;
                    break;
                case "bigmap": // Customs
                    result = ThatsLitPlugin.EnableCustoms.Value & result;
                    break;
                case "RezervBase": // Reserve
                    result = ThatsLitPlugin.EnableReserve.Value & result;
                    break;
                case "Interchange":
                    result = ThatsLitPlugin.EnableInterchange.Value & result;
                    break;
                case "TarkovStreets":
                    result = ThatsLitPlugin.EnableStreets.Value & result;
                    break;
                case "Sandbox": // GZ
                    result = ThatsLitPlugin.EnableGroundZero.Value & result;
                    break;
                case "Shoreline":
                    result = ThatsLitPlugin.EnableShoreline.Value & result;
                    break;
                default:
                    break;
            }
            
            if (CameraClass.Instance.OpticCameraManager.Camera != null
             && prefab == null)
            {
                CameraClass.Instance.OpticCameraManager.Camera.gameObject.SetActive(false);
                prefab = GameObject.Instantiate<Camera>(CameraClass.Instance.OpticCameraManager.Camera);
                CameraClass.Instance.OpticCameraManager.Camera.gameObject.SetActive(true);
                prefab.gameObject.name = "That's Lit Camera (Prefab)";
                foreach (var c in prefab.gameObject.GetComponents<MonoBehaviour>())
                switch (c) {
                    case AreaLightManager areaLightManager:
                        break;
                    default:
                        MonoBehaviour.Destroy(c);
                        break;
                }
                prefab.clearFlags = CameraClearFlags.SolidColor;
                prefab.backgroundColor = new Color (0, 0, 0, 0);

                prefab.nearClipPlane = 0.001f;
                prefab.farClipPlane = 5f;

                prefab.cullingMask = LayerMaskClass.PlayerMask;
                prefab.fieldOfView = 44;
            }

            return result;
        }

        static Camera prefab;
        internal void Setup (Player player)
        {
            this.Player = player;

            awakeAt = Time.time;
            if (Player.IsYourPlayer) DebugInfo = new PlayerDebugInfo();

            MaybeEnableBrightness();
        }

        void MaybeEnableBrightness ()
        {
            if (!ThatsLitPlugin.EnabledLighting.Value
             || Singleton<ThatsLitGameworld>.Instance.ScoreCalculator == null)
                return;

            if (PlayerLitScoreProfile == null) PlayerLitScoreProfile = new PlayerLitScoreProfile(this);

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
                cam = GameObject.Instantiate<Camera>(prefab);
                cam.gameObject.name = "That's Lit Camera";
                cam.transform.SetParent(Player.Transform.Original);
                cam.targetTexture = rt;
                cam.gameObject.SetActive(true);
            }
            else cam.enabled = true;


            if (Player.IsYourPlayer && ThatsLitPlugin.DebugTexture.Value)
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
        }

        void DisableBrightness ()
        {
            if (cam) cam.enabled = false;
            if (display) display.enabled = false;
            PlayerLitScoreProfile = null;
        }

        private void Update()
        {
            if (Player == null) return;
            if (Player?.HealthController?.IsAlive == false)
            {
                MonoBehaviour.Destroy(this);
                return;
            }

            if (!ThatsLitPlugin.EnabledMod.Value)
            {
                if (cam?.enabled ?? false) GameObject.Destroy(cam.gameObject);
                if (rt != null) rt.Release();
                if (display?.enabled ?? false) GameObject.Destroy(display);
                this.enabled = false;
                return;
            }

            #region BENCHMARK
            ThatsLitPlugin.swUpdate.MaybeResumme();
            #endregion


            Vector3 bodyPos = Player.MainParts[BodyPartType.body].Position;

            if (!Player.AIData.IsInside) lastOutside = Time.time;

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

            using (new ManagedStopWatch.RunningScope(ThatsLitPlugin.swTerrain))
            {
                if (ThatsLitPlugin.EnabledGrasses.Value
                 && !Singleton<ThatsLitGameworld>.Instance.terrainDetailsUnavailable
                 && TerrainDetails == null)
                {
                    TerrainDetails = new PlayerTerrainDetailsProfile();
                }

                if (TerrainDetails != null
                && Time.time > TerrainDetails.LastCheckedTime + 0.41f)
                {
                    Singleton<ThatsLitGameworld>.Instance.CheckTerrainDetails(bodyPos, TerrainDetails);
                    if (ThatsLitPlugin.TerrainInfo.Value)
                    {
                        var score = Singleton<ThatsLitGameworld>.Instance.CalculateDetailScore(TerrainDetails, Vector3.zero, 0, 0);
                        terrainScoreHintProne = score.prone;
                        terrainScoreHintRegular = score.regular;

                        var pf = (Player.PoseLevel / Player.AIData.Player.Physical.MaxPoseLevel) * 0.6f + 0.4f;
                        terrainScoreHintRegular /= (pf + 0.1f + 0.25f * Mathf.InverseLerp(0.45f, 0.55f, pf));
                    }
                }
            }

            using (new ManagedStopWatch.RunningScope(ThatsLitPlugin.swFoliage))
            {
                if (ThatsLitPlugin.EnabledFoliage.Value
                 && !Singleton<ThatsLitGameworld>.Instance.foliageUnavailable
                 && Foliage == null)
                {
                    Foliage = new PlayerFoliageProfile(new FoliageInfo[16], new Collider[16]);
                }
                if (Foliage != null)
                {
                    if (!Foliage.IsFoliageSorted) Foliage.IsFoliageSorted = SlicedBubbleSort(Foliage.Foliage, Foliage.FoliageCount * 3 / 2, Foliage.FoliageCount);
                    Singleton<ThatsLitGameworld>.Instance.UpdateFoliageScore(bodyPos, Foliage);
                }
            }

            if (PlayerLitScoreProfile == null && ThatsLitPlugin.EnabledLighting.Value)
            {
                MaybeEnableBrightness();
                ThatsLitPlugin.swUpdate.Stop();
                return;
            }
            else if (PlayerLitScoreProfile != null && !ThatsLitPlugin.EnabledLighting.Value)
            {
                DisableBrightness();
                ThatsLitPlugin.swUpdate.Stop();
                return;
            }

            if (PlayerLitScoreProfile == null)
            {
                ThatsLitPlugin.swUpdate.Stop();
                return;
            }

            if (gquReq.done) gquReq = AsyncGPUReadback.Request(rt, 0, req =>
            {
                if (!req.hasError)
                {
                    observed.Dispose();
                    observed = req.GetData<Color32>();
                    using (new ManagedStopWatch.RunningScope(ThatsLitPlugin.swScoreCalc))
                        Singleton<ThatsLitGameworld>.Instance.ScoreCalculator?.PreCalculate(observed, Utility.GetInGameDayTime());
                }
            });

            var camPos = Time.frameCount % 6;
            var camHeight = Player.IsInPronePose ? 0.45f : 2.2f * (0.6f + 0.4f * Player.PoseLevel);
            var targetHeight = Player.IsInPronePose ? 0.2f : 0.7f;
            var horizontalScale = Player.IsInPronePose ? 1.2f : 0.8f;
            switch (Time.frameCount % 6)
            {
                case 0:
                    {
                        if (Player.IsInPronePose)
                        {
                            cam.transform.localPosition = new Vector3(0, 2, 0);
                            cam.transform.LookAt(Player.Transform.Original.position);
                        }
                        else
                        {
                            cam.transform.localPosition = new Vector3(0, camHeight, 0);
                            cam.transform.LookAt(Player.Transform.Original.position);
                        }
                        break;
                    }
                case 1:
                    {
                        cam.transform.localPosition = new Vector3(horizontalScale, camHeight, horizontalScale);
                        cam.transform.LookAt(Player.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
                case 2:
                    {
                        cam.transform.localPosition = new Vector3(horizontalScale, camHeight, -horizontalScale);
                        cam.transform.LookAt(Player.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
                case 3:
                    {
                        if (Player.IsInPronePose)
                        {
                            cam.transform.localPosition = new Vector3(0, 2f, 0);
                            cam.transform.LookAt(Player.Transform.Original.position);
                        }
                        else
                        {
                            cam.transform.localPosition = new Vector3(0, -0.5f, 0.35f);
                            cam.transform.LookAt(Player.Transform.Original.position + Vector3.up * 1f);
                        }
                        break;
                    }
                case 4:
                    {
                        cam.transform.localPosition = new Vector3(-horizontalScale, camHeight, -horizontalScale);
                        cam.transform.LookAt(Player.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
                case 5:
                    {
                        cam.transform.localPosition = new Vector3(-horizontalScale, camHeight, horizontalScale);
                        cam.transform.LookAt(Player.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
            }

            if (ThatsLitPlugin.DebugTexture.Value && Time.frameCount % 61 == 0 && display?.enabled == true) Graphics.CopyTexture(rt, slowRT);

            // Ambient shadow
            UpdateAmbienceShadowRating();

            overheadHaxRating = UpdateOverheadHaxCastRating(bodyPos, overheadHaxRating);

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

            if (ThatsLitPlugin.EnableEquipmentCheck.Value && Time.time > lastCheckedLights + (ThatsLitPlugin.LessEquipmentCheck.Value ? 0.61f : 0.33f))
            {
                lastCheckedLights = Time.time;
                Utility.DetermineShiningEquipments(Player, out var vLight, out var vLaser, out var irLight, out var irLaser, out var vLightSub, out var vLaserSub, out var irLightSub, out var irLaserSub);
                var state = LightAndLaserState;
                state.VisibleLight = vLight;
                state.VisibleLaser = vLaser;
                state.IRLight = irLight;
                state.IRLaser = irLaser;
                state.VisibleLightSub = vLightSub;
                state.VisibleLaserSub = vLaserSub;
                state.IRLightSub = irLightSub;
                state.IRLaserSub = irLaserSub;
                LightAndLaserState = state;
            }

            ThatsLitPlugin.swUpdate.Stop();
        }

        private void UpdateAmbienceShadowRating()
        {
            Vector3 headPos = Player.MainParts[BodyPartType.head].Position;
            Vector3 lhPos = Player.MainParts[BodyPartType.leftArm].Position;
            Vector3 rhPos = Player.MainParts[BodyPartType.rightArm].Position;
            Vector3 lPos = Player.MainParts[BodyPartType.leftLeg].Position;
            Vector3 rPos = Player.MainParts[BodyPartType.rightLeg].Position;
            if (TOD_Sky.Instance != null)
            {
                Ray ray = default;
                Vector3 ambienceDir = Singleton<ThatsLitGameworld>.Instance.ScoreCalculator.CalculateSunLightTimeFactor(ActiveRaidSettings.LocationId, Utility.GetInGameDayTime()) > 0.05f ? TOD_Sky.Instance.LocalSunDirection : TOD_Sky.Instance.LightDirection;
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
        }

        private float UpdateOverheadHaxCastRating(Vector3 bodyPos, float currentRating)
        {
            if (OverheadHaxCast(bodyPos, out var haxHit))
            {
                currentRating += Time.timeScale * Mathf.Clamp01(10f - haxHit.distance);
            }
            else
            {
                currentRating -= Time.timeScale * 2f;
            }
            return Mathf.Clamp(currentRating, 0f, 10f);
        }

        void LateUpdate()
        {
            if (Player == null) return;
            if (PlayerLitScoreProfile == null) return;
            Singleton<ThatsLitGameworld>.Instance.GetWeatherStats(out fog, out rain, out cloud);

            //if (debugTex != null && Time.frameCount % 61 == 0) Graphics.CopyTexture(tex, debugTex);
            // if (envDebugTex != null && Time.frameCount % 61 == 0) Graphics.CopyTexture(envTex, envDebugTex);

            if (!observed.IsCreated) return;
            using (new ManagedStopWatch.RunningScope(ThatsLitPlugin.swScoreCalc))
                Singleton<ThatsLitGameworld>.Instance.ScoreCalculator?.CalculateMultiFrameScore(observed, cloud, fog, rain, Singleton<ThatsLitGameworld>.Instance, PlayerLitScoreProfile, Utility.GetInGameDayTime(), ActiveRaidSettings.LocationId);
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
            return RaycastIgnoreGlass(ray, 30, ambienceRaycastMask, out hit, out var lp);
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

        // private void UpdateFoliageScore(Vector3 bodyPos)
        // {
        //     lastCheckedFoliages = Time.time;
        //     foliageScore = 0;

        //     // Skip if basically standing still
        //     if ((bodyPos - lastFoliageCheckPos).magnitude < 0.05f)
        //     {
        //         return;
        //     }

        //     Array.Clear(foliage, 0, foliage.Length);
        //     Array.Clear(collidersCache, 0, collidersCache.Length);

        //     if (skipFoliageCheck) return;

        //     int castedCount = Physics.OverlapSphereNonAlloc(bodyPos, 4f, collidersCache, foliageLayerMask);
        //     int validCount = 0;

        //     for (int i = 0; i < castedCount; i++)
        //     {
        //         Collider casted = collidersCache[i];
        //         if (casted.gameObject.transform.root.gameObject.layer == 8) continue; // Somehow sometimes player spines are tagged PlayerSpiritAura, VB or vanilla?
        //         if (casted.gameObject.GetComponent<Terrain>()) continue; // Somehow sometimes terrains can be casted
        //         Vector3 bodyToFoliage = casted.transform.position - bodyPos;

        //         float dis = bodyToFoliage.magnitude;
        //         if (dis < 0.25f) foliageScore += 1f;
        //         else if (dis < 0.35f) foliageScore += 0.9f;
        //         else if (dis < 0.5f) foliageScore += 0.8f;
        //         else if (dis < 0.6f) foliageScore += 0.7f;
        //         else if (dis < 0.7f) foliageScore += 0.5f;
        //         else if (dis < 1f) foliageScore += 0.3f;
        //         else if (dis < 2f) foliageScore += 0.2f;
        //         else foliageScore += 0.1f;

        //         string fname = casted?.transform.parent.gameObject.name;
        //         if (string.IsNullOrWhiteSpace(fname)) continue;

        //         if (ThatsLitPlugin.FoliageSamples.Value == 1 && (foliage[0] == default || dis < foliage[0].dis)) // don't bother
        //         {
        //             foliage[0] = new FoliageInfo(fname, new Vector3(bodyToFoliage.x, bodyToFoliage.z), dis);
        //             validCount = 1;
        //             continue;
        //         }
        //         else foliage[validCount] = new FoliageInfo(fname, new Vector3(bodyToFoliage.x, bodyToFoliage.z), dis);
        //         validCount++;
        //     }

        //     for (int j = 0; j < validCount; j++)
        //     {
        //         var f = foliage[j];
        //         f.name = Regex.Replace(f.name, @"(.+?)\s?(\(\d+\))?", "$1");
        //         f.dis = f.dir.magnitude; // Use horizontal distance to replace casted 3D distance
        //         foliage[j] = f;
        //     }
        //     isFoliageSorted = false;
        //     if (foliage.Length == 1 || validCount == 1)
        //     {
        //         isFoliageSorted = true;
        //     }

        //     switch (castedCount)
        //     {
        //         case 1:
        //             foliageScore /= 3.3f;
        //             break;
        //         case 2:
        //             foliageScore /= 2.8f;
        //             break;
        //         case 3:
        //             foliageScore /= 2.3f;
        //             break;
        //         case 4:
        //             foliageScore /= 1.8f;
        //             break;
        //         case 5:
        //         case 6:
        //             foliageScore /= 1.2f;
        //             break;
        //         case 11:
        //         case 12:
        //         case 13:
        //             foliageScore /= 1.15f;
        //             break;
        //         case 14:
        //         case 15:
        //         case 16:
        //             foliageScore /= 1.25f;
        //             break;
        //     }

        //     foliageCount = validCount;

        //     lastFoliageCheckPos = bodyPos;
        // }

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

        void OnGUIInfo ()
        {
            if (PlayerLitScoreProfile != null) Utility.GUILayoutDrawAsymetricMeter((int)(PlayerLitScoreProfile.frame0.multiFrameLitScore / 0.0999f));
            if (PlayerLitScoreProfile != null) Utility.GUILayoutDrawAsymetricMeter((int)(Mathf.Pow(PlayerLitScoreProfile.frame0.multiFrameLitScore, POWER) / 0.0999f));
            if (Foliage != null && Foliage.FoliageScore > 0 && ThatsLitPlugin.FoliageInfo.Value)
                Utility.GUILayoutFoliageMeter((int)(Foliage.FoliageScore / 0.0999f));
            if (TerrainDetails != null && terrainScoreHintProne > 0.0998f && ThatsLitPlugin.TerrainInfo.Value)
                if (Player.IsInPronePose) Utility.GUILayoutTerrainMeter((int)(terrainScoreHintProne / 0.0999f));
                else Utility.GUILayoutTerrainMeter((int)(terrainScoreHintRegular / 0.0999f));
            if (Time.time < awakeAt + 10)
                GUILayout.Label("  [That's Lit HUD] Can be disabled in plugin settings.");

            if (PlayerLitScoreProfile != null)
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
        private void OnGUI()
        {
            if (Player?.IsYourPlayer != true) return;
            bool layoutCall = guiFrame < Time.frameCount;

            using (new ManagedStopWatch.RunningScope(ThatsLitPlugin.swGUI))
            {
                if (PlayerLitScoreProfile == null)
                {
                    if (Time.time - awakeAt < 30f && !ThatsLitPlugin.HideMapTip.Value)
                        GUILayout.Label("  [That's Lit] Brightness module is disabled in configs or not supported on this map.");
                }

                var poseFactor = Player.PoseLevel / Player.Physical.MaxPoseLevel * 0.6f + 0.4f; // crouch: 0.4f
                if (Player.IsInPronePose) poseFactor -= 0.4f; // prone: 0
                poseFactor += 0.05f; // base -> prone -> 0.05f, crouch -> 0.45f

                if (ThatsLitPlugin.DebugInfo.Value || ThatsLitPlugin.ScoreInfo.Value)
                    OnGUIInfo();

                if (!ThatsLitPlugin.DebugInfo.Value || DebugInfo == null) return;

                if (IsDebugSampleFrame)
                {
                    litFactorSample = PlayerLitScoreProfile?.litScoreFactor ?? 0;
                    ambScoreSample = PlayerLitScoreProfile?.frame0.ambienceScore ?? 0;
                    if (ThatsLitPlugin.EnableBenchmark.Value && layoutCall) // The trap here is OnGUI is called multiple times per frame, make sure to reset the stopwatches only once
                    {
                        ConcludeBenchmarks();
                    }
                }
                Singleton<ThatsLitGameworld>.Instance.ScoreCalculator?.OnGUI(layoutCall);
                // GUILayout.Label(string.Format(" IMPACT: {0:0.000} -> {1:0.000} ({2:0.000} <- {3:0.000} <- {4:0.000}) AMB: {5:0.00} LIT: {6:0.00} (SAMPLE)", lastCalcFrom, lastCalcTo, lastFactor2, lastFactor1, lastScore, ambScoreSample, litFactorSample));
                //GUILayout.Label(text: "PIXELS:");
                //GUILayout.Label(lastValidPixels.ToString());
                // GUILayout.Label(string.Format(" AFFECTED: {0} (+{1}) / ENCOUNTER: {2}", calced, calcedLastFrame, encounter));

                // GUILayout.Label(string.Format(" FOLIAGE: {0:0.000} ({1}) (H{2:0.00} Y{3:0.00} to {4})", foliageScore, foliageCount, foliageDisH, foliageDisV, foliage));

                                    // GUILayout.Label(string.Format(" POSE: {0:0.000} LOOK: {1} ({2})", poseFactor, MainPlayer.LookDirection, DetermineDir(MainPlayer.LookDirection)));
                                    // GUILayout.Label(string.Format(" {0} {1} {2}", collidersCache[0]?.gameObject.name, collidersCache[1]?.gameObject?.name, collidersCache[2]?.gameObject?.name));
                float fog = WeatherController.Instance?.WeatherCurve?.Fog ?? 0;
                float rain = WeatherController.Instance?.WeatherCurve?.Rain ?? 0;
                float cloud = WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0;
                
                    infoCache1 = $"  IMPACT: {DebugInfo.lastCalcFrom:0.000} -> {DebugInfo.lastCalcTo:0.000} ({DebugInfo.lastFactor2:0.000} <- {DebugInfo.lastFactor1:0.000} <- {DebugInfo.lastScore:0.000}) AMB: {ambScoreSample:0.00} LIT: {litFactorSample:0.00} (SAMPLE)\n  AFFECTED: {DebugInfo.calced} (+{DebugInfo.calcedLastFrame}) / ENCOUNTER: {DebugInfo.encounter}\n  TERRAIN: { terrainScoreHintProne :0.000}/{ terrainScoreHintRegular :0.000} 3x3:( { TerrainDetails.RecentDetailCount3x3 } ) (score-{ PlayerLitScoreProfile.detailBonusSmooth:0.00})  FOLIAGE: {Foliage?.FoliageScore:0.000} ({Foliage?.FoliageCount}) (H{Foliage?.Foliage?[0].dis:0.00} to {Foliage?.Foliage?[0].name})\n  FOG: {fog:0.000} / RAIN: {rain:0.000} / CLOUD: {cloud:0.000} / TIME: {Utility.GetInGameDayTime():0.000} / WINTER: {Singleton<ThatsLitGameworld>.Instance.IsWinter}\n  POSE: {poseFactor} SPEED: { Player.Velocity.magnitude :0.000}  INSIDE: { Time.time - lastOutside:0.000}  AMB: { ambienceShadownRating:0.000}  OVH: { overheadHaxRating:0.000}  BNKR: { bunkerTimeClamped:0.000}";
                if (layoutCall)
                GUILayout.Label(infoCache1);
                // GUILayout.Label(string.Format(" FOG: {0:0.000} / RAIN: {1:0.000} / CLOUD: {2:0.000} / TIME: {3:0.000} / WINTER: {4}", WeatherController.Instance?.WeatherCurve?.Fog ?? 0, WeatherController.Instance?.WeatherCurve?.Rain ?? 0, WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0, GetInGameDayTime(), isWinterCache));
                
                GUILayout.Label(LightAndLaserState.Format());
                OnGUIScoreCalc();

                if (ThatsLitPlugin.EnableBenchmark.Value)
                {
                    if (layoutCall)
                        infoCacheBenchmark = $"  Update: {benchmarkSampleUpdate,8:0.000}\n    Foliage: {benchmarkSampleFoliageCheck,8:0.000}\n    Terrain: {benchmarkSampleTerrainCheck,8:0.000}\n  SeenCoef: {benchmarkSampleSeenCoef,8:0.000}\n  Encountering: {benchmarkSampleEncountering,8:0.000}\n  ExtraVisDis: {benchmarkSampleExtraVisDis,8:0.000}\n  ScoreCalculator: {benchmarkSampleScoreCalculator,8:0.000}\n  Info(+Debug): {benchmarkSampleGUI,8:0.000} ms";
                    GUILayout.Label(infoCacheBenchmark);
                    if (Time.frameCount % 6000 == 0)
                        if (layoutCall) EFT.UI.ConsoleScreen.Log(infoCacheBenchmark);
                }

                if (ThatsLitPlugin.DebugTerrain.Value && TerrainDetails?.Details5x5 != null)
                {
                    infoCache2 = $"DETAIL (SAMPLE): {DebugInfo?.lastFinalDetailScoreNearest:+0.00;-0.00;+0.00} ({DebugInfo?.lastDisFactorNearest:0.000}df) 3x3: { TerrainDetails.RecentDetailCount3x3}\n  {Utility.DetermineDir(DebugInfo?.lastTriggeredDetailCoverDirNearest ?? Vector3.zero)} {DebugInfo?.lastNearest:0.00}m {DebugInfo?.lastTiltAngle} {DebugInfo?.lastRotateAngle}";
                    GUILayout.Label(infoCache2);
                    // GUILayout.Label(string.Format(" DETAIL (SAMPLE): {0:+0.00;-0.00;+0.00} ({1:0.000}df) 3x3: {2}", arg0: lastFinalDetailScoreNearest, lastDisFactorNearest, recentDetailCount3x3));
                    // GUILayout.Label(string.Format(" {0} {1:0.00}m {2} {3}", Utility.DetermineDir(lastTriggeredDetailCoverDirNearest), lastNearest, lastTiltAngle, lastRotateAngle));
                    for (int i = TerrainDetails.GetDetailInfoIndex(2, 2, 0, Singleton<ThatsLitGameworld>.Instance.MaxDetailTypes); i < TerrainDetails.GetDetailInfoIndex(3, 2, 0, Singleton<ThatsLitGameworld>.Instance.MaxDetailTypes); i++) // List the underfoot
                        if (TerrainDetails.Details5x5[i].casted)
                            GUILayout.Label($"  { TerrainDetails.Details5x5[i].count } Detail#{i}({ TerrainDetails.Details5x5[i].name }))");
                    Utility.GUILayoutDrawAsymetricMeter((int)(DebugInfo.lastFinalDetailScoreNearest / 0.0999f));
                }

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
            guiFrame = Time.frameCount;
            
        }
        string infoCache;
        internal virtual void OnGUIScoreCalc ()
        {
            // Utility.GUILayoutDrawAsymetricMeter((int)(baseAmbienceScore / 0.0999f));
            // Utility.GUILayoutDrawAsymetricMeter((int)(ambienceScore / 0.0999f));
            // Utility.GUILayoutDrawAsymetricMeter((int)(frame0.multiFrameLitScore / 0.0999f));
            if (Time.frameCount % 41 == 0)
            {
                DebugInfo.shinePixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioShinePixels + PlayerLitScoreProfile.frame1.RatioShinePixels + PlayerLitScoreProfile.frame2.RatioShinePixels + PlayerLitScoreProfile.frame3.RatioShinePixels + PlayerLitScoreProfile.frame4.RatioShinePixels + PlayerLitScoreProfile.frame5.RatioShinePixels) / 6f;
                DebugInfo.highLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioHighPixels + PlayerLitScoreProfile.frame1.RatioHighPixels + PlayerLitScoreProfile.frame2.RatioHighPixels + PlayerLitScoreProfile.frame3.RatioHighPixels + PlayerLitScoreProfile.frame4.RatioHighPixels + PlayerLitScoreProfile.frame5.RatioHighPixels) / 6f;
                DebugInfo.highMidLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioHighMidPixels + PlayerLitScoreProfile.frame1.RatioHighMidPixels + PlayerLitScoreProfile.frame2.RatioHighMidPixels + PlayerLitScoreProfile.frame3.RatioHighMidPixels + PlayerLitScoreProfile.frame4.RatioHighMidPixels + PlayerLitScoreProfile.frame5.RatioHighMidPixels) / 6f;
                DebugInfo.midLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioMidPixels + PlayerLitScoreProfile.frame1.RatioMidPixels + PlayerLitScoreProfile.frame2.RatioMidPixels + PlayerLitScoreProfile.frame3.RatioMidPixels + PlayerLitScoreProfile.frame4.RatioMidPixels + PlayerLitScoreProfile.frame5.RatioMidPixels) / 6f;
                DebugInfo.midLowLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioMidLowPixels + PlayerLitScoreProfile.frame1.RatioMidLowPixels + PlayerLitScoreProfile.frame2.RatioMidLowPixels + PlayerLitScoreProfile.frame3.RatioMidLowPixels + PlayerLitScoreProfile.frame4.RatioMidLowPixels + PlayerLitScoreProfile.frame5.RatioMidLowPixels) / 6f;
                DebugInfo.lowLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioLowPixels + PlayerLitScoreProfile.frame1.RatioLowPixels + PlayerLitScoreProfile.frame2.RatioLowPixels + PlayerLitScoreProfile.frame3.RatioLowPixels + PlayerLitScoreProfile.frame4.RatioLowPixels + PlayerLitScoreProfile.frame5.RatioLowPixels) / 6f;
                DebugInfo.darkPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioDarkPixels + PlayerLitScoreProfile.frame1.RatioDarkPixels + PlayerLitScoreProfile.frame2.RatioDarkPixels + PlayerLitScoreProfile.frame3.RatioDarkPixels + PlayerLitScoreProfile.frame4.RatioDarkPixels + PlayerLitScoreProfile.frame5.RatioDarkPixels) / 6f;
            }
            if (guiFrame < Time.frameCount) infoCache = $"  PIXELS: {DebugInfo.shinePixelsRatioSample * 100:000}% - {DebugInfo.highLightPixelsRatioSample * 100:000}% - {DebugInfo.highMidLightPixelsRatioSample * 100:000}% - { DebugInfo.midLightPixelsRatioSample * 100:000}% - {DebugInfo.midLowLightPixelsRatioSample * 100:000}% - {DebugInfo.lowLightPixelsRatioSample * 100:000}% | {DebugInfo.darkPixelsRatioSample * 100:000}% (AVG Sample)\n  AvgLumMF: {PlayerLitScoreProfile.frame0.avgLumMultiFrames:0.000} / {Singleton<ThatsLitGameworld>.Instance.ScoreCalculator.GetMinAmbianceLum():0.000} ~ {Singleton<ThatsLitGameworld>.Instance.ScoreCalculator.GetMaxAmbianceLum():0.000} ({Singleton<ThatsLitGameworld>.Instance.ScoreCalculator.GetAmbianceLumRange():0.000})\n   Sun: {Singleton<ThatsLitGameworld>.Instance.ScoreCalculator.sunLightScore:0.000}/{Singleton<ThatsLitGameworld>.Instance.ScoreCalculator.GetMaxSunlightScore():0.000}, Moon: {Singleton<ThatsLitGameworld>.Instance.ScoreCalculator.moonLightScore:0.000}/{Singleton<ThatsLitGameworld>.Instance.ScoreCalculator.GetMaxMoonlightScore():0.000}\n  SCORE : {DebugInfo.scoreRawBase:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw0:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw1:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw2:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw3:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw4:＋0.00;－0.00;+0.00} (SAMPLE)";            
            GUILayout.Label(infoCache);
            // GUILayout.Label(string.Format("  PIXELS: {0:000}% - {1:000}% - {2:000}% - {3:000}% - {4:000}% - {5:000}% | {6:000}% (AVG Sample)", shinePixelsRatioSample * 100, highLightPixelsRatioSample * 100, highMidLightPixelsRatioSample * 100, midLightPixelsRatioSample * 100, midLowLightPixelsRatioSample * 100, lowLightPixelsRatioSample * 100, darkPixelsRatioSample * 100));
            // GUILayout.Label(string.Format("  AvgLumMF: {0:0.000} / {1:0.000} ~ {2:0.000} ({3:0.000})", frame0.avgLumMultiFrames, GetMinAmbianceLum(), GetMaxAmbianceLum(), GetAmbianceLumRange()));
            // GUILayout.Label(string.Format("  Sun: {0:0.000}/{1:0.000}, Moon: {2:0.000}/{3:0.000}", sunLightScore, GetMaxSunlightScore(), moonLightScore, GetMaxMoonlightScore()));
            // GUILayout.Label(string.Format("  SCORE : {0:＋0.00;－0.00;+0.00} -> {1:＋0.00;－0.00;+0.00} -> {2:＋0.00;－0.00;+0.00} -> {3:＋0.00;－0.00;+0.00} (SAMPLE)", scoreRaw1, scoreRaw2, scoreRaw3, scoreRaw4));
            
            Utility.GUILayoutDrawAsymetricMeter((int)(PlayerLitScoreProfile.frame0.score / 0.0999f));
        }

        private void ConcludeBenchmarks()
        {
            benchmarkSampleSeenCoef = ThatsLitPlugin.swSeenCoef.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleEncountering = ThatsLitPlugin.swEncountering.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleExtraVisDis = ThatsLitPlugin.swExtraVisDis.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleScoreCalculator = ThatsLitPlugin.swScoreCalc.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleUpdate = ThatsLitPlugin.swUpdate.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleGUI = ThatsLitPlugin.swGUI.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleFoliageCheck = ThatsLitPlugin.swFoliage.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleTerrainCheck = ThatsLitPlugin.swTerrain.ConcludeMs() / (float) DEBUG_INTERVAL;
        }

        // Dictionary<Terrain, SpatialPartitionClass> terrainSpatialPartitions = new Dictionary<Terrain, SpatialPartitionClass>();
        // Dictionary<Terrain, List<int[,]>> terrainDetailMaps = new Dictionary<Terrain, List<int[,]>>();
        float terrainScoreHintProne, terrainScoreHintRegular;
        // internal Vector3 lastTerrainCheckPos;
        // void CheckTerrainDetails()
        // {
        //     Vector3 position = MainPlayer.MainParts[BodyPartType.body].Position;
        //     if ((position - lastTerrainCheckPos).magnitude < 0.15f) return;

        //     Array.Clear(detailScoreCache, 0, detailScoreCache.Length);
        //     if (detailsHere5x5 != null) Array.Clear(detailsHere5x5, 0, detailsHere5x5.Length);
        //     recentDetailCount3x3 = 0;
        //     var ray = new Ray(position, Vector3.down);
        //     if (!Physics.Raycast(ray, out var hit, 100, LayerMaskClass.TerrainMask)) return;
        //     var terrain = hit.transform?.GetComponent<Terrain>();
        //     GPUInstancerDetailManager manager = terrain?.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;

        //     if (!terrain || !manager || !manager.isInitialized) return;
        //     if (!terrainDetailMaps.TryGetValue(terrain, out var detailMap))
        //     {
        //         if (gatheringDetailMap == null) gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(terrain));
        //         return;
        //     }

        //     #region BENCHMARK
        //     if (ThatsLitPlugin.EnableBenchmark.Value && ThatsLitPlugin.DebugInfo.Value)
        //     {
        //         if (_benchmarkSWTerrainCheck == null) _benchmarkSWTerrainCheck = new System.Diagnostics.Stopwatch();
        //         if (_benchmarkSWTerrainCheck.IsRunning)
        //         {
        //             string message = $"[That's Lit] Benchmark stopwatch is not stopped! (TerrainCheck)";
        //             NotificationManagerClass.DisplayWarningNotification(message);
        //             Logger.LogWarning(message);
        //         }
        //         _benchmarkSWTerrainCheck.Start();
        //     }
        //     else if (_benchmarkSWTerrainCheck != null)
        //         _benchmarkSWTerrainCheck = null;
        //     #endregion
    
        //     Vector3 hitRelativePos = hit.point - (terrain.transform.position + terrain.terrainData.bounds.min);
        //     var currentLocationOnTerrainmap = new Vector2(hitRelativePos.x / terrain.terrainData.size.x, hitRelativePos.z / terrain.terrainData.size.z);

        //     if (detailsHere5x5 == null) // Initialize
        //     {
        //         foreach (var mgr in GPUInstancerDetailManager.activeManagerList)
        //         {
        //             if (MAX_DETAIL_TYPES < mgr.prototypeList.Count)
        //             {
        //                 MAX_DETAIL_TYPES = mgr.prototypeList.Count + 2;
        //             }
        //         }
        //         detailsHere5x5 = new DetailInfo[MAX_DETAIL_TYPES * 5 * 5];
        //         Logger.LogInfo($"Set MAX_DETAIL_TYPES to {MAX_DETAIL_TYPES}");
        //     }

        //     if (MAX_DETAIL_TYPES < manager.prototypeList.Count)
        //     {
        //         MAX_DETAIL_TYPES = manager.prototypeList.Count + 2;
        //         detailsHere5x5 = new DetailInfo[MAX_DETAIL_TYPES * 5 * 5];
        //     }

        //     for (int d = 0; d < manager.prototypeList.Count; d++)
        //     {
        //         var resolution = (manager.prototypeList[d] as GPUInstancerDetailPrototype).detailResolution;
        //         Vector2Int resolutionPos = new Vector2Int((int)(currentLocationOnTerrainmap.x * resolution), (int)(currentLocationOnTerrainmap.y * resolution));
        //         // EFT.UI.ConsoleScreen.Log($"JOB: Calculating score for detail#{d} at detail pos ({resolutionPos.x},{resolutionPos.y})" );
        //         for (int x = 0; x < 5; x++)
        //             for (int y = 0; y < 5; y++)
        //             {
        //                 var posX = resolutionPos.x - 2 + x;
        //                 var posY = resolutionPos.y - 2 + y;
        //                 int count = 0;

        //                 if (posX < 0 && terrain.leftNeighbor && posY >= 0 && posY < resolution)
        //                 {
        //                     Terrain neighbor = terrain.leftNeighbor;
        //                     if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
        //                         if (gatheringDetailMap == null)
        //                             gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
        //                         else if (neighborDetailMap.Count > d) // Async job
        //                             count = neighborDetailMap[d][resolution + posX, posY];
        //                 }
        //                 else if (posX >= resolution && terrain.rightNeighbor && posY >= 0 && posY < resolution)
        //                 {
        //                     Terrain neighbor = terrain.rightNeighbor;
        //                     if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
        //                         if (gatheringDetailMap == null)
        //                             gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
        //                         else if (neighborDetailMap.Count > d) // Async job
        //                             count = neighborDetailMap[d][posX - resolution, posY];
        //                 }
        //                 else if (posY >= resolution && terrain.topNeighbor && posX >= 0 && posX < resolution)
        //                 {
        //                     Terrain neighbor = terrain.topNeighbor;
        //                     if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
        //                         if (gatheringDetailMap == null)
        //                             gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
        //                         else if (neighborDetailMap.Count > d) // Async job
        //                             count = neighborDetailMap[d][posX, posY - resolution];
        //                 }
        //                 else if (posY < 0 && terrain.bottomNeighbor && posX >= 0 && posX < resolution)
        //                 {
        //                     Terrain neighbor = terrain.bottomNeighbor;
        //                     if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
        //                         if (gatheringDetailMap == null)
        //                             gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
        //                         else if (neighborDetailMap.Count > d) // Async job
        //                             count = neighborDetailMap[d][posX, posY + resolution];
        //                 }
        //                 else if (posY >= resolution && terrain.topNeighbor.rightNeighbor && posX >= resolution)
        //                 {
        //                     Terrain neighbor = terrain.topNeighbor.rightNeighbor;
        //                     if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
        //                         if (gatheringDetailMap == null)
        //                             gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
        //                         else if (neighborDetailMap.Count > d) // Async job
        //                             count = neighborDetailMap[d][posX - resolution, posY - resolution];
        //                 }
        //                 else if (posY >= resolution && terrain.topNeighbor.leftNeighbor && posX < 0)
        //                 {
        //                     Terrain neighbor = terrain.topNeighbor.leftNeighbor;
        //                     if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
        //                         if (gatheringDetailMap == null)
        //                             gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
        //                         else if (neighborDetailMap.Count > d) // Async job
        //                             count = neighborDetailMap[d][posX + resolution, posY - resolution];
        //                 }
        //                 else if (posY < 0 && terrain.bottomNeighbor.rightNeighbor && posX >= resolution)
        //                 {
        //                     Terrain neighbor = terrain.bottomNeighbor.rightNeighbor;
        //                     if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
        //                         if (gatheringDetailMap == null)
        //                             gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
        //                         else if (neighborDetailMap.Count > d) // Async job
        //                             count = neighborDetailMap[d][posX - resolution, posY + resolution];
        //                 }
        //                 else if (posY < 0 && terrain.bottomNeighbor.leftNeighbor && posX < 0)
        //                 {
        //                     Terrain neighbor = terrain.bottomNeighbor.leftNeighbor;
        //                     if (!terrainDetailMaps.TryGetValue(neighbor, out var neighborDetailMap))
        //                         if (gatheringDetailMap == null)
        //                             gatheringDetailMap = StartCoroutine(BuildAllTerrainDetailMapCoroutine(neighbor));
        //                         else if (neighborDetailMap.Count > d) // Async job
        //                             count = neighborDetailMap[d][posX + resolution, posY + resolution];
        //                 }
        //                 else if (detailMap.Count > d) // Async job
        //                 {
        //                     count = detailMap[d][posX, posY];
        //                 }

        //                 detailsHere5x5[GetDetailInfoIndex(x, y, d)] = new DetailInfo()
        //                 {
        //                     casted = true,
        //                     name = manager.prototypeList[d].name,
        //                     count = count,
        //                 };

        //                 if (x >= 1 && x <= 3 && y >= 1 && y <= 3) recentDetailCount3x3 += count;
        //             }
        //     }

        //     lastTerrainCheckPos = position;
        //     #region BENCHMARK
        //     _benchmarkSWTerrainCheck?.Stop();
        //     #endregion
        //     // scoreCache[16] = 0;
        //     // scoreCache[17] = 0;
        //     // foreach (var pos in IterateDetailIndex3x3)
        //     // {
        //     //     for (int i = 0; i < MAX_DETAIL_TYPES; i++)
        //     //     {
        //     //         var info = detailsHere5x5[pos*MAX_DETAIL_TYPES + i];
        //     //         GetDetailCoverScoreByName(info.name, info.count, out var s1, out var s2);
        //     //         scoreCache[16] += s1;
        //     //         scoreCache[17] += s2;
        //     //     }
        //     // }
        //     // CalculateDetailScore(Vector3.forward, 31, 0, out scoreCache[0], out scoreCache[1]);
        //     // CalculateDetailScore(Vector3.forward + Vector3.right, 31, 0, out scoreCache[2], out scoreCache[3]);
        //     // CalculateDetailScore(Vector3.right, 31, 0, out scoreCache[4], out scoreCache[5]);
        //     // CalculateDetailScore(Vector3.right + Vector3.back, 31, 0, out scoreCache[6], out scoreCache[7]);
        //     // CalculateDetailScore(Vector3.back, 31, 0, out scoreCache[8], out scoreCache[9]);
        //     // CalculateDetailScore(Vector3.back + Vector3.left, 31, 0, out scoreCache[10], out scoreCache[11]);
        //     // CalculateDetailScore(Vector3.left, 31, 0, out scoreCache[12], out scoreCache[13]);
        //     // CalculateDetailScore(Vector3.left + Vector3.forward, 31, 0, out scoreCache[14], out scoreCache[15]);

        // }

        // Coroutine gatheringDetailMap;
        // IEnumerator BuildAllTerrainDetailMapCoroutine(Terrain priority = null)
        // {
        //     yield return new WaitForSeconds(1); // Grass Cutter
        //     Logger.LogInfo($"[{ ActiveRaidSettings.LocationId }] Starting building terrain detail maps at { Time.time }...");
        //     bool allDisabled = true;
        //     var mgr = priority.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;
        //     if (mgr != null && mgr.enabled)
        //     {
        //         allDisabled = false;
        //         if (!terrainDetailMaps.ContainsKey(priority))
        //         {
        //             terrainDetailMaps[priority] = new List<int[,]>(mgr.prototypeList.Count);
        //             yield return BuildTerrainDetailMapCoroutine(priority, terrainDetailMaps[priority]);

        //         }
        //     }
        //     else terrainDetailMaps[priority] = null;
        //     foreach (Terrain terrain in Terrain.activeTerrains)
        //     {
        //         mgr = terrain.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;
        //         if (mgr != null && mgr.enabled)
        //         {
        //             allDisabled = false;
        //             if (!terrainDetailMaps.ContainsKey(terrain))
        //             {
        //                 terrainDetailMaps[terrain] = new List<int[,]>(mgr.prototypeList.Count);
        //                 yield return BuildTerrainDetailMapCoroutine(terrain, terrainDetailMaps[terrain]);

        //             }
        //         }
        //         else terrainDetailMaps[terrain] = null;
        //     }
        //     if (allDisabled) skipDetailCheck = true;
        //     Logger.LogInfo($"[{ ActiveRaidSettings.LocationId }] Finished building terrain detail maps at { Time.time }... (AllDisabled: {allDisabled})");
        // }
        // IEnumerator BuildTerrainDetailMapCoroutine(Terrain terrain, List<int[,]> detailMapData)
        // {
        //     var mgr = terrain.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;
        //     if (mgr == null || !mgr.isInitialized) yield break;
        //     float time = Time.time;
        //     Logger.LogInfo($"[{ActiveRaidSettings.LocationId }] Starting building detail map of {terrain.name} at {time}...");
        //     if (!terrainSpatialPartitions.TryGetValue(terrain, out var spData))
        //     {
        //         spData = terrainSpatialPartitions[terrain] = AccessTools.Field(typeof(GPUInstancerDetailManager), "spData").GetValue(mgr) as SpatialPartitionClass;
        //     }
        //     if (spData == null)
        //     {
        //         terrainSpatialPartitions.Remove(terrain);
        //     }
        //     var waitNextFrame = new WaitForEndOfFrame();

        //     if (detailMapData == null) detailMapData = new List<int[,]>(mgr.prototypeList.Count);
        //     else detailMapData.Clear();
        //     for (int layer = 0; layer < mgr.prototypeList.Count; ++layer)
        //     {
        //         var prototype = mgr.prototypeList[layer] as GPUInstancerDetailPrototype;
        //         if (prototype == null) detailMapData.Add(null);
        //         int[,] detailLayer = new int[prototype.detailResolution, prototype.detailResolution];
        //         detailMapData.Add(detailLayer);
        //         var resolutionPerCell = prototype.detailResolution / spData.cellRowAndCollumnCountPerTerrain;
        //         for (int terrainCellX = 0; terrainCellX < spData.cellRowAndCollumnCountPerTerrain; ++terrainCellX)
        //         {
        //             for (int terrainCellY = 0; terrainCellY < spData.cellRowAndCollumnCountPerTerrain; ++terrainCellY)
        //             {
        //                 BaseCellClass abstractCell;
        //                 if (spData.GetCell(BaseCellClass.CalculateHash(terrainCellX, 0, terrainCellY), out abstractCell))
        //                 {
        //                     CellClass cell = (CellClass)abstractCell;
        //                     if (cell.detailMapData != null)
        //                     {
        //                         for (int cellResX = 0; cellResX < resolutionPerCell; ++cellResX)
        //                         {
        //                             for (int cellResY = 0; cellResY < resolutionPerCell; ++cellResY)
        //                                 detailLayer[cellResX + terrainCellX * resolutionPerCell, cellResY + terrainCellY * resolutionPerCell] = cell.detailMapData[layer][cellResX + cellResY * resolutionPerCell];
        //                         }
        //                     }
        //                 }

        //                 yield return waitNextFrame;
        //             }
        //         }
        //     }
        //     Logger.LogInfo($"[{ ActiveRaidSettings.LocationId }] Finished building detail map of {terrain.name} at { Time.time }... Costed { Time.time - time }");
        // }

        // Layout: [block of all detail types at the pos] * maxX * maxY 
        // int GetDetailInfoIndex(int x5x5, int y5x5, int detailId) => (y5x5 * 5 + x5x5) * MAX_DETAIL_TYPES + detailId;

    }
}