using System;
using System.Collections;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.EnvironmentEffect;
using EFT.UI;
using EFT.Weather;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ThatsLit
{
    // TODO:
    // # Experiment: Full body lit check
    // Assign player thorax to layer N and set ShadowCastingMode on
    // un-cull N for all Lights
    // Cull N for FPS cam
    // Un-Cull N for TL cam
    // ! Could breaks Vision check?
    public class ThatsLitPlayer : MonoBehaviour
    {
        public const int DEBUG_INTERVAL = 19;
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
        float setupTime;
        float lastCheckedLights;
        public RaidSettings ActiveRaidSettings => gameworld.activeRaidSettings;
        public Player Player { get; internal set; }
        public float fog, rain, cloud;
        AsyncGPUReadbackRequest gquReq;
        internal float lastOutside;
        internal float surroundingRating;
        /// <summary>
        /// 0~10
        /// </summary>
        internal float ambienceShadownRating;
        /// <summary>
        /// Used in ScoreCalculator
        /// </summary>
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
        internal static readonly LayerMask ambienceRaycastMask = (1 << LayerMask.NameToLayer("Terrain")) | (1 << LayerMask.NameToLayer("HighPolyCollider")) | (1 << LayerMask.NameToLayer("Grass")) | (1 << LayerMask.NameToLayer("Foliage"));
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
        static float canLoadTime = 0;
        internal RaycastHit flashLightHit;
        internal BotOwner lastNearest;
        ThatsLitGameworld gameworld;
        static Camera prefab;
        int cameraThrottleFrequency = 1440;
        public static bool CanLoad ()
        {
            if (CameraClass.Instance.OpticCameraManager.Camera != null
             && prefab == null)
            {
                CameraClass.Instance.OpticCameraManager.Camera.gameObject.SetActive(false);
                prefab = GameObject.Instantiate<Camera>(CameraClass.Instance.OpticCameraManager.Camera);
                CameraClass.Instance.OpticCameraManager.Camera.gameObject.SetActive(true);
                prefab.gameObject.name = "That's Lit Camera (Prefab)";
                foreach (var c in prefab.gameObject.GetComponents<MonoBehaviour>())
                switch (c) {
                    case VolumetricLightRenderer volumetricLightRenderer:
                        if (ThatsLitPlugin.VolumetricLightRenderer.Value) volumetricLightRenderer.IsOptic = false;
                        break;
                    case AreaLightManager areaLightManager:
                        MonoBehaviour.Destroy(c);
                        break;
                    default:
                        MonoBehaviour.Destroy(c);
                        break;
                }
                prefab.clearFlags = CameraClearFlags.SolidColor;
                prefab.backgroundColor = new Color (0, 0, 0, 0);

                prefab.nearClipPlane = 0.001f;
                prefab.farClipPlane = 3.5f;

                prefab.cullingMask = LayerMaskClass.PlayerMask;
                prefab.fieldOfView = 44;


                canLoadTime = Time.realtimeSinceStartup;
                Logger.LogWarning($"[That's Lit] Can load players. Time: {canLoadTime}");
                return false; // wait for next frame
            }

            return prefab != null && canLoadTime + 10 < Time.realtimeSinceStartup;
        }

        internal void Setup (ThatsLitGameworld gameworld)
        {
            setupTime = Time.time;
            if (Player.IsYourPlayer)
                DebugInfo = new PlayerDebugInfo();

            this.gameworld = gameworld;
            MaybeEnableBrightness();
        }

        internal void MaybeEnableBrightness ()
        {
            if (!ThatsLitPlugin.EnabledLighting.Value
             || gameworld.ScoreCalculator == null // Disabled on the map?
             || Application.isBatchMode)
                return;

            if (PlayerLitScoreProfile == null) PlayerLitScoreProfile = new PlayerLitScoreProfile(this);
            if (PlayerLitScoreProfile.IsProxy) return;

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


            if (Player.IsYourPlayer)
                ThatsLitPlugin.DebugTexture.SettingChanged += HandleDebugTextureSettingChanged;
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

        internal void DisableBrightness ()
        {
            if (cam) cam.enabled = false;
            if (display) display.enabled = false;
            PlayerLitScoreProfile = null;
            ThatsLitPlugin.DebugTexture.SettingChanged -= HandleDebugTextureSettingChanged;
        }
        internal void ToggleBrightnessProxy (bool toggle)
        {
            if (cam) cam.enabled = !toggle;
            if (display) display.enabled = !toggle;
            PlayerLitScoreProfile.IsProxy = toggle;
            if (toggle)
            {
                PlayerLitScoreProfile.frame0 = default;
            }
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
                DisableBrightness();
                return;
            }

            #region BENCHMARK
            ThatsLitPlugin.swUpdate.MaybeResume();
            #endregion


            Vector3 bodyPos = Player.MainParts[BodyPartType.body].Position;

            if (!Player.AIData.IsInside)
                lastOutside = Time.time;

            if (EnvironmentManager.Instance.InBunker && lastOutBunkerTime >= lastInBunkerTime)
            {
                lastInBunkerTime = Time.time;
                lastInBunderPos = bodyPos;
            }

            if (!EnvironmentManager.Instance.InBunker && lastOutBunkerTime < lastInBunkerTime)
            {
                lastOutBunkerTime = Time.time;
            }

            if (lastOutBunkerTime < lastInBunkerTime && bodyPos.SqrDistance(lastInBunderPos) > 2.25f)
                 bunkerTimeClamped += Time.deltaTime;
            else bunkerTimeClamped -= Time.deltaTime * 5;

            bunkerTimeClamped = Mathf.Clamp(bunkerTimeClamped, 0, 10);

            ThatsLitPlugin.swTerrain.MaybeResume();
            if (ThatsLitPlugin.EnabledGrasses.Value
                && !gameworld.terrainDetailsUnavailable
                && TerrainDetails == null
                && !Application.isBatchMode)
            {
                TerrainDetails = new PlayerTerrainDetailsProfile();
            }

            if (TerrainDetails != null
            && Time.time > TerrainDetails.LastCheckedTime + 0.41f)
            {
                gameworld.CheckTerrainDetails(bodyPos, TerrainDetails);
                ThatsLitAPI.OnPlayerSurroundingTerrainSampledDirect?.Invoke(this);
                ThatsLitAPI.OnPlayerSurroundingTerrainSampled?.Invoke(this.Player);
                if (ThatsLitPlugin.TerrainInfo.Value)
                {
                    var score = gameworld.CalculateDetailScore(TerrainDetails, Vector3.zero, 0, 0);
                    terrainScoreHintProne = score.prone;
                    var pf = Utility.GetPoseFactor(Player.PoseLevel, Player.Physical.MaxPoseLevel, Player.IsInPronePose);
                    terrainScoreHintRegular = Utility.GetPoseWeightedRegularTerrainScore(pf, score);
                }
            }
            ThatsLitPlugin.swTerrain.Stop();

            ThatsLitPlugin.swFoliage.MaybeResume();
            if (ThatsLitPlugin.EnabledFoliage.Value
             && !gameworld.foliageUnavailable
             && Foliage == null
             && !Application.isBatchMode)
            {
                Foliage = new PlayerFoliageProfile(new FoliageInfo[16], new Collider[16]);
            }
            if (Foliage != null)
            {
                if (!Foliage.IsFoliageSorted)
                    Foliage.IsFoliageSorted = SlicedBubbleSort(Foliage.Foliage, Foliage.FoliageCount * 2, Foliage.FoliageCount);
                gameworld.UpdateFoliageScore(bodyPos, Foliage);
            }
            ThatsLitPlugin.swFoliage.Stop();

            if (ThatsLitPlugin.EnableEquipmentCheck.Value
             && Time.time > lastCheckedLights + 0.41f
             && PlayerLitScoreProfile != null)
            {
                lastCheckedLights = Time.time;
                var state = LightAndLaserState;
                (state.deviceStateCache, state.deviceStateCacheSub) = Utility.DetermineShiningEquipments(Player);
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

            CastFlashlight();

            overheadHaxRating = UpdateOverheadHaxCastRating(bodyPos, overheadHaxRating);

            if (PlayerLitScoreProfile == null && ThatsLitPlugin.EnabledLighting.Value)
            {
                MaybeEnableBrightness();
                ThatsLitPlugin.swUpdate.Stop();
                return;
            }
            else if (PlayerLitScoreProfile != null && !PlayerLitScoreProfile.IsProxy && !ThatsLitPlugin.EnabledLighting.Value)
            {
                DisableBrightness();
                ThatsLitPlugin.swUpdate.Stop();
                return;
            }

            if (PlayerLitScoreProfile == null || PlayerLitScoreProfile.IsProxy || gameworld?.ScoreCalculator == null)
            {
                ThatsLitPlugin.swUpdate.Stop();
                return;
            }

            // ! Consdier this the box of all Brightness required statements
            // Remember that proxies also have a PlayerLitScoreProfile

            if (ThatsLitPlugin.EnabledLighting.Value && !cam.enabled && (ThatsLitPlugin.EnabledCameraThrottling.Value == false || Time.frameCount % cameraThrottleFrequency == 0))
            {
                cam.enabled = true;
                cameraThrottleFrequency = (int) (1f/Time.deltaTime);
                cameraThrottleFrequency = Mathf.Max(cameraThrottleFrequency, 1);
                cameraThrottleFrequency = (int) Mathf.Lerp(cameraThrottleFrequency, 1, Mathf.InverseLerp(0.35f, -0.1f, PlayerLitScoreProfile.frame0.ambienceScore));
            }
            else if (ThatsLitPlugin.EnabledCameraThrottling.Value && (cameraThrottleFrequency == 1 || Time.frameCount % cameraThrottleFrequency != 0))
            {
                if (cam.enabled)
                {
                    cameraThrottleFrequency = (int) (1f/Time.deltaTime);
                    cameraThrottleFrequency = Mathf.Max(cameraThrottleFrequency, 1);
                    cameraThrottleFrequency = (int) Mathf.Lerp(cameraThrottleFrequency, 1, Mathf.InverseLerp(0.35f, -0.1f, PlayerLitScoreProfile.frame0.ambienceScore));
                    if (cameraThrottleFrequency > 4)
                        cam.enabled = false;
                }
            }

            if (gquReq.done && rt != null)
            {
                if (cam?.enabled == false)
                {
                    observed.Dispose();
                }
                else
                {
                    gquReq = AsyncGPUReadback.Request(rt, 0, req =>
                    {
                        observed.Dispose();
                        if (!req.hasError)
                        {
                            ThatsLitPlugin.swScoreCalc.MaybeResume();
                            observed = req.GetData<Color32>();
                                gameworld?.ScoreCalculator?.PreCalculate(PlayerLitScoreProfile, observed, Utility.GetInGameDayTime());
                            ThatsLitPlugin.swScoreCalc.Stop();
                        }
                    });
                }
            }

            if (cam?.enabled == true)
            {
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

                if (ThatsLitPlugin.DebugTexture.Value && Time.frameCount % 61 == 0 && display?.enabled == true && rt != null)
                    Graphics.CopyTexture(rt, slowRT);
            }
            
            // Ambient shadow
            // Not required for proxies
            Vector3 headPos = Player.MainParts[BodyPartType.head].Position;
            Vector3 lhPos = Player.MainParts[BodyPartType.leftArm].Position;
            Vector3 rhPos = Player.MainParts[BodyPartType.rightArm].Position;
            Vector3 lPos = Player.MainParts[BodyPartType.leftLeg].Position;
            Vector3 rPos = Player.MainParts[BodyPartType.rightLeg].Position;
            if (TOD_Sky.Instance != null)
            {
                Ray ray = default;
                float? sunlightFactor = gameworld.ScoreCalculator?.CalculateSunLightTimeFactor(ActiveRaidSettings.LocationId, Utility.GetInGameDayTime());
                if (sunlightFactor == null)
                    sunlightFactor = TOD_Sky.Instance.IsDay ? 1f : 0f;
                Vector3 ambienceDir = sunlightFactor > 0.05f ? TOD_Sky.Instance.LocalSunDirection : TOD_Sky.Instance.LightDirection;

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
                else ambienceShadownRating -= 25f * Time.deltaTime;
                ambienceShadownRating = Mathf.Clamp(ambienceShadownRating, 0, 10f);
            }

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

            ThatsLitPlugin.swUpdate.Stop();
        }

        void FixedUpdate ()
        {
            ThatsLitPlugin.swFUpdate.MaybeResume();
            if (ThatsLitPlugin.BotLookDirectionTweaks.Value == false
             || Player?.HealthController?.IsAlive == false
             || gameworld == null
             || !ThatsLitPlugin.EnabledMod.Value)
            {
                ThatsLitPlugin.swFUpdate.Stop();
                return;
            }

            Player nearestBotPlayer = lastNearest?.GetPlayer;
            if (ThatsLitPlugin.EnableNearestBotSteering.Value
             && nearestBotPlayer != null
             && nearestBotPlayer.isActiveAndEnabled
             && nearestBotPlayer.MainParts != null
             && nearestBotPlayer.MainParts.ContainsKey(BodyPartType.head)
             && lastNearest.Steering != null
             && lastNearest.Mover != null)
            {
                var nearestBotGoalEnemy = lastNearest.Memory?.GoalEnemy;

                ThatsLitGameworld.SingleIdThrottler throttler;
                gameworld.singleIdThrottlers.TryGetValue(lastNearest.ProfileId, out throttler);

                if (nearestBotGoalEnemy?.Person == Player
                 && nearestBotGoalEnemy?.HaveSeen == true
                 && Time.time - throttler.lastForceLook > 1f
                 && lastNearest.Mover.IsMoving == true && nearestBotPlayer.IsSprintEnabled == false
                //  && lastNearest.Steering.SteeringMode == EBotSteering.ToMovingDirection
                 && Vector3.Distance(Player.Position, lastNearest.Position) < 5f
                 && UnityEngine.Random.Range(0f, 1f) < 0.6f * Mathf.InverseLerp(15f, 5f, Time.time - nearestBotGoalEnemy.TimeLastSeenReal) + 0.5f * Mathf.InverseLerp(7.5f, 1f, Vector3.Distance(Player.Position, nearestBotGoalEnemy.EnemyLastPositionReal)))
                {
                    lastNearest.Steering.LookToPoint(Player.Position);
                    if (DebugInfo != null)
                        DebugInfo.forceLooks++;
                    
                    throttler.lastForceLook = Time.time;
                    gameworld.singleIdThrottlers[lastNearest.ProfileId] = throttler;
                    ThatsLitPlugin.swFUpdate.Stop();
                    return;
                }

                if (nearestBotGoalEnemy?.Person == Player
                 && nearestBotGoalEnemy?.HaveSeen == true
                 && Time.time - nearestBotGoalEnemy.TimeLastSeen < 10f
                 && lastNearest.Mover.IsMoving == true && !nearestBotPlayer.IsSprintEnabled
                 && Time.time - lastOutside > 1f
                 && Time.time - throttler.lastForceLook > 1f
                 && UnityEngine.Random.Range(0f, 1f) < 0.05f * Mathf.InverseLerp(5f, 1f, nearestBotGoalEnemy.Distance))
                {
                    lastNearest.Steering?.LookToPoint(Player.Position);
                    if (DebugInfo != null)
                        DebugInfo.forceLooks++;

                    throttler.lastForceLook = Time.time;
                    gameworld.singleIdThrottlers[lastNearest.ProfileId] = throttler;
                    ThatsLitPlugin.swFUpdate.Stop();
                    return;
                }

                if (!nearestBotPlayer.IsSprintEnabled
                //  && lastNearest.Steering.SteeringMode == EBotSteering.ToMovingDirection
                 && !(nearestBotGoalEnemy != null && Time.time - nearestBotGoalEnemy.TimeLastSeenReal < 10f)
                 && Time.time - throttler.lastSideLook > 10f)
                {
                    lastNearest.StartCoroutine(MakeBotPeekSide(lastNearest));
                    if (DebugInfo != null)
                        DebugInfo.sideLooks++;
                    
                    throttler.lastSideLook = Time.time;
                    gameworld.singleIdThrottlers[lastNearest.ProfileId] = throttler;
                    ThatsLitPlugin.swFUpdate.Stop();
                    return;
                }

                Vector3 botHeadPos = nearestBotPlayer.MainParts[BodyPartType.head].Position;
                if (flashLightHit.collider != null
                 && Time.time - throttler.lastForceLook > 1f
                 && botHeadPos != null
                 && (nearestBotGoalEnemy?.Person == Player || nearestBotGoalEnemy == null)
                //  && lastNearest.Steering.SteeringMode == EBotSteering.ToMovingDirection
                 && Vector3.Angle(lastNearest.LookDirection, flashLightHit.normal) > 90f
                 && Vector3.Angle(lastNearest.LookDirection, flashLightHit.point - botHeadPos) < 90f
                 && Vector3.Angle(lastNearest.LookDirection, Player.Position - botHeadPos) > 30f
                 && UnityEngine.Random.Range(0f, 1f) < 0.05f * Mathf.InverseLerp(20f, 2f, Vector3.Distance(lastNearest.Position, Player.Position)))
                {
                    lastNearest.Steering?.LookToPoint(Player.Position);
                    if (DebugInfo != null)
                        DebugInfo.forceLooks++;
                    
                    throttler.lastForceLook = Time.time;
                    gameworld.singleIdThrottlers[lastNearest.ProfileId] = throttler;
                    ThatsLitPlugin.swFUpdate.Stop();
                    return;
                }
            }
            ThatsLitPlugin.swFUpdate.Stop();
        }


        IEnumerator MakeBotPeekSide (BotOwner bot)
        {
            Vector3 lookDirection = bot.LookDirection;
            Vector3 dir = lookDirection.RotateAroundPivot(Vector3.up, Quaternion.Euler(0f, UnityEngine.Random.Range(-180f, 180f), 0f));
            bot.Steering?.LookToDirection(dir);
            yield return new WaitForSeconds(UnityEngine.Random.Range(1, 4f));
            bot.Steering?.LookToMovingDirection();
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

        private float UpdateSurroundingCastRating(Vector3 bodyPos, float currentRating)
        {
            if (SurroundingCast(bodyPos, out var hit))
            {
                currentRating += Time.timeScale * (Mathf.InverseLerp(5f, 1f, hit.distance) - 0.01f);
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
            gameworld.GetWeatherStats(out fog, out rain, out cloud);
            if (PlayerLitScoreProfile == null) return;

            //if (debugTex != null && Time.frameCount % 61 == 0) Graphics.CopyTexture(tex, debugTex);
            // if (envDebugTex != null && Time.frameCount % 61 == 0) Graphics.CopyTexture(envTex, envDebugTex);

            ThatsLitPlugin.swScoreCalc.MaybeResume();
                gameworld.ScoreCalculator?.CalculateMultiFrameScore(cloud, fog, rain, gameworld, PlayerLitScoreProfile, Utility.GetInGameDayTime(), ActiveRaidSettings.LocationId);
            ThatsLitPlugin.swScoreCalc.Stop();

            if (!PlayerLitScoreProfile.IsProxy)
            {
                ThatsLitAPI.OnPlayerBrightnessScoreCalculatedDirect?.Invoke(this, PlayerLitScoreProfile.frame0.multiFrameLitScore, PlayerLitScoreProfile.frame0.ambienceScore);
                ThatsLitAPI.OnPlayerBrightnessScoreCalculated?.Invoke(Player, PlayerLitScoreProfile.frame0.multiFrameLitScore, PlayerLitScoreProfile.frame0.ambienceScore);
            }


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

        private bool SurroundingCast (Vector3 from, out RaycastHit hit)
        {
            Vector3 cast = new Vector3(1, 0, 0);
            int slice = Time.frameCount % 60;
            cast = Quaternion.Euler(0, slice * 6, 0) * cast;
            
            var ray = new Ray(from, cast);
            return Physics.Raycast(ray, out hit, 5, ambienceRaycastMask);
        }

        /// <summary>
        /// Detect where the active flashlight is hitting.
        /// </summary>
        private void CastFlashlight ()
        {
            flashLightHit = default;
            if (!LightAndLaserState.AnyLightMain)
            {
                return;
            }
            if (Player.HandsController is Player.FirearmController fc)
            {
                var ray = new Ray(fc.FireportPosition, fc.WeaponDirection + UnityEngine.Random.insideUnitSphere / 7.5f);
                Physics.Raycast(ray, out flashLightHit, 20, ambienceRaycastMask);
            }
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
            if (slowRT == null)
                slowRT = new Texture2D(RESOLUTION, RESOLUTION, TextureFormat.RGBA32, false);
            if (display == null)
            {
                display = new GameObject().AddComponent<RawImage>();
                display.transform.SetParent(MonoBehaviourSingleton<GameUI>.Instance.RectTransform());
                display.RectTransform().sizeDelta = new Vector2(160, 160);
                display.texture = slowRT;
                display.RectTransform().anchoredPosition = new Vector2(-720, -360);
            }
            
            display.enabled = ThatsLitPlugin.DebugTexture.Value;
        }

        private void OnDestroy()
        {
            DisableBrightness();
            if (display) GameObject.Destroy(display.gameObject);
            if (cam) GameObject.Destroy(cam.gameObject);
            if (rt) rt.Release();
            observed.Dispose();
        }
        float litFactorSample, ambScoreSample;
        static float benchmarkSampleSeenCoef, benchmarkSampleEncountering, benchmarkSampleExtraVisDis, benchmarkSampleScoreCalculator, benchmarkSampleUpdate, benchmarkSampleFUpdate, benchmarkSampleFoliageCheck, benchmarkSampleTerrainCheck, benchmarkSampleGUI, benchmarkSampleNoBushOverride, benchmarkSampleBlindFire;
        int guiFrame;
        string infoCache1, infoCache2, infoCacheBenchmark;

        void OnGUIInfo ()
        {
            if (ThatsLitPlugin.ScoreInfo.Value
             || ThatsLitPlugin.WeatherInfo.Value
             || ThatsLitPlugin.EquipmentInfo.Value
             || ThatsLitPlugin.TerrainInfo.Value
             || ThatsLitPlugin.FoliageInfo.Value)
            {
                switch (ThatsLitPlugin.InfoOffset.Value)
                {
                    case 1:
                        GUILayout.Label("\n\n\n\n", style);
                        break;
                    case 2:
                        GUILayout.Label("\n\n\n\n\n\n\n\n", style);
                        break;
                    case 3:
                        GUILayout.Label("\n\n\n\n\n\n\n\n\n\n\n\n", style);
                        break;
                    case 4:
                        GUILayout.Label("\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n", style);
                        break;
                    case 5:
                        GUILayout.Label("\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n", style);
                        break;
                    case 6:
                        GUILayout.Label("\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n", style);
                        break;
                    case 7:
                        GUILayout.Label("\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n", style);
                        break;
                }
            }
            if (ThatsLitPlugin.ScoreInfo.Value
             && gameworld.ScoreCalculator != null
             && Time.time < setupTime + 10)
                GUILayout.Label("  [That's Lit] The HUD can be configured in plugin settings.", style);

            if (ThatsLitPlugin.ScoreInfo.Value && PlayerLitScoreProfile != null)
            {
                Utility.GUILayoutDrawAsymetricMeter((int)(PlayerLitScoreProfile.frame0.multiFrameLitScore / 0.0999f), false, style);
                Utility.GUILayoutDrawAsymetricMeter((int)(Mathf.Pow(PlayerLitScoreProfile.frame0.multiFrameLitScore, POWER) / 0.0999f), false, style);
            }

            if (ThatsLitPlugin.EquipmentInfo.Value && LightAndLaserState.storage != 0)
                GUILayout.Label(LightAndLaserState.Format(), style);

            if (ThatsLitPlugin.FoliageInfo.Value
             && Foliage != null
             && Foliage.FoliageScore > 0)
                Utility.GUILayoutFoliageMeter((int)(Foliage.FoliageScore / 0.0999f), false, style);
            if (ThatsLitPlugin.TerrainInfo.Value && TerrainDetails != null && terrainScoreHintProne > 0.0998f)
                if (Player.IsInPronePose)
                    Utility.GUILayoutTerrainMeter((int)(terrainScoreHintProne / 0.0999f), false, style);
                else
                    Utility.GUILayoutTerrainMeter((int)(terrainScoreHintRegular / 0.0999f), false, style);

            if (ThatsLitPlugin.WeatherInfo.Value && PlayerLitScoreProfile != null)
            {
                if (cloud <= -1.1f)
                    GUILayout.Label("  CLEAR ☀☀☀", style);
                else if (cloud <= -0.7f)
                    GUILayout.Label("  CLEAR ☀☀", style);
                else if (cloud <= -0.25f)
                    GUILayout.Label("  CLEAR ☀", style);
                else if (cloud >= 1.1f)
                    GUILayout.Label("  CLOUDY ☁☁☁", style);
                else if (cloud >= 0.7f)
                    GUILayout.Label("  CLOUDY ☁☁", style);
                else if (cloud >= 0.25f)
                    GUILayout.Label("  CLOUDY ☁", style);
            }
        }

        GUIStyle style;

        private void OnGUI()
        {
            if (Player?.IsYourPlayer != true)
                return;

            var align = GUI.skin.label.alignment;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft
                };
                if (ThatsLitPlugin.InfoFontSizeOverride.Value != 0) style.fontSize = ThatsLitPlugin.InfoFontSizeOverride.Value;
            }
            if (style.fontSize != ThatsLitPlugin.InfoFontSizeOverride.Value)
            {
                if (ThatsLitPlugin.InfoFontSizeOverride.Value != 0)
                    style.fontSize = ThatsLitPlugin.InfoFontSizeOverride.Value;
                else
                    style.fontSize = GUI.skin.label.fontSize;
            }

            bool layoutCall = guiFrame < Time.frameCount;
            ThatsLitPlugin.swGUI.MaybeResume();
            if (PlayerLitScoreProfile == null)
            {
                if (Time.time - setupTime < 30f && !ThatsLitPlugin.HideMapTip.Value)
                    GUILayout.Label("  [That's Lit] Brightness module is disabled in configs or not supported on this map.", style);
            }

            var poseFactor = Player.PoseLevel / Player.Physical.MaxPoseLevel * 0.6f + 0.4f; // crouch: 0.4f
            if (Player.IsInPronePose)
                poseFactor -= 0.4f; // prone: 0
            poseFactor += 0.05f; // base -> prone -> 0.05f, crouch -> 0.45f

            OnGUIInfo();
            ThatsLitAPI.OnMainPlayerGUI?.Invoke();

            if (!ThatsLitPlugin.DebugInfo.Value || DebugInfo == null)
            {
                ThatsLitPlugin.swGUI.Stop();
                guiFrame = Time.frameCount;
                return;
            }

            float fog = WeatherController.Instance?.WeatherCurve?.Fog ?? 0;
            float rain = WeatherController.Instance?.WeatherCurve?.Rain ?? 0;
            float cloud = WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0;
            
            if (layoutCall)
                infoCache1 = $"  IMPACT: {DebugInfo.lastCalcFrom:0.000} -> {DebugInfo.lastCalcTo0:0.000} -> {DebugInfo.lastCalcTo1:0.000} -> {DebugInfo.lastCalcTo2:0.000} -> {DebugInfo.lastCalcTo3:0.000} -> {DebugInfo.lastCalcTo4:0.000} -> {DebugInfo.lastCalcTo5:0.000} -> {DebugInfo.lastCalcTo6:0.000} -> {DebugInfo.lastCalcTo7:0.000} -> {DebugInfo.lastCalcTo8:0.000}\n ({DebugInfo.lastFactor2:0.000} <- {DebugInfo.lastFactor1:0.000} <- {DebugInfo.lastScore:0.000}) AMB: {ambScoreSample:0.00} LIT: {litFactorSample:0.00} (SAMPLE)\n  AFFECTED: {DebugInfo.calced} (+{DebugInfo.calcedLastFrame})\n  ENCOUNTER: {DebugInfo.encounter}  V.HINT: { DebugInfo.vagueHint }  V.CANCEL: { DebugInfo.vagueHintCancel }  SIG.D: { DebugInfo.signalDanger }\n  LAST SHOT: { lastShotVector } { Time.time - lastShotTime:0.0}s { DebugInfo.lastEncounterShotAngleDelta }deg  -{ DebugInfo.lastEncounteringShotCutoff }x\n  LAST_PARTS: { DebugInfo.lastVisiblePartsFactor} (x9)  G.OVL: { DebugInfo.lastGlobalOverlookChance:P1}\n  V.DIS.COMP: { DebugInfo.lastDisCompThermal }(T)  { DebugInfo.lastDisCompNVG }(NVG)  { DebugInfo.lastDisCompDay }(Day)  { DebugInfo.lastDisComp }  Focus: { DebugInfo.lastNearestFocusAngleX:0.0}degX/{ DebugInfo.lastNearestFocusAngleY:0.0}degY\n  TERRAIN: { terrainScoreHintProne :0.000}/{ terrainScoreHintRegular :0.000}  3x3/5x5: { TerrainDetails?.RecentDetailCount3x3 }/{ TerrainDetails?.RecentDetailCount5x5 } (score-{ PlayerLitScoreProfile?.detailBonusSmooth:0.00})  FOLIAGE: {Foliage?.FoliageScore:0.000} ({Foliage?.FoliageCount}) (H{Foliage?.Nearest?.dis:0.00} to {Foliage?.Nearest?.name}) RAT: { DebugInfo.lastBushRat:0.00}\n  FOG: {fog:0.000} / RAIN: {rain:0.000} / CLOUD: {cloud:0.000} / TIME: {Utility.GetInGameDayTime():0.000} / WINTER: {gameworld.IsWinter}\n  POSE: {poseFactor} SPEED: { Player.Velocity.magnitude :0.000}  INSIDE: { Time.time - lastOutside:0.000}  AMB: { ambienceShadownRating:0.000}  OVH: { overheadHaxRating:0.000}  BNKR: { bunkerTimeClamped:0.000}  SRD: { surroundingRating:0.000}\n  {DebugInfo.nearestOffset}  Caution: { DebugInfo.nearestCaution }  NoBush.Cancel: {DebugInfo.cancelledSAINNoBush}/{DebugInfo.attemptToCancelSAINNoBush} {DebugInfo.lastInterruptChance:P1}  {DebugInfo.lastInterruptChanceDis:0.0}m SNP: {DebugInfo.sniperHintOffset} ({DebugInfo.sniperHintChance:P1})  FL: {flashLightHit.point:000.0}  HINT:{DebugInfo.flashLightHint}\n  F.L:{DebugInfo.forceLooks}  S.L:{DebugInfo.sideLooks}\n Throttle: { !cam.enabled } { cameraThrottleFrequency }";
            GUILayout.Label(infoCache1, style);
            // GUILayout.Label(string.Format(" FOG: {0:0.000} / RAIN: {1:0.000} / CLOUD: {2:0.000} / TIME: {3:0.000} / WINTER: {4}", WeatherController.Instance?.WeatherCurve?.Fog ?? 0, WeatherController.Instance?.WeatherCurve?.Rain ?? 0, WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0, GetInGameDayTime(), isWinterCache));
            
            OnGUIScoreCalc(style);

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
                    infoCacheBenchmark = $"  Update:         {benchmarkSampleUpdate,8:0.0000}\n  FUpdate:         {benchmarkSampleFUpdate,8:0.0000}\n  Foliage:        {benchmarkSampleFoliageCheck,8:0.0000}\n  Terrain:        {benchmarkSampleTerrainCheck,8:0.0000}\n  SeenCoef:       {benchmarkSampleSeenCoef,8:0.0000}\n  Encountering:   {benchmarkSampleEncountering,8:0.0000}\n  ExtraVisDis:    {benchmarkSampleExtraVisDis,8:0.0000}\n  ScoreCalculator:{benchmarkSampleScoreCalculator,8:0.0000}\n  Info(+Debug):    {benchmarkSampleGUI,8:0.0000}\n  No Bush OVR:    {benchmarkSampleNoBushOverride,8:0.0000}\n  BlindFire:    {benchmarkSampleBlindFire,8:0.0000} ms";
                GUILayout.Label(infoCacheBenchmark, style);
                if (Time.frameCount % 6000 == 0)
                    if (layoutCall) EFT.UI.ConsoleScreen.Log(infoCacheBenchmark);
            }

            gameworld.ScoreCalculator?.OnGUI(PlayerLitScoreProfile, layoutCall);

            if (ThatsLitPlugin.DebugTerrain.Value && TerrainDetails?.Details5x5 != null)
            {
                infoCache2 = $"  DETAIL (SAMPLE): {DebugInfo?.lastFinalDetailScoreNearest:+0.00;-0.00;+0.00} ({DebugInfo?.lastDisFactorNearest:0.000}df) 3x3: { TerrainDetails.RecentDetailCount3x3}\n  {Utility.DetermineDir(DebugInfo?.lastTriggeredDetailCoverDirNearest ?? Vector3.zero)} {DebugInfo?.lastNearest:0.00}m {DebugInfo?.lastTiltAngle} {DebugInfo?.lastRotateAngle}";
                GUILayout.Label(infoCache2, style);
                // GUILayout.Label(string.Format(" DETAIL (SAMPLE): {0:+0.00;-0.00;+0.00} ({1:0.000}df) 3x3: {2}", arg0: lastFinalDetailScoreNearest, lastDisFactorNearest, recentDetailCount3x3));
                // GUILayout.Label(string.Format(" {0} {1:0.00}m {2} {3}", Utility.DetermineDir(lastTriggeredDetailCoverDirNearest), lastNearest, lastTiltAngle, lastRotateAngle));
                for (int i = TerrainDetails.GetDetailInfoIndex(2, 2, 0, gameworld.MaxDetailTypes); i < TerrainDetails.GetDetailInfoIndex(3, 2, 0, gameworld.MaxDetailTypes); i++) // List the underfoot
                    if (TerrainDetails.Details5x5[i].casted)
                        GUILayout.Label($"  { TerrainDetails.Details5x5[i].count } Detail#{i}({ TerrainDetails.Details5x5[i].name }))", style);
                Utility.GUILayoutDrawAsymetricMeter((int)(DebugInfo.lastFinalDetailScoreNearest / 0.0999f), false, style);
            }

            ThatsLitPlugin.swGUI.Stop();
            guiFrame = Time.frameCount;
            
        }
        string infoCache;
        internal virtual void OnGUIScoreCalc (GUIStyle style)
        {
            if (PlayerLitScoreProfile == null || DebugInfo == null) return;
            if (PlayerLitScoreProfile.IsProxy)
            {
                GUILayout.Label("  [PROXY]", style);
                return;
            }
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
            if (guiFrame < Time.frameCount) infoCache = $"  PIXELS: {DebugInfo.shinePixelsRatioSample * 100:000}% - {DebugInfo.highLightPixelsRatioSample * 100:000}% - {DebugInfo.highMidLightPixelsRatioSample * 100:000}% - { DebugInfo.midLightPixelsRatioSample * 100:000}% - {DebugInfo.midLowLightPixelsRatioSample * 100:000}% - {DebugInfo.lowLightPixelsRatioSample * 100:000}% | {DebugInfo.darkPixelsRatioSample * 100:000}% (AVG Sample)\n  AvgLum: {PlayerLitScoreProfile.frame0.avgLum:0.000}  AvgLumMF: {PlayerLitScoreProfile.frame0.avgLumMultiFrames:0.000} / {gameworld.ScoreCalculator.GetMinAmbianceLum():0.000} ~ {gameworld?.ScoreCalculator?.GetMaxAmbianceLum():0.000} ({gameworld?.ScoreCalculator?.GetAmbianceLumRange():0.000})\n   Sun: {gameworld?.ScoreCalculator?.sunLightScore:0.000}/{gameworld?.ScoreCalculator?.GetMaxSunlightScore():0.000}, Moon: {gameworld?.ScoreCalculator?.moonLightScore:0.000}/{gameworld?.ScoreCalculator?.GetMaxMoonlightScore():0.000}\n  SCORE : {DebugInfo.scoreRawBase:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw0:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw1:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw2:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw3:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw4:＋0.00;－0.00;+0.00} (SAMPLE)";            
            GUILayout.Label(infoCache, style);

            Utility.GUILayoutDrawAsymetricMeter((int)(PlayerLitScoreProfile.frame0.score / 0.0999f), false, style);
        }

        private void ConcludeBenchmarks()
        {
            benchmarkSampleSeenCoef         = ThatsLitPlugin.swSeenCoef.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleEncountering     = ThatsLitPlugin.swEncountering.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleExtraVisDis      = ThatsLitPlugin.swExtraVisDis.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleScoreCalculator  = ThatsLitPlugin.swScoreCalc.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleUpdate           = ThatsLitPlugin.swUpdate.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleFUpdate           = ThatsLitPlugin.swFUpdate.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleGUI              = ThatsLitPlugin.swGUI.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleFoliageCheck     = ThatsLitPlugin.swFoliage.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleTerrainCheck     = ThatsLitPlugin.swTerrain.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleNoBushOverride              = ThatsLitPlugin.swNoBushOverride.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleBlindFire              = ThatsLitPlugin.swBlindFireScatter.ConcludeMs() / (float) DEBUG_INTERVAL;
        }
    }
}