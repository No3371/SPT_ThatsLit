using SPT.Reflection.Patching;
using Comfort.Common;
using HarmonyLib;
using System.Reflection;
using UnityEngine;


namespace ThatsLit
{
    // public class ShadowMaskExtractorPatch : ModulePatch
    // {
    //     static Camera tlCam;

    //     protected override MethodBase GetTargetMethod()
    //     {
    //         return AccessTools.Method(typeof(ShadowMaskExtractor), "smethod_0");
    //     }
    //     [PatchPostfix]
    //     public static bool PatchPostfix(bool value, Camera currentCamera)
    //     {
    //         if (tlCam == null) tlCam = Singleton<ThatsLitMainPlayerComponent>.Instance?.cam;
    //         return currentCamera == tlCam;
    //     }
    // }
}