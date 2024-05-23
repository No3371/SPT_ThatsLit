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
    public class ThatsLitPlayer : MonoBehaviour
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
        float terrainScoreHintProne, terrainScoreHintRegular;
        internal PlayerFoliageProfile Foliage;
        internal PlayerLitScoreProfile PlayerLitScoreProfile { get; set;}
        internal Vector3 lastShotVector;
        internal float lastShotTime;
        static readonly LayerMask ambienceRaycastMask = (1 << LayerMask.NameToLayer("Terrain")) | (1 << LayerMask.NameToLayer("HighPolyCollider")) | (1 << LayerMask.NameToLayer("Grass")) | (1 << LayerMask.NameToLayer("Foliage"));
        internal delegate bool CheckStimEffectProxy (EFT.HealthSystem.EStimulatorBuffType buffType);
        internal CheckStimEffectProxy CheckEffectDelegate
        {
            get
            {
                if (checkEffectDelegate == null)
                {
                    var methodInfo = ReflectionHelper.FindMethodByArgTypes(typeof(EFT.HealthSystem.ActiveHealthController), new Type[] { typeof(EFT.HealthSystem.EStimulatorBuffType) }, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    checkEffectDelegate = (CheckStimEffectProxy) methodInfo.CreateDelegate(typeof(CheckStimEffectProxy), Player.ActiveHealthController);
                }
                return checkEffectDelegate;
            }
        }
        private CheckStimEffectProxy checkEffectDelegate;
        public static bool CanLoad ()
        {
            bool result = false;
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
                    ThatsLitPlugin.DebugTexture.SettingChanged += HandleDebugTextureSettingChanged;
                }
                else display.enabled = true;
            }
        }

        void DisableBrightness ()
        {
            if (cam) cam.enabled = false;
            if (display) display.enabled = false;
            PlayerLitScoreProfile = null;
            ThatsLitPlugin.DebugTexture.SettingChanged -= HandleDebugTextureSettingChanged;
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
            ThatsLitPlugin.swUpdate.MaybeResume();
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

            ThatsLitPlugin.swTerrain.MaybeResume();
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
                    var pf = (Player.PoseLevel / Player.AIData.Player.Physical.MaxPoseLevel) * 0.6f + 0.4f;
                    terrainScoreHintRegular = score.regular / (1f + 0.35f * Mathf.InverseLerp(0.45f, 1f, pf));
                }
            }
            ThatsLitPlugin.swTerrain.Stop();

            ThatsLitPlugin.swFoliage.MaybeResume();
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
            ThatsLitPlugin.swFoliage.Stop();

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
                    ThatsLitPlugin.swScoreCalc.MaybeResume();
                        Singleton<ThatsLitGameworld>.Instance.ScoreCalculator?.PreCalculate(PlayerLitScoreProfile, observed, Utility.GetInGameDayTime());
                    ThatsLitPlugin.swScoreCalc.Stop();
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

            if (ThatsLitPlugin.DebugTexture.Value && Time.frameCount % 61 == 0 && display?.enabled == true)
                Graphics.CopyTexture(rt, slowRT);

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
                var state = LightAndLaserState;
                Utility.DetermineShiningEquipments(Player, out state.deviceStateCache, out state.deviceStateCacheSub);
                state.VisibleLight = state.deviceStateCache.light > 0;
                state.VisibleLaser = state.deviceStateCache.laser > 0;
                state.IRLight = state.deviceStateCache.irLight > 0;
                state.IRLaser = state.deviceStateCache.irLaser > 0;
                state.VisibleLightSub = state.deviceStateCacheSub.light > 0;
                state.VisibleLaserSub = state.deviceStateCacheSub.laser > 0;
                state.IRLightSub = state.deviceStateCacheSub.irLight > 0;
                state.IRLaserSub = state.deviceStateCacheSub.irLaser > 0;
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
                currentRating += Time.timeScale * (Mathf.InverseLerp(10f, 1f, haxHit.distance) - 0.01f);
            }
            else
            {
                currentRating -= Time.timeScale * 1.75f;
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
            ThatsLitPlugin.swScoreCalc.MaybeResume();
                Singleton<ThatsLitGameworld>.Instance.ScoreCalculator?.CalculateMultiFrameScore(observed, cloud, fog, rain, Singleton<ThatsLitGameworld>.Instance, PlayerLitScoreProfile, Utility.GetInGameDayTime(), ActiveRaidSettings.LocationId);
            ThatsLitPlugin.swScoreCalc.Stop();
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
            DisableBrightness();
            if (display) GameObject.Destroy(display);
            if (cam) GameObject.Destroy(cam);
            if (rt) rt.Release();
        }
        float litFactorSample, ambScoreSample;
        static float benchmarkSampleSeenCoef, benchmarkSampleEncountering, benchmarkSampleExtraVisDis, benchmarkSampleScoreCalculator, benchmarkSampleUpdate, benchmarkSampleFoliageCheck, benchmarkSampleTerrainCheck, benchmarkSampleGUI;
        int guiFrame;
        string infoCache1, infoCache2, infoCacheBenchmark;

        void OnGUIInfo ()
        {
            if (PlayerLitScoreProfile != null) Utility.GUILayoutDrawAsymetricMeter((int)(PlayerLitScoreProfile.frame0.multiFrameLitScore / 0.0999f));
            if (PlayerLitScoreProfile != null) Utility.GUILayoutDrawAsymetricMeter((int)(Mathf.Pow(PlayerLitScoreProfile.frame0.multiFrameLitScore, POWER) / 0.0999f));
            if (ThatsLitPlugin.EquipmentInfo.Value && PlayerLitScoreProfile != null && LightAndLaserState.storage != 0) GUILayout.Label(LightAndLaserState.Format());
            if (Foliage != null && Foliage.FoliageScore > 0 && ThatsLitPlugin.FoliageInfo.Value)
                Utility.GUILayoutFoliageMeter((int)(Foliage.FoliageScore / 0.0999f));
            if (TerrainDetails != null && terrainScoreHintProne > 0.0998f && ThatsLitPlugin.TerrainInfo.Value)
                if (Player.IsInPronePose) Utility.GUILayoutTerrainMeter((int)(terrainScoreHintProne / 0.0999f));
                else Utility.GUILayoutTerrainMeter((int)(terrainScoreHintRegular / 0.0999f));

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
            ThatsLitPlugin.swGUI.MaybeResume();
            if (PlayerLitScoreProfile == null)
            {
                if (Time.time - awakeAt < 30f && !ThatsLitPlugin.HideMapTip.Value)
                    GUILayout.Label("  [That's Lit] Brightness module is disabled in configs or not supported on this map.");
            }

            var poseFactor = Player.PoseLevel / Player.Physical.MaxPoseLevel * 0.6f + 0.4f; // crouch: 0.4f
            if (Player.IsInPronePose) poseFactor -= 0.4f; // prone: 0
            poseFactor += 0.05f; // base -> prone -> 0.05f, crouch -> 0.45f

            if (ThatsLitPlugin.DebugInfo.Value || ThatsLitPlugin.ScoreInfo.Value)
            {
                OnGUIInfo();

                if (Time.time < awakeAt + 10)
                    GUILayout.Label("  [That's Lit] The HUD can be configured in plugin settings.");
            }

            if (!ThatsLitPlugin.DebugInfo.Value || DebugInfo == null) return;

            Singleton<ThatsLitGameworld>.Instance.ScoreCalculator?.OnGUI(layoutCall);

            float fog = WeatherController.Instance?.WeatherCurve?.Fog ?? 0;
            float rain = WeatherController.Instance?.WeatherCurve?.Rain ?? 0;
            float cloud = WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0;
            
            if (layoutCall)
                infoCache1 = $"  IMPACT: {DebugInfo.lastCalcFrom:0.000} -> {DebugInfo.lastCalcTo:0.000} ({DebugInfo.lastFactor2:0.000} <- {DebugInfo.lastFactor1:0.000} <- {DebugInfo.lastScore:0.000}) AMB: {ambScoreSample:0.00} LIT: {litFactorSample:0.00} (SAMPLE)\n  AFFECTED: {DebugInfo.calced} (+{DebugInfo.calcedLastFrame}) / ENCOUNTER: {DebugInfo.encounter} VAGUE HINT: { DebugInfo.vagueHint }\n  TERRAIN: { terrainScoreHintProne :0.000}/{ terrainScoreHintRegular :0.000} 3x3:( { TerrainDetails?.RecentDetailCount3x3 } ) (score-{ PlayerLitScoreProfile?.detailBonusSmooth:0.00})  FOLIAGE: {Foliage?.FoliageScore:0.000} ({Foliage?.FoliageCount}) (H{Foliage?.Nearest?.dis:0.00} to {Foliage?.Nearest?.name})\n  FOG: {fog:0.000} / RAIN: {rain:0.000} / CLOUD: {cloud:0.000} / TIME: {Utility.GetInGameDayTime():0.000} / WINTER: {Singleton<ThatsLitGameworld>.Instance.IsWinter}\n  POSE: {poseFactor} SPEED: { Player.Velocity.magnitude :0.000}  INSIDE: { Time.time - lastOutside:0.000}  AMB: { ambienceShadownRating:0.000}  OVH: { overheadHaxRating:0.000}  BNKR: { bunkerTimeClamped:0.000}";
            GUILayout.Label(infoCache1);
            // GUILayout.Label(string.Format(" FOG: {0:0.000} / RAIN: {1:0.000} / CLOUD: {2:0.000} / TIME: {3:0.000} / WINTER: {4}", WeatherController.Instance?.WeatherCurve?.Fog ?? 0, WeatherController.Instance?.WeatherCurve?.Rain ?? 0, WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0, GetInGameDayTime(), isWinterCache));
            
            OnGUIScoreCalc();

            if (IsDebugSampleFrame)
            {
                litFactorSample = PlayerLitScoreProfile?.litScoreFactor ?? 0;
                ambScoreSample = PlayerLitScoreProfile?.frame0.ambienceScore ?? 0;
                
            }
            if (Time.frameCount % DEBUG_INTERVAL == 1 && ThatsLitPlugin.EnableBenchmark.Value && layoutCall) // The trap here is OnGUI is called multiple times per frame, make sure to reset the stopwatches only once
            {
                ConcludeBenchmarks();
            }

            if (ThatsLitPlugin.EnableBenchmark.Value)
            {
                if (layoutCall)
                    infoCacheBenchmark = $"  Update:         {benchmarkSampleUpdate,8:0.0000}\n  Foliage:        {benchmarkSampleFoliageCheck,8:0.0000}\n  Terrain:        {benchmarkSampleTerrainCheck,8:0.0000}\n  SeenCoef:       {benchmarkSampleSeenCoef,8:0.0000}\n  Encountering:   {benchmarkSampleEncountering,8:0.0000}\n  ExtraVisDis:    {benchmarkSampleExtraVisDis,8:0.0000}\n  ScoreCalculator:{benchmarkSampleScoreCalculator,8:0.0000}\n  Info(+Debug):    {benchmarkSampleGUI,8:0.0000} ms";
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

            ThatsLitPlugin.swGUI.Stop();
            guiFrame = Time.frameCount;
            
        }
        string infoCache;
        internal virtual void OnGUIScoreCalc ()
        {
            if (PlayerLitScoreProfile == null || DebugInfo == null) return;
            if (ThatsLitPlayer.IsDebugSampleFrame)
            {
                DebugInfo.shinePixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioShinePixels + PlayerLitScoreProfile.frame1.RatioShinePixels + PlayerLitScoreProfile.frame2.RatioShinePixels + PlayerLitScoreProfile.frame3.RatioShinePixels + PlayerLitScoreProfile.frame4.RatioShinePixels + PlayerLitScoreProfile.frame5.RatioShinePixels) / 6f;
                DebugInfo.highLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioHighPixels + PlayerLitScoreProfile.frame1.RatioHighPixels + PlayerLitScoreProfile.frame2.RatioHighPixels + PlayerLitScoreProfile.frame3.RatioHighPixels + PlayerLitScoreProfile.frame4.RatioHighPixels + PlayerLitScoreProfile.frame5.RatioHighPixels) / 6f;
                DebugInfo.highMidLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioHighMidPixels + PlayerLitScoreProfile.frame1.RatioHighMidPixels + PlayerLitScoreProfile.frame2.RatioHighMidPixels + PlayerLitScoreProfile.frame3.RatioHighMidPixels + PlayerLitScoreProfile.frame4.RatioHighMidPixels + PlayerLitScoreProfile.frame5.RatioHighMidPixels) / 6f;
                DebugInfo.midLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioMidPixels + PlayerLitScoreProfile.frame1.RatioMidPixels + PlayerLitScoreProfile.frame2.RatioMidPixels + PlayerLitScoreProfile.frame3.RatioMidPixels + PlayerLitScoreProfile.frame4.RatioMidPixels + PlayerLitScoreProfile.frame5.RatioMidPixels) / 6f;
                DebugInfo.midLowLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioMidLowPixels + PlayerLitScoreProfile.frame1.RatioMidLowPixels + PlayerLitScoreProfile.frame2.RatioMidLowPixels + PlayerLitScoreProfile.frame3.RatioMidLowPixels + PlayerLitScoreProfile.frame4.RatioMidLowPixels + PlayerLitScoreProfile.frame5.RatioMidLowPixels) / 6f;
                DebugInfo.lowLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioLowPixels + PlayerLitScoreProfile.frame1.RatioLowPixels + PlayerLitScoreProfile.frame2.RatioLowPixels + PlayerLitScoreProfile.frame3.RatioLowPixels + PlayerLitScoreProfile.frame4.RatioLowPixels + PlayerLitScoreProfile.frame5.RatioLowPixels) / 6f;
                DebugInfo.darkPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioDarkPixels + PlayerLitScoreProfile.frame1.RatioDarkPixels + PlayerLitScoreProfile.frame2.RatioDarkPixels + PlayerLitScoreProfile.frame3.RatioDarkPixels + PlayerLitScoreProfile.frame4.RatioDarkPixels + PlayerLitScoreProfile.frame5.RatioDarkPixels) / 6f;
            }
            if (guiFrame < Time.frameCount) infoCache = $"  PIXELS: {DebugInfo.shinePixelsRatioSample * 100:000}% - {DebugInfo.highLightPixelsRatioSample * 100:000}% - {DebugInfo.highMidLightPixelsRatioSample * 100:000}% - { DebugInfo.midLightPixelsRatioSample * 100:000}% - {DebugInfo.midLowLightPixelsRatioSample * 100:000}% - {DebugInfo.lowLightPixelsRatioSample * 100:000}% | {DebugInfo.darkPixelsRatioSample * 100:000}% (AVG Sample)\n  AvgLumMF: {PlayerLitScoreProfile.frame0.avgLumMultiFrames:0.000} / {Singleton<ThatsLitGameworld>.Instance.ScoreCalculator.GetMinAmbianceLum():0.000} ~ {Singleton<ThatsLitGameworld>.Instance?.ScoreCalculator?.GetMaxAmbianceLum():0.000} ({Singleton<ThatsLitGameworld>.Instance?.ScoreCalculator?.GetAmbianceLumRange():0.000})\n   Sun: {Singleton<ThatsLitGameworld>.Instance?.ScoreCalculator?.sunLightScore:0.000}/{Singleton<ThatsLitGameworld>.Instance?.ScoreCalculator?.GetMaxSunlightScore():0.000}, Moon: {Singleton<ThatsLitGameworld>.Instance?.ScoreCalculator?.moonLightScore:0.000}/{Singleton<ThatsLitGameworld>.Instance?.ScoreCalculator?.GetMaxMoonlightScore():0.000}\n  SCORE : {DebugInfo.scoreRawBase:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw0:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw1:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw2:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw3:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw4:＋0.00;－0.00;+0.00} (SAMPLE)";            
            GUILayout.Label(infoCache);

            Utility.GUILayoutDrawAsymetricMeter((int)(PlayerLitScoreProfile.frame0.score / 0.0999f));
        }

        private void ConcludeBenchmarks()
        {
            benchmarkSampleSeenCoef         = ThatsLitPlugin.swSeenCoef.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleEncountering     = ThatsLitPlugin.swEncountering.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleExtraVisDis      = ThatsLitPlugin.swExtraVisDis.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleScoreCalculator  = ThatsLitPlugin.swScoreCalc.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleUpdate           = ThatsLitPlugin.swUpdate.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleGUI              = ThatsLitPlugin.swGUI.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleFoliageCheck     = ThatsLitPlugin.swFoliage.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleTerrainCheck     = ThatsLitPlugin.swTerrain.ConcludeMs() / (float) DEBUG_INTERVAL;
        }
    }
}