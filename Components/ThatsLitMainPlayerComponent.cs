using System;
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
                    case "interchange":
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

            if (gquReq.done && !ThatsLitPlugin.NoGPUReq.Value) gquReq = AsyncGPUReadback.Request(rt, 0, req =>
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

        //// 20 ~ 22.5 => 0 ~ 2 => 3 ~ 5 => 5 ~ 7
        // SUN- => MOON+ => MOON- => SUN+
        // DARK (-1) <-> BRIGHT (1)
        float GetTimeLighingFactor ()
        {
            var time = GetInGameDayTime();
            if (time >= 20 && time < 24)
                return -Mathf.Clamp01((time - 20f) / 2.25f);
            else if (time >= 0 && time < 3)
                return GetBrightestNightHourFactor() - Mathf.Clamp01((2f - time) / 2f) * (GetBrightestNightHourFactor() + 1f); // 0:00 -> 100%, 1:00 -> 75%, 2:00 -> 25%, 3:00 -> 25%
            else if (time >= 3 && time < 5)
                return GetBrightestNightHourFactor() - Mathf.Clamp01((time - 3f) / 2f) * (GetBrightestNightHourFactor() + 1f); // 3:00 -> 25%, 5:00 -> 100%
            else if (time >= 5 && time < 8)
                return -Mathf.Clamp01((8f - time) / 3f); // 5:00 -> 100%, 8:00 -> 0%
            else if (time >= 7 && time < 16)
                return 1f - Mathf.Clamp01((12f - time) / 6f);
            else if (time >= 16 && time < 20)
                return Mathf.Clamp01((20f - time) / 7f);
            else return 0;

            // Maybe should be done with score profile
            float GetBrightestNightHourFactor ()
            {
                switch (activeRaidSettings?.SelectedLocation?.Name)
                {
                    case "Woods":
                        return -0.35f; // Moon is dimmer in Woods?
                    default:
                        return -0.25f;
                }
            }
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

        //void DetermineShiningEquipments (Player player, out bool vLight, out bool vLaser, out bool irLight, out bool irLaser, out bool vLightSub, out bool vLaserSub, out bool irLightSub, out bool irLaserSub)
        //{
        //    vLight = vLaser = irLight = irLaser = vLightSub = vLaserSub = irLightSub = irLaserSub = false;
        //    IEnumerable<(Item item, LightComponent light)> activeLights;
        //    if (player?.ActiveSlot?.ContainedItem != null)
        //    {
        //        activeLights = (MainPlayer.ActiveSlot.ContainedItem as Weapon)?.AllSlots
        //            .Select<Slot, Item>((Func<Slot, Item>)(x => x.ContainedItem))
        //            .GetComponents<LightComponent>().Where(c => c.IsActive).Select(l => (l.Item, l)); ;

        //        if (activeLights != null)
        //        foreach (var i in activeLights)
        //        {
        //            MapComponentsModes(i.item.TemplateId, i.light.SelectedMode, out bool thisLight, out bool thisLaser, out bool thisLightIsIR, out bool thisLaserIsIR);
        //            if (thisLight && !thisLightIsIR) vLight = true;
        //            if (thisLight && thisLightIsIR) irLight = true;
        //            if (thisLaser && !thisLaserIsIR) vLaser = true;
        //            if (thisLaser && thisLaserIsIR) irLaser = true;
        //            if (vLight) return; // Early exit for main visible light because that's enough to decrease score
        //        }
        //    }

        //    var inv = player?.ActiveSlot?.ContainedItem?.Owner as InventoryControllerClass;

        //    if (inv == null) return;

        //    var helmet = inv?.Inventory?.Equipment?.GetSlot(EquipmentSlot.Headwear)?.ContainedItem as GClass2537;

        //    if (helmet != null)
        //    {
        //        activeLights = (MainPlayer.ActiveSlot.ContainedItem as Weapon)?.AllSlots
        //            .Select<Slot, Item>((Func<Slot, Item>)(x => x.ContainedItem))
        //            .GetComponents<LightComponent>().Where(c => c.IsActive).Select(l => (l.Item, l));

        //        foreach (var i in activeLights)
        //        {
        //            MapComponentsModes(i.item.TemplateId, i.light.SelectedMode, out bool thisLight, out bool thisLaser, out bool thisLightIsIR, out bool thisLaserIsIR);
        //            if (thisLight && !thisLightIsIR) vLight = true;
        //            if (thisLight && thisLightIsIR) irLight = true;
        //            if (thisLaser && !thisLaserIsIR) vLaser = true;
        //            if (thisLaser && thisLaserIsIR) irLaser = true;
        //            if (vLight) return; // Early exit for main visible light because that's enough to decrease score
        //        }
        //    }

        //    var secondaryWeapons = inv?.Inventory?.GetItemsInSlots(new[] { EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster });

        //    if (secondaryWeapons != null)
        //    foreach (Item i in secondaryWeapons)
        //    {
        //        Weapon w = i as Weapon;
        //        if (w == null) continue;
        //        activeLights = (MainPlayer.ActiveSlot.ContainedItem as Weapon)?.AllSlots
        //            .Select<Slot, Item>((Func<Slot, Item>)(x => x.ContainedItem))
        //            .GetComponents<LightComponent>().Where(c => c.IsActive).Select(l => (l.Item, l));

        //        foreach (var ii in activeLights)
        //        {
        //            MapComponentsModes(ii.item.TemplateId, ii.light.SelectedMode, out bool thisLight, out bool thisLaser, out bool thisLightIsIR, out bool thisLaserIsIR);
        //            if (thisLight && !thisLightIsIR) vLightSub = true;
        //            if (thisLight && thisLightIsIR) irLightSub = true;
        //            if (thisLaser && !thisLaserIsIR) vLaserSub = true;
        //            if (thisLaser && thisLaserIsIR) irLaserSub = true;
        //            if (vLightSub) return; // Early exit for main visible light because that's enough to decrease score
        //        }
        //    }
        //    // GClass2550 544909bb4bdc2d6f028b4577 x item tactical_all_insight_anpeq15 2457 / V + IR + IRL / MODES: 4  V -> IR -> IRL -> IR+IRL
        //    // 560d657b4bdc2da74d8b4572 tactical_all_zenit_2p_kleh_vis_laser MODES: 3, F -> F+V -> V
        //    // GClass2550 56def37dd2720bec348b456a item tactical_all_surefire_x400_vis_laser 2457 F + V MDOES: 3: F -> F + V -> V
        //    // 57fd23e32459772d0805bcf1 item tactical_all_holosun_ls321 2457 V + IR + IRL MDOES 4: V -> IR -> IRL -> IRL + IR
        //    // 55818b164bdc2ddc698b456c tactical_all_zenit_2irs_kleh_lam MODES: 3 IRL -> IRL+IR -> IR
        //    // 5a7b483fe899ef0016170d15 tactical_all_surefire_xc1 MODES: 1
        //    // 5a800961159bd4315e3a1657 tactical_all_glock_gl_21_vis_lam MODES 3
        //    // 5b07dd285acfc4001754240d tactical_all_steiner_las_tac_2 Modes 1

        //    // "_id": "5b3a337e5acfc4704b4a19a0", "_name": "tactical_all_zenit_2u_kleh", 1
        //    //"_id": "5c06595c0db834001a66af6c", "_name": "tactical_all_insight_la5", 4, V -> IR -> IRL -> IRL+IR
        //    //"_id": "5c079ed60db834001a66b372", "_name": "tactical_tt_dlp_tactical_precision_laser_sight", 1
        //    //"_id": "5c5952732e2216398b5abda2", "_name": "tactical_all_zenit_perst_3", 4
        //    //"_id": "5cc9c20cd7f00c001336c65d", "_name": "tactical_all_ncstar_tactical_blue_laser", 1
        //    //"_id": "5d10b49bd7ad1a1a560708b0", "_name": "tactical_all_insight_anpeq2", 2
        //    //"_id": "5d2369418abbc306c62e0c80", "_name": "tactical_all_steiner_9021_dbal_pl", 6 / F -> V -> F+V -> IRF -> IR -> IRF+IR
        //    //"_id": "61605d88ffa6e502ac5e7eeb", "_name": "tactical_all_wilcox_raptar_es", 5 / RF -> V -> IR -> IRL -> IRL+IR
        //    //"_id": "626becf9582c3e319310b837", "_name": "tactical_all_insight_wmx200", 2
        //    //"_id": "6272370ee4013c5d7e31f418", "_name": "tactical_all_olight_baldr_pro", 3
        //    //"_id": "6272379924e29f06af4d5ecb", "_name": "tactical_all_olight_baldr_pro_tan", 3


        //    //"_id": "57d17c5e2459775a5c57d17d", "_name": "flashlight_ultrafire_WF-501B", 1 (2) (different slot)
        //    //"_id": "59d790f486f77403cb06aec6", "_name": "flashlight_armytek_predator_pro_v3_xhp35_hi", 1(2) (different slot)


        //    // "_id": "5bffcf7a0db83400232fea79", "_name": "pistolgrip_tt_pm_laser_tt_206", always on
        //}
        //void MapComponentsModes(string templateId, int selectedMode, out bool light, out bool laser, out bool lightIsIR, out bool laserIsIR)
        //{
        //    light = laser = laserIsIR = lightIsIR = false;

        //    switch (templateId)
        //    {
        //        case "544909bb4bdc2d6f028b4577": // tactical_all_insight_anpeq15
        //        case "57fd23e32459772d0805bcf1": // tactical_all_holosun_ls321
        //        case "5c06595c0db834001a66af6c": // tactical_all_insight_la5
        //        case "5c5952732e2216398b5abda2": // tactical_all_zenit_perst_3
        //            switch (selectedMode)
        //            {
        //                case 0:
        //                    laser = true;
        //                    break;
        //                case 1:
        //                    laser = laserIsIR = true;
        //                    break;
        //                case 2:
        //                    light = lightIsIR = true;
        //                    break;
        //                case 3:
        //                    laser = laserIsIR = light = lightIsIR = true;
        //                    break;
        //            }
        //            break;
        //        case "61605d88ffa6e502ac5e7eeb": // tactical_all_wilcox_raptar_es
        //            switch (selectedMode)
        //            {
        //                case 1:
        //                    laser = true;
        //                    break;
        //                case 2:
        //                    laser = laserIsIR = true;
        //                    break;
        //                case 3:
        //                    light = lightIsIR = true;
        //                    break;
        //                case 4:
        //                    laser = laserIsIR = light = lightIsIR = true;
        //                    break;
        //            }
        //            break;
        //        case "560d657b4bdc2da74d8b4572": // tactical_all_zenit_2p_kleh_vis_laser
        //        case "56def37dd2720bec348b456a": // tactical_all_surefire_x400_vis_laser
        //        case "5a800961159bd4315e3a1657": // tactical_all_glock_gl_21_vis_lam
        //        case "6272379924e29f06af4d5ecb": // tactical_all_olight_baldr_pro_tan
        //        case "6272370ee4013c5d7e31f418": // tactical_all_olight_baldr_pro
        //            switch (selectedMode)
        //            {
        //                case 0:
        //                    light = true;
        //                    break;
        //                case 1:
        //                    laser = light = true;
        //                    break;
        //                case 2:
        //                    laser = true;
        //                    break;
        //            }
        //            break;
        //        case "55818b164bdc2ddc698b456c": // tactical_all_zenit_2irs_kleh_lam
        //            switch (selectedMode)
        //            {
        //                case 0:
        //                    light = lightIsIR = true;
        //                    break;
        //                case 1:
        //                    laser = laserIsIR = light = lightIsIR = true;
        //                    break;
        //                case 2:
        //                    laser = laserIsIR = true;
        //                    break;
        //            }
        //            break;
        //        case "5a7b483fe899ef0016170d15": // tactical_all_surefire_xc1
        //        case "5b3a337e5acfc4704b4a19a0": // tactical_all_zenit_2u_kleh
        //        case "59d790f486f77403cb06aec6": // flashlight_armytek_predator_pro_v3_xhp35_hi
        //        case "57d17c5e2459775a5c57d17d": // flashlight_ultrafire_WF
        //            light = true;
        //            break;
        //        case "5b07dd285acfc4001754240d": // tactical_all_steiner_las_tac_2
        //        case "5c079ed60db834001a66b372": // tactical_tt_dlp_tactical_precision_laser_sight
        //        case "5cc9c20cd7f00c001336c65d": // tactical_all_ncstar_tactical_blue_laser
        //        case "5bffcf7a0db83400232fea79": // pistolgrip_tt_pm_laser_tt_206
        //            laser = true;
        //            break;
        //        case "5d10b49bd7ad1a1a560708b0": // tactical_all_insight_anpeq2
        //            switch (selectedMode)
        //            {
        //                case 0:
        //                    laser = laserIsIR = true;
        //                    break;
        //                case 1:
        //                    laser = laserIsIR = light = lightIsIR = true;
        //                    break;
        //                case 2:
        //                    break;
        //            }
        //            break;
        //        case "5d2369418abbc306c62e0c80": // tactical_all_steiner_9021_dbal_pl
        //            switch (selectedMode)
        //            {
        //                case 0:
        //                    light = true;
        //                    break;
        //                case 1:
        //                    laser = true;
        //                    break;
        //                case 2:
        //                    laser = light = true;
        //                    break;
        //                case 3:
        //                    light = lightIsIR = true;
        //                    break;
        //                case 4:
        //                    laser = laserIsIR = true;
        //                    break;
        //                case 5:
        //                    light = lightIsIR = laser = laserIsIR = true;
        //                    break;
        //            }
        //            break;
        //        case "626becf9582c3e319310b837": // tactical_all_insight_wmx200
        //            switch (selectedMode)
        //            {
        //                case 0:
        //                    light = true;
        //                    break;
        //                case 1:
        //                    light = lightIsIR = true;
        //                    break;
        //            }
        //            break;
        //    }
        //}

    }
}