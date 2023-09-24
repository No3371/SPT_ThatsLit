﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.Weather;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ThatsLit.Components
{
    public class ThatsLitMainPlayerComponent : MonoBehaviour
    {
        static readonly List<string> EnabledMaps = new List<string>() { "Customs", "Shoreline", "Lighthouse", "Woods", "Reserve", "Factory" };
        public bool disabledLit;
        static readonly int RESOLUTION = ThatsLitPlugin.LowResMode.Value? 32 : 64;
        public const int POWER = 3;
        public RenderTexture rt, envRt;
        public Camera cam, envCam;
        int currentCamPos = 0;
        public Texture2D envTex, envDebugTex;
        Unity.Collections.NativeArray<Color32> observed;
        public float lastCalcFrom, lastCalcTo;
        public int calced = 0, calcedLastFrame = 0;
        public int lockPos = -1;
        public int lastValidPixels = RESOLUTION * RESOLUTION;
        public RawImage display;
        public RawImage displayEnv;
        public bool disableVisionPatch;

        public float cloudinessCompensationScale = 0.5f;

        public float foliageScore;
        int foliageCount;
        internal Vector2 foliageDir;
        Collider[] collidersCache;
        public LayerMask foliageLayerMask = 1 << LayerMask.NameToLayer("Foliage") | 1 << LayerMask.NameToLayer("PlayerSpiritAura");
        // PlayerSpiritAura is Visceral Bodies compat

        float awakeAt, lastCheckedLights, lastCheckedFoliages;
        // Note: If vLight > 0, other counts may be skipped
        public bool vLight, vLaser, irLight, irLaser, vLightSub, vLaserSub, irLightSub, irLaserSub;

        public Vector3 envCamOffset = new Vector3(0, 2, 0);

        public RaidSettings activeRaidSettings;
        bool skipFoliageCheck;
        public float fog, rain, cloud;
        public float MultiFrameLitScore { get; private set; }

        ScoreCalculator scoreCalculator;
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
                        break;
                    case "factory4_day":
                        scoreCalculator = null;
                        skipFoliageCheck = true;
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
                    case "shoreline":
                        if (ThatsLitPlugin.EnableShoreline.Value) scoreCalculator = new ShorelineScoreCalculator();
                        break;
                    case "laboratory":
                        // scoreCalculator = new LabScoreCalculator();
                        skipFoliageCheck = true;
                        break;
                    case null:
                        if (ThatsLitPlugin.EnableHideout.Value) scoreCalculator = new HideoutScoreCalculator();
                        skipFoliageCheck = true;
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
            if (Time.time > lastCheckedFoliages + (ThatsLitPlugin.LessFoliageCheck.Value ? 0.75f : 0.4f))
            {
                UpdateFoliageScore(bodyPos);
            }
            if (disabledLit)
            {
                return;
            }
            if (lockPos != -1) currentCamPos = lockPos;
            var camHeight = MainPlayer.IsInPronePose ? 0.45f : 2.2f;
            var targetHeight = MainPlayer.IsInPronePose ? 0.2f : 0.7f;
            var horizontalScale = MainPlayer.IsInPronePose ? 1.2f : 1;
            switch (currentCamPos++)
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
                        currentCamPos = 0;
                        break;
                    }
            }

            if (gquReq.done) gquReq = AsyncGPUReadback.Request(rt, 0, req =>
            {
                if (req.hasError)
                    return;
                
                observed = req.GetData<Color32>();
            });

            if (ThatsLitPlugin.DebugTexture.Value && envCam)
            {
                envCam.transform.localPosition = envCamOffset;
                switch (currentCamPos)
                {
                    case 0:
                        {
                            envCam.transform.LookAt(bodyPos + Vector3.left * 25);
                            break;
                        }
                    case 1:
                        {
                            envCam.transform.LookAt(bodyPos + Vector3.right * 25);
                            break;
                        }
                    case 2:
                        {
                            envCam.transform.localPosition = envCamOffset;
                            envCam.transform.LookAt(bodyPos + Vector3.down * 10);
                            break;
                        }
                    case 3:
                        {
                            envCam.transform.LookAt(bodyPos + Vector3.back * 25);
                            break;
                        }
                    case 4:
                        {
                            envCam.transform.LookAt(bodyPos + Vector3.right * 25);
                            break;
                        }
                }
            }

            if (Time.time > lastCheckedLights + (ThatsLitPlugin.LessEquipmentCheck.Value ? 0.6f : 0.33f))
            {
                lastCheckedLights = Time.time;
                Utility.DetermineShiningEquipments(MainPlayer, out vLight, out vLaser, out irLight, out irLaser, out vLightSub, out vLaserSub, out irLightSub, out irLaserSub);
                scoreCalculator?.UpdateEquipmentLights(vLight, vLaser, irLight, irLaser, vLightSub, vLaserSub, irLightSub, irLaserSub);
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

                for (int i = 0; i < count; i++)
                {
                    Vector3 dir = (collidersCache[i].transform.position - bodyPos);
                    float dis = dir.magnitude;
                    if (dis < 0.25f) foliageScore += 3f;
                    else if (dis < 0.4f) foliageScore += 1f;
                    else if (dis < 0.6f) foliageScore += 0.5f;
                    else if (dis < 1f) foliageScore += 0.3f;
                    else if (dis < 2f) foliageScore += 0.15f;
                    else foliageScore += 0.05f;

                    if (dis < closet)
                    {
                        closet = dis;
                        foliageDir = new Vector2(dir.x, dir.z);
                    }
                }

                foliageCount = count;

                if (count > 0) foliageScore /= (float) count;
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

            calcedLastFrame = 0;
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
                GUILayout.Label("[That's Lit] The map is not supported or disabled in configs.");
                if (!ThatsLitPlugin.DebugInfo.Value) return;
            }
            if (ThatsLitPlugin.DebugInfo.Value || ThatsLitPlugin.ScoreInfo.Value)
            {
                Utility.GUILayoutDrawAsymetricMeter((int) (MultiFrameLitScore / 0.0999f));
                Utility.GUILayoutDrawAsymetricMeter((int)(Mathf.Pow(MultiFrameLitScore, POWER) / 0.0999f));
                if (foliageScore > 0.3f)
                    GUILayout.Label("[FOLIAGE+++]");
                else if (foliageScore > 0.2f)
                    GUILayout.Label("[FOLIAGE++]");
                else if (foliageScore > 0.1f)
                    GUILayout.Label("[FOLIAGE+]");
                else if (foliageScore > 0.05f)
                    GUILayout.Label("[FOLIAGE]");
                if (Time.time < awakeAt + 10)
                    GUILayout.Label("[That's Lit HUD] Can be disabled in plugin settings.");
            }
            if (!ThatsLitPlugin.DebugInfo.Value) return;
            scoreCalculator?.CalledOnGUI();
            GUILayout.Label(string.Format("IMPACT: {0:0.00} -> {1:0.00} (SAMPLE)", lastCalcFrom, lastCalcTo));
            //GUILayout.Label(text: "PIXELS:");
            //GUILayout.Label(lastValidPixels.ToString());
            GUILayout.Label(string.Format("AFFECTED: {0} (+{1})", calced, calcedLastFrame));

            GUILayout.Label(string.Format("FOLIAGE: {0:0.000} ({1})", foliageScore, foliageCount));
            GUILayout.Label(string.Format("FOG: {0:0.000} / RAIN: {1:0.000} / CLOUD: {2:0.000} / TIME: {3:0.000}", WeatherController.Instance?.WeatherCurve?.Fog ?? 0, WeatherController.Instance?.WeatherCurve?.Rain ?? 0, WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0, GetInGameDayTime()));
            GUILayout.Label(string.Format("LIGHT: [{0}] / LASER: [{1}] / LIGHT2: [{2}] / LASER2: [{3}]", vLight? "V" : irLight? "I" : "-", vLaser ? "V" : irLaser ? "I" : "-", vLightSub ? "V" : irLightSub ? "I" : "-", vLaserSub ? "V" : irLaserSub ? "I" : "-"));
            GUILayout.Label(string.Format("{0} ({1})", activeRaidSettings?.LocationId, activeRaidSettings?.SelectedLocation?.Name));
            // GUILayout.Label(string.Format("{0:0.00000}ms / {1:0.00000}ms", benchMark1, benchMark2));

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
    }
}