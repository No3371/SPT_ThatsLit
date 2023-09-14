using System.Collections;
using Comfort.Common;
using EFT;
using EFT.UI;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ThatsLit.Components
{
    public class ThatsLitMainPlayerComponent : MonoBehaviour
    {
        const int RESOLUTION = 48;
        public RenderTexture rt;
        public Camera cam;
        int currentCamPos = 0;
        public Texture2D tex, debugTex;
        public int highLightPixels, highMidLightPixels, midLightPixels, midLowLightPixels, lowLightPixels;
        public float highLightScore = 5f, highMidLightScore = 1.5f , midLightScore = 1f, midLowLightScore = 0.75f, lowLightScore = 0.4f;
        public float defaultLitScore, defaultLitScoreRaw;
        public float defaultLitScoreSample, defaultLitScoreRawSample;
        public float prevLitScore1, prevLitScore2, prevLitScore3, prevLitScore4, prevLitScore5;
        public float lastBrightest, lastDarkest;
        public float lastBrightestSample, lastDarkestSample;
        public float lastCalcFrom, lastCalcTo;
        public float avgLum, avgLumSample;
        public int calced = 0, calcedLastFrame = 0;
        public int lockPos = -1;
        public int lastValidPixels;
        public RawImage display;
        public bool disableVisionPatch;

        public void Awake()
        {
            Singleton<ThatsLitMainPlayerComponent>.Instance = this;
            MainPlayer = Singleton<GameWorld>.Instance.MainPlayer;

            rt = new RenderTexture(RESOLUTION, RESOLUTION, 0, GraphicsFormat.R8G8B8A8_SRGB);
            rt.filterMode = FilterMode.Point;
            rt.Create();

            tex = new Texture2D(RESOLUTION, RESOLUTION);

            cam = new GameObject().AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cam.transform.SetParent(MainPlayer.Transform.Original);

            cam.nearClipPlane = 0.005f;

            cam.cullingMask = LayerMaskClass.PlayerMask;
            cam.fieldOfView = 44;

            cam.targetTexture = rt;

            if (ThatsLitPlugin.DebugTexture.Value)
            {
                debugTex = new Texture2D(RESOLUTION, RESOLUTION);
                display = new GameObject().AddComponent<RawImage>();
                display.transform.SetParent(MonoBehaviourSingleton<GameUI>.Instance.RectTransform());
                display.RectTransform().sizeDelta = new Vector2(160, 160);
                display.texture = debugTex;
                display.RectTransform().anchoredPosition = new Vector2(-720, -320);
            }

            prevLitScore1 = prevLitScore2 = prevLitScore3 = prevLitScore4 = prevLitScore5 = 0;
        }


        private void Update()
        {
            if (lockPos != -1) currentCamPos = lockPos;
            var camHeight = MainPlayer.IsInPronePose ? 1.3f : 2.2f;
            var targetHeight = MainPlayer.IsInPronePose ? 0.2f : 0.6f;
            switch (currentCamPos++)
            {
                case 0:
                {
                    cam.transform.localPosition = new Vector3(0, camHeight, 0);
                    cam.transform.LookAt(MainPlayer.Transform.Original.position);
                    break;
                }
                case 1:
                    {
                        cam.transform.localPosition = new Vector3(0.7f, camHeight, 0.7f);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
                case 2:
                    {
                        cam.transform.localPosition = new Vector3(0.7f, camHeight, -0.7f);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
                case 3:
                    {
                        if (MainPlayer.IsInPronePose)
                        {
                            cam.transform.localPosition = new Vector3(0, camHeight, 0);
                            cam.transform.LookAt(MainPlayer.Transform.Original.position);
                        }
                        else
                        {
                            cam.transform.localPosition = new Vector3(0.05f, 0.4f);
                            cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * 1.25f);
                        }
                        break;
                    }
                case 4:
                    {
                        cam.transform.localPosition = new Vector3(-0.7f, camHeight, -0.7f);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                        break;
                    }
                case 5:
                    {
                        cam.transform.localPosition = new Vector3(-0.7f, camHeight, 0.7f);
                        cam.transform.LookAt(MainPlayer.Transform.Original.position + Vector3.up * targetHeight);
                        currentCamPos = 0;
                        break;
                    }
            }
        }

        void LateUpdate ()
        {
            calcedLastFrame = 0;
            prevLitScore5 = prevLitScore4;
            prevLitScore4 = prevLitScore3;
            prevLitScore3 = prevLitScore2;
            prevLitScore2 = prevLitScore1;
            prevLitScore1 = defaultLitScore;
            defaultLitScore = highLightPixels = highMidLightPixels = midLightPixels = midLowLightPixels = lowLightPixels = 0;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            if (debugTex != null && Time.frameCount % 61 == 0) Graphics.CopyTexture(tex, debugTex);

            var validPixels = 0;
            avgLum = 0.0f;
            for (int x = 0; x < RESOLUTION; x++)
            {
                for (int y = 0; y < RESOLUTION; y++)
                {
                    var c = tex.GetPixel(x, y);
                    if (c == Color.white)
                    {
                        continue;
                    }
                    var lum = (c.r + c.g + c.b) / 3f;
                    avgLum += lum;
                    if (lum > 0.6f) highLightPixels += 1;
                    else if (lum > 0.25f) highMidLightPixels += 1;
                    else if (lum > 0.13f) midLightPixels += 1;
                    else if (lum > 0.055f) midLowLightPixels += 1;
                    else if (lum > 0.015f) lowLightPixels += 1;
                    validPixels++;
                }
            }

            if (validPixels == 0) validPixels = RESOLUTION * RESOLUTION; // Not sure if the RenderTexture will be empty (fully white) or not at the very begining

            var brighterPixels = highLightPixels + highMidLightPixels + midLightPixels / 2;
            var darkerLitPixels = midLightPixels / 2 + midLowLightPixels + lowLightPixels;
            var darkerPixels = validPixels - brighterPixels;
            defaultLitScore = highLightPixels * highLightScore + highMidLightPixels * highMidLightScore + midLightPixels * midLightScore + midLowLightPixels * midLowLightScore + lowLightPixels * lowLightScore;
            lastValidPixels = validPixels;
            defaultLitScore /= (float) validPixels;
            avgLum /= (float)validPixels;


            defaultLitScore = Mathf.Clamp01(defaultLitScore);
            defaultLitScore -= 0.5f;
            defaultLitScore *= 2f;

            defaultLitScoreRaw = defaultLitScore;

            defaultLitScore += ThatsLitPlugin.ScoreOffset.Value;

            // The most lit side is weighted more (A lot of time the player receive lights from only one side)
            var brightest = defaultLitScore;
            var darkest = defaultLitScore;
            if (prevLitScore1 > brightest) brightest = prevLitScore1;
            if (prevLitScore2 > brightest) brightest = prevLitScore2;
            if (prevLitScore3 > brightest) brightest = prevLitScore3;
            if (prevLitScore4 > brightest) brightest = prevLitScore4;
            if (prevLitScore5 > brightest) brightest = prevLitScore5;
            if (prevLitScore1 < darkest) darkest = prevLitScore1;
            if (prevLitScore2 < darkest) darkest = prevLitScore2;
            if (prevLitScore3 < darkest) darkest = prevLitScore3;
            if (prevLitScore4 < darkest) darkest = prevLitScore4;
            if (prevLitScore5 < darkest) darkest = prevLitScore5;

            lastDarkest = darkest;
            lastBrightest = brightest;


            if (brightest - darkest < 0.2f) // Low contrast
            {
                // Assuming the player is not receiving lights other than amibient
                if (defaultLitScore > 0 && avgLum > 0.08f) defaultLitScore /= 1.2f;
                if (defaultLitScore < 0.05f && (lowLightPixels / validPixels > 0.8f || darkerLitPixels > 0.98f) && brighterPixels / validPixels < 0.05f)
                {
                    var contrastFactor = 0.15f - (brightest - darkest);
                    contrastFactor += 1;
                    contrastFactor *= contrastFactor;
                    defaultLitScore -= UnityEngine.Random.Range(0.25f, 0.35f) * contrastFactor;
                }
            }

            defaultLitScore = (defaultLitScore + prevLitScore1 + prevLitScore2 + prevLitScore3 + prevLitScore4 + prevLitScore5) / 6f;

            // High contrast lit and shadow
            if (Mathf.Abs(brighterPixels - darkerPixels) / validPixels < 0.3f) // Both bright and dark pixels exist and are balanced
                defaultLitScore = (defaultLitScore + brightest * 3) / 4f;
            else if (brighterPixels - darkerPixels / validPixels > 0.1f) // Brighter pixels are more, could be in bright env, assuming the score is already high
                defaultLitScore = defaultLitScore;
            else if (brighterPixels > validPixels * 0.1f && brighterPixels - darkerPixels / validPixels < -0.1f) // Darker pixels are more but still some lit sides
                defaultLitScore = (defaultLitScore + brightest * 2) / 3f;

            if (defaultLitScore < 0 && (MainPlayer.AIData.UsingLight || MainPlayer.AIData.GetFlare)) defaultLitScore /= 2f;
            if (defaultLitScore > 0 && (MainPlayer.AIData.UsingLight || MainPlayer.AIData.GetFlare)) defaultLitScore *= 1.1f;

            defaultLitScore = Mathf.Clamp(defaultLitScore, -1, 1);

        }

        private void OnDestroy()
        {
            if (display) GameObject.Destroy(display);
            GameObject.Destroy(cam);
            rt.Release();

        }

        private void OnGUI()
        {
            if (!ThatsLitPlugin.DebugInfo.Value) return;
                GUILayout.Label(string.Format("PIXELS: {0:000} - {1:000} - {2:000} - {3:0000} - {4:0000}", highLightPixels, highMidLightPixels, midLightPixels, midLowLightPixels, lowLightPixels));
            GUILayout.Label(string.Format("PIXELS: {0:000}% - {1:000}% - {2:000}% - {3:000}% - {4:000}%", (highLightPixels / (float) lastValidPixels) * 100, (highMidLightPixels / (float) lastValidPixels) * 100, (midLightPixels / (float) lastValidPixels) * 100, (midLowLightPixels / (float) lastValidPixels) * 100, (lowLightPixels / (float) lastValidPixels) * 100));
            switch ((int) (defaultLitScore / 0.1f))
            {
                case -11:
                    GUILayout.Label("＋＋＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -10:
                    GUILayout.Label("＋＋＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -9:
                    GUILayout.Label("－＋＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -8:
                    GUILayout.Label("－－＋＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -7:
                    GUILayout.Label("－－－＋＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -6:
                    GUILayout.Label("－－－－＋＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -5:
                    GUILayout.Label("－－－－－＋＋＋＋＋|－－－－－－－－－－");
                    break;
                case -4:
                    GUILayout.Label("－－－－－－＋＋＋＋|－－－－－－－－－－");
                    break;
                case -3:
                    GUILayout.Label("－－－－－－－＋＋＋|－－－－－－－－－－");
                    break;
                case -2:
                    GUILayout.Label("－－－－－－－－＋＋|－－－－－－－－－－");
                    break;
                case -1:
                    GUILayout.Label("－－－－－－－－－＋|－－－－－－－－－－");
                    break;
                case 0:
                    GUILayout.Label("－－－－－－－－－－|－－－－－－－－－－");
                    break;
                case 1:
                    GUILayout.Label("－－－－－－－－－－|＋－－－－－－－－－");
                    break;
                case 2:
                    GUILayout.Label("－－－－－－－－－－|＋＋－－－－－－－－");
                    break;
                case 3:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋－－－－－－－");
                    break;
                case 4:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋－－－－－－");
                    break;
                case 5:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋－－－－－");
                    break;
                case 6:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋－－－－");
                    break;
                case 7:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋－－－");
                    break;
                case 8:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋－－");
                    break;
                case 9:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋＋－");
                    break;
                case 10:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋＋＋");
                    break;
                case 11:
                    GUILayout.Label("－－－－－－－－－－|＋＋＋＋＋＋＋＋＋＋");
                    break;
            }
            if (Time.frameCount % 43 == 0)
            {
                defaultLitScoreSample = defaultLitScore;
                defaultLitScoreRawSample = defaultLitScoreRaw;
                lastDarkestSample = lastDarkest;
                lastBrightestSample = lastBrightest;
                avgLumSample = avgLum;
            }
            GUILayout.Label(string.Format("SCORE : {0:＋0.00;－0.00;+0.00} -> {1:＋0.00;－0.00;+0.00} -> {2:＋000.0;－000.0;+000.0}%", defaultLitScoreRaw, defaultLitScore, Mathf.Pow(defaultLitScore, 3) * 100));
            GUILayout.Label(string.Format("SCORE : {0:＋0.00;－0.00;+0.00} -> {1:＋0.00;－0.00;+0.00} -> {2:＋000.0;－000.0;+000.0}% (SAMPLE)", defaultLitScoreRawSample, defaultLitScoreSample, Mathf.Pow(defaultLitScoreSample, 3) * 100));
            GUILayout.Label(string.Format("CONTRA: {0:＋0.00;－0.00} <-> {1:＋0.00;－0.00} (SAMPLE)", lastDarkestSample, lastBrightestSample));
            GUILayout.Label(string.Format("AVGLUM: {0:＋0.00;－0.00}", avgLumSample));
            GUILayout.Label(string.Format("IMPACT: {0:0.00} -> {1:0.00} (SAMPLE)", lastCalcFrom, lastCalcTo));
            //GUILayout.Label(text: "PIXELS:");
            //GUILayout.Label(lastValidPixels.ToString());
            GUILayout.Label(string.Format("AFFECTED: {0} (+{1})", calced, calcedLastFrame));
        }

        public Player MainPlayer { get; private set; }
    }
}