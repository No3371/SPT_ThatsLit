﻿// #define DEBUG_DETAILS
using Aki.Reflection.Patching;
using EFT;
using HarmonyLib;
using ThatsLit.Components;
using System;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using EFT.Utilities;
using System.Globalization;
using UnityEngine.UIElements;
using System.Collections.Generic;


namespace ThatsLit
{
    public class SeenCoefPatch : ModulePatch
    {
        private static PropertyInfo _enemyRel;

        protected override MethodBase GetTargetMethod()
        {
            _enemyRel = AccessTools.Property(typeof(BotMemoryClass), "GoalEnemy");
            Type enemyInfoType = _enemyRel.PropertyType;
            return ReflectionHelper.FindMethodByArgTypes(enemyInfoType, new Type[] { typeof(BifacialTransform), typeof(BifacialTransform), typeof(BotDifficultySettingsClass), typeof(AIData), typeof(float), typeof(Vector3) });;
        }

        private static float nearestRecent;

        [PatchPostfix]
        public static void PatchPostfix(EnemyInfo __instance, BifacialTransform BotTransform, BifacialTransform enemy, ref float __result)
        {
            // if (ThatsLitPlugin.DevMode.Value && ThatsLitPlugin.DevModeInvisible.Value)
            // {
            //     __result = 8888;
            //     return;
            // }
            if (__result == 8888 || !ThatsLitPlugin.EnabledMod.Value) return;
            if (!ThatsLitPlugin.IncludeBosses.Value && __instance.Owner.Boss.IamBoss) return;
            ThatsLitMainPlayerComponent mainPlayer = Singleton<ThatsLitMainPlayerComponent>.Instance;

            var original = __result;

            if (Time.frameCount % 47 == 0)
            {
                if (mainPlayer) mainPlayer.calcedLastFrame = 0;
                if (mainPlayer) mainPlayer.foliageCloaking = false;
            }
           
            if (__instance.Person.IsYourPlayer)
            {
                if (!mainPlayer) return;
                if (mainPlayer.disableVisionPatch) return;
                nearestRecent += 0.5f;
                var caution = __instance.Owner.Id % 9; // 0 -> HIGH, 1,2,3 -> MID, 4,5,6,7,8 -> LOW
                float sinceSeen = Time.time - __instance.TimeLastSeen;
                bool isGoalEnemy = __instance.Owner.Memory.GoalEnemy == __instance;
                float immunityNegation = 0;

                Vector3 eyeToEnemyBody = mainPlayer.MainPlayer.MainParts[BodyPartType.body].Position - __instance.Owner.MainParts[BodyPartType.head].Position;
                var dis = eyeToEnemyBody.magnitude;
                var disFactor = Mathf.Clamp01((dis  - 10) / 100f);
                // To scale down various sneaking bonus
                // The bigger the distance the bigger it is, capped to 110m
                disFactor = disFactor * disFactor; // A slow accelerating curve, 110m => 1, 10m => 0, 50m => 0.16
                // The disFactor is to scale up effectiveness of various mechanics by distance
                // Once player is seen, it should be suppressed unless the player is out fo visual for sometime, to prevent interrupting long range fight
                disFactor = Mathf.Lerp(0, disFactor, sinceSeen / (8f * (1.2f - disFactor)) / (isGoalEnemy? 0.33f : 1f)); // Takes 1.6 seconds out of visual for the disFactor to reset for AIs at 110m away, 9.6s for 10m, 8.32s for 50m, if it's targeting the player, 3x the time

                var poseFactor = __instance.Person.AIData.Player.PoseLevel / __instance.Person.AIData.Player.Physical.MaxPoseLevel * 0.6f + 0.4f; // crouch: 0.4f
                bool isInPronePose = __instance.Person.AIData.Player.IsInPronePose;
                if (isInPronePose) poseFactor -= 0.4f; // prone: 0
                poseFactor += 0.05f; // base -> prone -> 0.05f, crouch -> 0.45f
                poseFactor = Mathf.Clamp01(poseFactor);

                Vector3 botVisionDir = __instance.Owner.GetPlayer.LookDirection;
                var visionAngleDelta = Vector3.Angle(botVisionDir, eyeToEnemyBody);
                var visionAngleDeltaVertical = Vector3.Angle(new Vector3(eyeToEnemyBody.x, 0, eyeToEnemyBody.z), eyeToEnemyBody) * (eyeToEnemyBody.y >= 0? 1f : -1f); // negative if looking down (higher), 0 when looking straight... 

                // Vector3 EyeToEnemyHead = mainPlayer.MainPlayer.MainParts[BodyPartType.body].Position - __instance.Owner.GetPlayer.MainParts[BodyPartType.head].Position;
                // Vector3 EyeToEnemyLeg = mainPlayer.MainPlayer.MainParts[BodyPartType.body].Position - __instance.Owner.GetPlayer.MainParts[BodyPartType.leftLeg].Position;
                // var visionAngleToEnemyHead = Vector3.Angle(botVisionDir, EyeToEnemyHead);

                var canSeeLight = mainPlayer.scoreCalculator?.vLight ?? false;
                if (!canSeeLight && __instance.Owner.NightVision.UsingNow && (mainPlayer.scoreCalculator?.irLight ?? false)) canSeeLight = true;
                var canSeeLaser = mainPlayer.scoreCalculator?.vLaser ?? false;
                if (!canSeeLaser && __instance.Owner.NightVision.UsingNow && (mainPlayer.scoreCalculator?.irLaser ?? false)) canSeeLaser = true;

                if (sinceSeen > 30f && !canSeeLight)
                {
                    var angleFactor = Mathf.Clamp01(1f * (visionAngleDeltaVertical - 15f) / 30f) + Mathf.Clamp01(2f * (visionAngleDeltaVertical - 45f) / 45f);
                    // Overlook close enemies at higher attitude and in low pose
                    var overheadFactor = angleFactor * (Mathf.Clamp01(visionAngleDelta - 15f) / 75f) * (1 - poseFactor * 1.5f); // 2.5+ (0%) ~ 10+ (100%) ... prone: 92.5%, crouch: 32.5%
                    overheadFactor *= Mathf.Clamp01((sinceSeen - 30) / 30f);
                    overheadFactor *= Mathf.Clamp01((__instance.Person.Position - __instance.EnemyLastPosition).magnitude / 12f);
                    if (UnityEngine.Random.Range(0f, 1f) <  Mathf.Clamp01(ThatsLitPlugin.GlobalRandomOverlookChance.Value) * 20f * overheadFactor * (1f - disFactor)) // mainly for "close but high" scenarios
                    {
                        __result *= 10; // Instead of set it to flat 8888, so if the player has been in the vision for quite some time, this don't block
                    }
                }
                
                if (__instance.Owner.WeaponManager.ShootController.IsAiming)
                {
                    float v = __instance.Owner?.WeaponManager?.CurrentWeapon?.GetSightingRange() ?? 50;
                    if (visionAngleDelta > 1000f/v // Out of scope
                     || __instance.Owner.NightVision.UsingNow) v = Mathf.Min(v, 50); // AIs using NVGs does not get the scope buff
                    disFactor *= 1 + 0.1f * (300 - v) / 100;
                    disFactor = Mathf.Clamp01(disFactor);
                    // 10m sight? => 1.29x... 10m -> 0, 110m -> 1.29x
                    // 50m sight => 1.25x... 10m -> 0, 110m -> 1.25x
                    // 100m sight => 1.2x... 10m -> 0, 110m -> 1.2x
                    // 300m sight => 1x... 110m -> 0.8
                    // 600m sight => 0.8x... 110m -> 0.64
                    // 1000m sight => 0.3x... 110m -> 0.24
                }
                


                float globalOverlookChance = Mathf.Clamp01(ThatsLitPlugin.GlobalRandomOverlookChance.Value) * disFactor / poseFactor;
                if (canSeeLight) globalOverlookChance /= 2f;
                if (isGoalEnemy)
                {
                    if (sinceSeen < 10f) globalOverlookChance = 0;
                    else globalOverlookChance *= UnityEngine.Random.Range(0.1f, 0.5f);
                }
                if (UnityEngine.Random.Range(0f, 1f) < globalOverlookChance)
                {
                    __result *= 10; // Instead of set it to flat 8888, so if the player has been in the vision for quite some time, this don't block
                    // prone, 110m, about 8% 
                    // prone, 50m, about 1.08%
                    // prone, 10m, 0
                    // stand, 110m, about 0.8% 
                    // stand, 50m, about 0.108%
                    // prone, 10m, 0
                }

                float score, factor;

                if (mainPlayer.disabledLit)
                {
                    score = factor = 0;
                }
                else
                {
                    score = mainPlayer.MultiFrameLitScore; // -1 ~ 1
                    if (score < 0 && __instance.Owner.NightVision.UsingNow) // The score was not reduced (toward 0) for IR lights, process the score here
                    {
                        if (mainPlayer.scoreCalculator.irLight) score /= 2;
                        else if (mainPlayer.scoreCalculator.irLaser) score /= 1.75f;
                        else if (mainPlayer.scoreCalculator.irLightSub) score /= 1.3f;
                        else if (mainPlayer.scoreCalculator.irLaserSub) score /= 1.1f;
                    }

                    factor = Mathf.Pow(score, ThatsLitMainPlayerComponent.POWER); // -1 ~ 1, the graph is basically flat when the score is between ~0.3 and 0.3
                }

                bool nearestAI = false;
                if (dis < nearestRecent)
                {
                    nearestRecent = dis;
                    nearestAI = true;
                    mainPlayer.lastNearest = nearestRecent;
                    if (Time.frameCount % 47 == 46)
                    {
                        mainPlayer.lastCalcFrom = original;
                        mainPlayer.lastScore = score;
                        mainPlayer.lastFactor1 = factor;
                    }
                }

                var foliageImpact = mainPlayer.foliageScore * (1f - factor);
                if (mainPlayer.foliageDir != Vector2.zero) foliageImpact *= 1 - Mathf.Clamp01(Vector2.Angle(new Vector2(-eyeToEnemyBody.x, -eyeToEnemyBody.z), mainPlayer.foliageDir) / 90f); // 0deg -> 1, 90+deg -> 0
                // Maybe randomly lose vision for foliages
                // Pose higher than half will reduce the change
                if (UnityEngine.Random.Range(0f, 1.05f) < Mathf.Clamp01(disFactor * foliageImpact * ThatsLitPlugin.FoliageImpactScale.Value * Mathf.Clamp01(1.2f - poseFactor))) // Among bushes, from afar
                {
                    __result *= 10f;
                }

                float lastPosDis = (__instance.EnemyLastPosition - __instance.Person.Position).magnitude;

                var cqb = 1f - Mathf.Clamp01((dis - 1f) / 5f); // 6+ -> 0, 1f -> 1
                var cqb15m = 1f - Mathf.Clamp01((dis - 1f) / 15f); // 6+ -> 0, 1f -> 1
                // Fix for blind bots who are already touching us

                var cqbSmooth = 1 - Mathf.Clamp01((dis - 1) / 10f); // 11+ -> 0, 1 -> 1, 6 ->0.5
                cqbSmooth *= cqbSmooth; // 6m -> 25%, 1m -> 100%

                var xyFacingFactor = 0f;
                var layingVerticaltInVisionFactor = 0f;
                var detailScore = 0f;
                mainPlayer.CalculateDetailScore(-eyeToEnemyBody, dis, visionAngleDeltaVertical, out float scoreLow, out float scoreMid);
                if (scoreLow > 0.1f || scoreMid > 0.1f)
                {
                    if (isInPronePose) // Deal with player laying on slope and being very visible even with grasses
                    {
                        Vector3 playerLegPos = (mainPlayer.MainPlayer.MainParts[BodyPartType.leftLeg].Position + mainPlayer.MainPlayer.MainParts[BodyPartType.rightLeg].Position) / 2f;
                        var playerLegToHead = mainPlayer.MainPlayer.MainParts[BodyPartType.head].Position - playerLegPos;
                        var playerLegToHeadFlattened = new Vector2(playerLegToHead.x, playerLegToHead.z);
                        var playerLegToBotEye = __instance.Owner.MainParts[BodyPartType.head].Position - playerLegPos;
                        var playerLegToBotEyeFlatted = new Vector2(playerLegToBotEye.x, playerLegToBotEye.z);
                        var facingAngleDelta = Vector2.Angle(playerLegToHeadFlattened, playerLegToBotEyeFlatted); // Close to 90 when the player is facing right or left in the vision
                        if (facingAngleDelta >= 90) xyFacingFactor = (180f - facingAngleDelta) / 90f;
                        else if (facingAngleDelta <= 90) xyFacingFactor = (facingAngleDelta) / 90f;
                        #if DEBUG_DETAILS
                        if (nearestAI) mainPlayer.lastRotateAngle = facingAngleDelta;
                        #endif
                        xyFacingFactor = 1f - xyFacingFactor; // 0 ~ 1

                        // Calculate how flat it is in the vision
                        var normal = Vector3.Cross(BotTransform.up, -playerLegToBotEye);
                        var playerLegToHeadAlongVision = Vector3.ProjectOnPlane(playerLegToHead, normal);
                        layingVerticaltInVisionFactor = Vector3.SignedAngle(playerLegToBotEye, playerLegToHeadAlongVision, normal); // When the angle is 90, it means the player looks straight up in the vision, vice versa for -90.
                        #if DEBUG_DETAILS
                        if (nearestAI)
                            if (layingVerticaltInVisionFactor >= 90f) mainPlayer.lastTiltAngle = (180f - layingVerticaltInVisionFactor);
                            else if (layingVerticaltInVisionFactor <= 0)  mainPlayer.lastTiltAngle = layingVerticaltInVisionFactor;
                        #endif

                        if (layingVerticaltInVisionFactor >= 90f) layingVerticaltInVisionFactor = (180f - layingVerticaltInVisionFactor) / 15f; // the player is laying head up feet down in the vision...   "-> /"
                        else if (layingVerticaltInVisionFactor <= 0 && layingVerticaltInVisionFactor >= -90f) layingVerticaltInVisionFactor = layingVerticaltInVisionFactor / -15f; // "-> /"
                        else layingVerticaltInVisionFactor = 0; // other cases grasses should take effect

                        detailScore = scoreLow * Mathf.Clamp01(1f - layingVerticaltInVisionFactor * xyFacingFactor);
                    }
                    else
                    {
                        detailScore = scoreMid / (poseFactor + 0.1f) * (1f - cqbSmooth) * Mathf.Clamp01(1f - (5f - visionAngleDeltaVertical) / 30f); // nerf when < looking down
                    }


                    detailScore *= 1f + disFactor / 2f; // At 110m+, 1.5x effective
                    if (canSeeLight) detailScore /= 2 - disFactor; // At 110m+, lights does not impact
                    
                    switch (caution)
                    {
                        case 0:
                            detailScore /= 2f;
                            detailScore *= 1f - cqb15m * Mathf.Clamp01((5f - visionAngleDeltaVertical) / 30f);
                            break;
                        case 1:
                        case 3:
                        case 2:
                            detailScore *= 1.5f;
                            detailScore *= 1f - cqb * Mathf.Clamp01((5f - visionAngleDeltaVertical) / 30f);
                            break;
                        case 4:
                        case 5:
                        case 6:
                        case 7:
                        case 8:
                            detailScore *= 1f - cqbSmooth * Mathf.Clamp01((5f - visionAngleDeltaVertical) / 30f);
                            break;
                    }

                    if (UnityEngine.Random.Range(0f, 1.001f) < Mathf.Clamp01(detailScore))
                    {
                        float detailImpact;
                        if (detailScore > 1 && isInPronePose) // But if the score is high and is proning (because the score is not capped to 1 even when crouching), make it "blink" so there's a chance to get hidden again
                        {
                            detailImpact = UnityEngine.Random.Range(2, 4f) + UnityEngine.Random.Range(0, 5f) * Mathf.Clamp01(lastPosDis / (10f * Mathf.Clamp01(1f - disFactor + 0.05f))); // Allow diving back into the grass field
                            immunityNegation = 0.6f;
                        }
                        else detailImpact = 9f * Mathf.Clamp01(lastPosDis / (10f * Mathf.Clamp01(1f - disFactor + 0.05f))); // The closer it is the more the player need to move to gain bonus from grasses, if has been seen;
                        __result *= 1 + detailImpact;
                        if (__result < dis / 10f) __result = dis / 10f;
                        if (nearestAI)
                        {
                            mainPlayer.lastTriggeredDetailCoverDirNearest = -eyeToEnemyBody;
                        }
                    }
                }
                if (nearestAI)
                {
                    mainPlayer.lastFinalDetailScoreNearest = detailScore;
                }

                // BUSH RAT ----------------------------------------------------------------------------------------------------------------
                /// Overlook when the bot has no idea the player is nearby and the player is sitting inside a bush
                if (mainPlayer.foliage != null && !__instance.Owner.Boss.IamBoss
                 && (!__instance.HaveSeen || lastPosDis > 50f || sinceSeen > 300f && lastPosDis > 10f))
                {
                    float angleFactor = 0, foliageDisFactor = 0, poseScale = 0, enemyDisFactor = 0, yDeltaFactor = 1;
                    bool bushRat = true;

                    switch (mainPlayer.foliage)
                    {
                        case "filbert_big01":
                            angleFactor = 1; // works even if looking right at
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.8f) / 0.7f); 
                            enemyDisFactor = Mathf.Clamp01(dis / 2.5f); // 100% at 2.5m+
                            poseScale = 1 - Mathf.Clamp01((poseFactor - 0.45f) / 0.55f); // 100% at crouch
                            yDeltaFactor = 1f - Mathf.Clamp01(-visionAngleDeltaVertical / 60f); // +60deg => 1, 0deg => 1, -30deg => 0.5f, -60deg (looking down) => 0 (this flat bush is not effective against AIs up high)
                            break;
                        case "filbert_big02":
                            angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 20f);
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.5f) / 0.1f); // 0.3 -> 100%, 0.55 -> 0%
                            enemyDisFactor = Mathf.Clamp01(dis / 10f);
                            poseScale = poseFactor == 0.05f? 0.7f : 1f; // 
                            break;
                        case "filbert_big03":
                            angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 30f);
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.25f) / 0.2f); // 0.3 -> 100%, 0.55 -> 0%
                            enemyDisFactor = Mathf.Clamp01(dis / 15f);
                            poseScale = poseFactor == 0.05f? 0 : 0.1f + (poseFactor - 0.45f) / 0.55f * 0.9f; // standing is better with this tall one
                            break;
                        case "filbert_01":
                            angleFactor = 1; 
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.35f) / 0.25f); 
                            enemyDisFactor = Mathf.Clamp01(dis / 12f); // 100% at 2.5m+
                            poseScale = 1 - Mathf.Clamp01((poseFactor - 0.45f) / 0.3f); 
                            break;
                        case "filbert_small01":
                            angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 35f); 
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.15f) / 0.15f); 
                            enemyDisFactor = Mathf.Clamp01(dis / 10f);
                            poseScale = poseFactor == 0.45f? 1f : 0; // crouch (0.45) -> 0%, prone (0.05) -> 100%
                            break;
                        case "filbert_small02":
                            angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 25f); 
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.15f) / 0.15f); 
                            enemyDisFactor = Mathf.Clamp01(dis / 8f);
                            poseScale = poseFactor == 0.45f? 1f : 0; // crouch (0.45) -> 0%, prone (0.05) -> 100%
                            break;
                        case "filbert_small03":
                            angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 40f); 
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.1f) / 0.15f); 
                            enemyDisFactor = Mathf.Clamp01(dis / 10f);
                            poseScale = poseFactor == 0.45f? 1f : 0; // crouch (0.45) -> 0%, prone (0.05) -> 100%
                            break;
                        case "filbert_dry03":
                            angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 30f); 
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.5f) / 0.3f); 
                            enemyDisFactor = Mathf.Clamp01(dis / 30f);
                            poseScale = poseFactor == 0.05f? 0 : 0.1f + (poseFactor - 0.45f) / 0.55f * 0.9f;
                            break;
                        case "bush_dry01":
                            angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 35f); 
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.15f) / 0.15f); 
                            enemyDisFactor = Mathf.Clamp01(dis / 25f);
                            poseScale = poseFactor == 0.45f? 1f : 0; // crouch (0.45) -> 0%, prone (0.05) -> 100%
                            break;
                        case "bush_dry02":
                            angleFactor = 1;
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 1f) / 0.4f); 
                            enemyDisFactor = Mathf.Clamp01(dis / 15f);
                            poseScale = 1 - Mathf.Clamp01((poseFactor - 0.45f) / 0.1f); 
                            yDeltaFactor = 1f - Mathf.Clamp01(-visionAngleDeltaVertical / 60f); // +60deg => 1, -60deg (looking down) => 0 (this flat bush is not effective against AIs up high)
                            break;
                        case "bush_dry03":
                            angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 20f);
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.5f) / 0.3f); // 0.3 -> 100%, 0.55 -> 0%
                            enemyDisFactor = Mathf.Clamp01(dis / 20f);
                            poseScale = poseFactor == 0.05f? 0.6f : 1 - Mathf.Clamp01((poseFactor - 0.45f) / 0.55f); // 100% at crouch
                            break;
                        case "tree02":
                            yDeltaFactor = 0.7f + 0.5f * Mathf.Clamp01((-visionAngleDeltaVertical - 10) / 40f); // bonus against bots up high
                            angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 45f); // 0deg -> 0, 75 deg -> 1
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.5f) / 0.2f); // 0.3 -> 100%, 0.55 -> 0%
                            enemyDisFactor = Mathf.Clamp01(dis * yDeltaFactor / 20f);
                            poseScale = poseFactor == 0.05f? 0 : 0.1f + (poseFactor - 0.45f) / 0.55f * 0.9f; // standing is better with this tall one
                            break;
                        case "pine01":
                            yDeltaFactor = 0.7f + 0.5f * Mathf.Clamp01((-visionAngleDeltaVertical - 10) / 40f); // bonus against bots up high
                            angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 30f); // 0deg -> 0, 75 deg -> 1
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.5f) / 0.35f); // 0.3 -> 100%, 0.55 -> 0%
                            enemyDisFactor = Mathf.Clamp01(dis * yDeltaFactor / 25f);
                            poseScale = poseFactor == 0.05f? 0 : 0.5f + (poseFactor - 0.45f) / 0.55f * 0.5f; // standing is better with this tall one
                            break;
                        case "pine05":
                            angleFactor = 1; // 0deg -> 0, 75 deg -> 1
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.5f) / 0.45f); // 0.3 -> 100%, 0.55 -> 0%
                            enemyDisFactor = Mathf.Clamp01(dis / 20f);
                            poseScale = poseFactor == 0.05f? 0 : 0.5f + (poseFactor - 0.45f) / 0.55f * 0.5f; // standing is better with this tall one
                            yDeltaFactor = Mathf.Clamp01((-visionAngleDeltaVertical - 15) / 45f); // only against bots up high
                            break;
                        case "fern01":
                            angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 25f); // 0deg -> 0, 75 deg -> 1
                            foliageDisFactor = 1f - Mathf.Clamp01((mainPlayer.foliageDisH - 0.1f) / 0.2f); // 0.3 -> 100%, 0.55 -> 0%
                            enemyDisFactor = Mathf.Clamp01(dis / 30f);
                            poseScale = poseFactor == 0.05f? 1f : (1f - poseFactor) / 5f; // very low
                            break;
                        default:
                            bushRat = false;
                            break;
                    }
                    var overallFactor = Mathf.Clamp01(angleFactor * foliageDisFactor * enemyDisFactor * poseScale * yDeltaFactor);
                    if (canSeeLight || (canSeeLaser && UnityEngine.Random.Range(0, 100) < 20)) overallFactor /= 2f;
                    if (nearestAI && overallFactor > 0.05f) mainPlayer.foliageCloaking = bushRat;
                    if (bushRat && overallFactor > 0.01f)
                    {
                        __result = Mathf.Max(__result, dis);
                        switch (caution)
                        {
                            case 0:
                                if (UnityEngine.Random.Range(0f, 1f) > 0.01f) __result *= 1 + 5 * overallFactor * UnityEngine.Random.Range(0.2f, 0.4f);
                                cqb *= 1 - overallFactor * 0.5f;
                                cqbSmooth *= 1 - overallFactor * 0.5f;
                                break;
                            case 1:
                            case 3:
                            case 2:
                                if (UnityEngine.Random.Range(0f, 1f) > 0.005f)__result *= 1 + 6 * overallFactor * UnityEngine.Random.Range(0.3f, 0.65f);
                                cqb *= 1 - overallFactor * 0.8f;
                                cqbSmooth *= 1 - overallFactor * 0.8f;
                                break;
                            case 4:
                            case 5:
                            case 6:
                            case 7:
                            case 8:
                                if (UnityEngine.Random.Range(0f, 1f) > 0.001f)__result *= 1 + 7 * overallFactor * UnityEngine.Random.Range(0.5f, 1.0f);
                                cqb *= 1 - overallFactor;
                                cqbSmooth *= 1 - overallFactor;
                                break;
                        }
                    }
                }
                // BUSH RAT ----------------------------------------------------------------------------------------------------------------

                if (!mainPlayer.disabledLit && Mathf.Abs(score) >= 0.15f) // Skip works
                {
                    if (factor < 0) factor *= 1 + disFactor * (mainPlayer.fog / 0.35f);

                    if (factor < 0) factor *= 1 + disFactor * ((1 - poseFactor) * 0.8f) * (canSeeLight? 0.3f : 1f); // Darkness will be far more effective from afar
                    else if (factor > 0) factor /= 1 + disFactor; // Highlight will be less effective from afar

                    if (factor < -0.85f && __instance.Owner.NightVision.UsingNow)
                        factor *= UnityEngine.Random.Range(0.5f, 0.8f); // Negative factor is reduced to only 10% regardless distance
                    else if (factor < -0.7f && __instance.Owner.NightVision.UsingNow)
                        factor *= UnityEngine.Random.Range(0.25f, 0.45f); // Negative factor is reduced to only 10% regardless distance
                    else if (factor < 0 && __instance.Owner.NightVision.UsingNow)
                        factor *= UnityEngine.Random.Range(0.15f, 0.3f); // Negative factor is reduced to only 10% regardless distance

                    factor = Mathf.Clamp(factor, -0.95f, 0.95f);

                    // Absoulute offset
                    // factor: -0.1 => -0.005~-0.01, factor: -0.2 => -0.02~-0.04, factor: -0.5 => -0.125~-0.25, factor: -1 => 0 ~ -0.5 (1m), -0.5 ~ -1 (6m)
                    // f-1, 1m => 
                    var reducingSeconds = (Mathf.Pow(Mathf.Abs(factor), 2)) * Mathf.Sign(factor) * UnityEngine.Random.Range(0.5f - 0.5f * cqbSmooth, 1f - 0.5f*cqbSmooth);
                    reducingSeconds *= factor < 0 ? 1 : 0.1f; // Give positive factor a smaller offset because the normal values are like 0.15 or something
                    reducingSeconds *= reducingSeconds > 0 ? ThatsLitPlugin.DarknessImpactScale.Value : ThatsLitPlugin.BrightnessImpactScale.Value;
                    __result -= reducingSeconds;

                    // The scaling here allows the player to stay in the dark without being seen
                    // The reason why scaling is needed is because SeenCoef will change dramatically depends on vision angles
                    // Absolute offset alone won't work for different vision angles
                    if (factor < 0)
                    {
                        var cqbCancelChance = Mathf.Clamp01((visionAngleDelta - 15f) / 85f); // 0~15deg (in front) => 0%, 45deg() => 40%, 90deg => 88%
                                                                                             // So even at 1m (cqb = 0), if the AI is facing 45+ deg away, there's a chance cqb check is bypassed
                        float rand = UnityEngine.Random.Range(0f, 1f);
                        rand /= 1f + 0.5f * Mathf.Clamp01(-0.85f - factor) / 0.1f; // 45deg at f-0.95 => 40% -> 26%, 90deg at f-0.95 => 58%
                        var cqbCancel = rand < cqbCancelChance;
                        if (UnityEngine.Random.Range(-1f, 0f) > factor * Mathf.Clamp01(1 - (cqbSmooth + cqb) * (cqbCancel ? 0.1f : 1f))
                         && rand > 0.0001f)
                         __result = 8888f;
                    }
                    else if (factor > 0 && UnityEngine.Random.Range(0, 1) < factor) __result *= (1f - factor * 0.5f * ThatsLitPlugin.BrightnessImpactScale.Value); // Half the reaction time regardles angle half of the time at 100% score
                    else if (factor < -0.9f) __result *= 1 - (factor * (2f - cqb - cqbSmooth) * ThatsLitPlugin.DarknessImpactScale.Value);
                    else if (factor < -0.5f) __result *= 1 - (factor * (1.5f - 0.75f * cqb - 0.75f * cqbSmooth) * ThatsLitPlugin.DarknessImpactScale.Value);
                    else if (factor < -0.2f) __result *= 1 - factor * cqb * ThatsLitPlugin.DarknessImpactScale.Value;
                    else if (factor < 0f) __result *= 1 - factor / 1.5f * ThatsLitPlugin.DarknessImpactScale.Value;
                    else if (factor > 0f) __result /= (1 + factor / 2f * ThatsLitPlugin.BrightnessImpactScale.Value); // 0.66x at 100% score
                }

                if (factor < 0)
                {
                    __result = Mathf.Lerp(__result, original, 1f - Mathf.Clamp01(sinceSeen / UnityEngine.Random.Range(0.075f, 0.15f) + immunityNegation)); // just seen (0s) => original, 0.1s => modified
                }
                // This probably will let bots stay unaffected until losing the visual

                __result += ThatsLitPlugin.FinalOffset.Value;
                if (__result < 0.001f) __result = 0.001f;

                if (Time.frameCount % 47 == 46 && nearestAI)
                {
                    mainPlayer.lastCalcTo = __result;
                    mainPlayer.lastFactor2 = factor;
                }
                mainPlayer.calced++;
                mainPlayer.calcedLastFrame++;
    
            }
        }
    }
}