// #define DEBUG_DETAILS
using Aki.Reflection.Patching;
using EFT;
using HarmonyLib;
using ThatsLit.Components;
using System;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using EFT.InventoryLogic;
using System.Diagnostics;


namespace ThatsLit
{
    public class SeenCoefPatch : ModulePatch
    {
        private static PropertyInfo _enemyRel;
        internal static Stopwatch _benchmarkSW;

        protected override MethodBase GetTargetMethod()
        {
            return ReflectionHelper.FindMethodByArgTypes(typeof(EnemyInfo), new Type[] { typeof(BifacialTransform), typeof(BifacialTransform), typeof(BotDifficultySettingsClass), typeof(AIData), typeof(float), typeof(Vector3) }); ;
        }

        private static float nearestRecent;

        [PatchPostfix]
        public static void PatchPostfix(EnemyInfo __instance, BifacialTransform BotTransform, BifacialTransform enemy, float personalLastSeenTime, Vector3 personalLastSeenPos, ref float __result)
        {
            // if (ThatsLitPlugin.DevMode.Value && ThatsLitPlugin.DevModeInvisible.Value)
            // {
            //     __result = 8888;
            //     return;
            // }
            if (__result == 8888 || !ThatsLitPlugin.EnabledMod.Value || ThatsLitPlugin.FinalImpactScale.Value == 0) return;
            WildSpawnType spawnType = __instance.Owner?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
            BotImpactType botImpactType = Utility.GetBotImpactType(spawnType);
            if ((!ThatsLitPlugin.IncludeBosses.Value && botImpactType == BotImpactType.BOSS)
             || Utility.IsBossNerfExcluded(spawnType)) return;


            ThatsLitMainPlayerComponent mainPlayer = Singleton<ThatsLitMainPlayerComponent>.Instance;
            if (!mainPlayer) return;

            var original = __result;

            if (Time.frameCount % 47 == 0)
            {
                mainPlayer.calcedLastFrame = 0;
                mainPlayer.foliageCloaking = false;
            }

            if (!__instance.Person.IsYourPlayer) return;

#region BENCHMARK
            if (ThatsLitPlugin.EnableBenchmark.Value && ThatsLitPlugin.DebugInfo.Value)
            {
                if (_benchmarkSW == null) _benchmarkSW = new Stopwatch();
                if (_benchmarkSW.IsRunning)
                {
                    string message = $"[That's Lit] Benchmark stopwatch is not stopped! (SeenCoef)";
                    NotificationManagerClass.DisplayWarningNotification(message);
                    Logger.LogWarning(message);
                }
                _benchmarkSW.Start();
            }
            else if (_benchmarkSW != null)
                _benchmarkSW = null;
#endregion

            float pSpeedFactor = Mathf.Clamp01((mainPlayer.MainPlayer.Velocity.magnitude - 1f) / 4f);

            nearestRecent += 0.5f;
            var caution = __instance.Owner.Id % 10; // 0 -> HIGH, 1 -> HIGH-MID, 2,3,4 -> MID, 5,6,7,8,9 -> LOW
            float sinceSeen = Time.time - personalLastSeenTime;
            float lastSeenPosDelta = (__instance.Person.Position - __instance.EnemyLastPosition).magnitude;
            float lastSeenPosDeltaSqr = lastSeenPosDelta * lastSeenPosDelta;
            bool isGoalEnemy = __instance.Owner.Memory.GoalEnemy == __instance;
            float stealthNegation = 0;

            System.Collections.Generic.Dictionary<BodyPartType, EnemyPart> playerParts = mainPlayer.MainPlayer.MainParts;
            Vector3 eyeToPlayerBody = playerParts[BodyPartType.body].Position - __instance.Owner.MainParts[BodyPartType.head].Position;

            var poseFactor = __instance.Person.AIData.Player.PoseLevel / __instance.Person.AIData.Player.Physical.MaxPoseLevel * 0.6f + 0.4f; // crouch: 0.4f
            bool isInPronePose = __instance.Person.AIData.Player.IsInPronePose;
            if (isInPronePose) poseFactor -= 0.4f; // prone: 0
            poseFactor += 0.05f; // base -> prone -> 0.05f, crouch -> 0.45f (Prevent devide by zero)
            poseFactor = Mathf.Clamp01(poseFactor);

            float rand1 = UnityEngine.Random.Range(0f, 1f);
            float rand2 = UnityEngine.Random.Range(0f, 1f);
            float rand3 = UnityEngine.Random.Range(0f, 1f);
            float rand4 = UnityEngine.Random.Range(0f, 1f);

            Vector3 botVisionDir = __instance.Owner.GetPlayer.LookDirection;
            var visionAngleDelta = Vector3.Angle(botVisionDir, eyeToPlayerBody);
            // negative if looking down (from higher pos), 0 when looking straight...
            var visionAngleDeltaVertical = Vector3.Angle(new Vector3(eyeToPlayerBody.x, 0, eyeToPlayerBody.z), eyeToPlayerBody) * (eyeToPlayerBody.y >= 0 ? 1f : -1f); 

            var dis = eyeToPlayerBody.magnitude;
            float disFactor = 0;
            bool inThermalView = false;
            bool inNVGView = false;
            float insideTime = Mathf.Max(0, Time.time - mainPlayer.lastOutside);


            BotNightVisionData nightVision = __instance.Owner.NightVision;
            bool usingNVG = nightVision?.UsingNow ?? false;
            if (usingNVG) // Goggles
            {
                if (nightVision.NightVisionItem?.Template?.Mask == NightVisionComponent.EMask.Thermal) inThermalView = true;
                else if (nightVision.NightVisionItem?.Template?.Mask != null) inNVGView = true;
            }
            else if (UnityEngine.Random.Range((__instance.Owner.Mover?.IsMoving ?? false) ? -4f : -1f, 1f) > Mathf.Clamp01(visionAngleDelta / 15f)) // Scopes
            {
                EFT.InventoryLogic.SightComponent sightMod = __instance.Owner?.GetPlayer?.ProceduralWeaponAnimation?.CurrentAimingMod;
                if (sightMod != null)
                {
                    if (rand1 < 0.1f) sightMod.SetScopeMode(UnityEngine.Random.Range(0, sightMod.ScopesCount), UnityEngine.Random.Range(0, 2));
                    float currentZoom = sightMod.GetCurrentOpticZoom();
                    if (currentZoom == 0) currentZoom = 1;

                    if (visionAngleDelta <= 60f / currentZoom) // Scoped?  (btw AIs using NVGs does not get the scope buff (Realism style)
                    {
                        disFactor = Mathf.Clamp01((dis / currentZoom - 10) / 100f);
                        if (Utility.IsThermalScope(sightMod.Item?.TemplateId, out float effDis) && dis <= effDis)
                            inThermalView = true;
                        else if (Utility.IsNightVisionScope(sightMod.Item?.TemplateId))
                            inNVGView = true;
                    }
                    else if (dis > 10) // Regular
                    {
                        disFactor = Mathf.Clamp01((dis - 10) / 100f);
                    }
                }
                else if (dis > 10) // Regular
                {
                    disFactor = Mathf.Clamp01((dis - 10) / 100f);
                }
            }
            else if (dis > 10) // Regular
            {
                disFactor = Mathf.Clamp01((dis - 10) / 100f);
            }

            if (disFactor > 0)
            {
                // var disFactorLong = Mathf.Clamp01((dis - 10) / 300f);
                // To scale down various sneaking bonus
                // The bigger the distance the bigger it is, capped to 110m
                disFactor = disFactor * disFactor; // A slow accelerating curve, 110m => 1, 10m => 0, 50m => 0.16
                                                   // The disFactor is to scale up effectiveness of various mechanics by distance
                                                   // Once player is seen, it should be suppressed unless the player is out fo visual for sometime, to prevent interrupting long range fight
                disFactor = Mathf.Lerp(0, disFactor, sinceSeen / (8f * (1.2f - disFactor)) / (isGoalEnemy ? 0.33f : 1f)); // Takes 1.6 seconds out of visual for the disFactor to reset for AIs at 110m away, 9.6s for 10m, 8.32s for 50m, if it's targeting the player, 3x the time
                                                                                                                          // disFactorLong = Mathf.Lerp(0, disFactorLong, sinceSeen / (8f * (1.2f - disFactorLong)) / (isGoalEnemy ? 0.33f : 1f)); // Takes 1.6 seconds out of visual for the disFactor to reset for AIs at 110m away, 9.6s for 10m, 8.32s for 50m, if it's targeting the player, 3x the time
            }

            var canSeeLight = mainPlayer.scoreCalculator?.vLight ?? false;
            if (!canSeeLight && inNVGView && (mainPlayer.scoreCalculator?.irLight ?? false)) canSeeLight = true;
            if (visionAngleDelta > 90) canSeeLight = false;
            var canSeeLaser = mainPlayer.scoreCalculator?.vLaser ?? false;
            if (!canSeeLaser && inNVGView && (mainPlayer.scoreCalculator?.irLaser ?? false)) canSeeLaser = true;
            if (visionAngleDelta > 85) canSeeLaser = false;

            // ======
            // Overhead overlooking
            // ======
            if (sinceSeen > 15f && !canSeeLight)
            {
                var weight = Mathf.Pow((Mathf.Clamp01((visionAngleDeltaVertical - 30f) / 75f)), 2) + Mathf.Clamp01((visionAngleDeltaVertical - 15) / 180f);
                // (unscaled) 30deg -> 8%, 45deg->20%, 60deg -> 41%, 75deg->69%, 80deg->80%, 85deg->92%
                // Overlook close enemies at higher attitude and in low pose
                var overheadChance = Mathf.Clamp01(weight) * (1.025f - poseFactor / 2f); // prone: 1.0x, crouch: 0.8x, stand: 0.5x
                overheadChance *= Mathf.Clamp01(lastSeenPosDelta / 15f); // Seen nearby
                overheadChance *= 1 - pSpeedFactor * 0.1f;
                overheadChance = Mathf.Clamp01(overheadChance + (rand3 - 0.5f) * 2f * 0.1f);

                switch (caution)
                {
                    case 0:
                        overheadChance /= 2f;
                        break;
                    case 1:
                        overheadChance /= 1.5f;
                        break;
                    case 2:
                    case 3:
                    case 4:
                        break;
                    case 5:
                    case 6:
                    case 7:
                    case 8:
                    case 9:
                        overheadChance *= 1.2f;
                        break;
                }

                if (rand1 < overheadChance)
                {
                    __result *= 10 + rand2 * 100;
                }

                bool botIsInside = __instance.Owner.AIData.IsInside;
                bool playerIsInside = mainPlayer.MainPlayer.AIData.IsInside;
                if (!botIsInside && playerIsInside && insideTime >= 1)
                {
                    var insideImpact = dis * Mathf.Clamp01(visionAngleDeltaVertical / 40f) * Mathf.Clamp01(visionAngleDelta / 60f) * (isGoalEnemy? 0.05f : 0.5f); // 50m -> 2.5/25 (BEST ANGLE), 10m -> 0.5/5
                    __result *= 1 + insideImpact * (0.75f + rand3 * 0.05f * caution);
                }
            }

            // ======
            // Global random overlooking
            // ======
            float globalOverlookChance = 0.05f * disFactor / poseFactor;
            if (canSeeLight) globalOverlookChance /= 2f;
            if (rand4 < globalOverlookChance * seenPosDeltaFactorSqr * sinceSeenFactorSqr)
            {
                __result *= 10 + rand1 * 10; // Instead of set it to flat 8888, so if the player has been in the vision for quite some time, this don't block
            }

            float score, factor;

            if (mainPlayer.disabledLit)
            {
                score = factor = 0;
            }
            else if (inThermalView)
                score = factor = 1f;
            else
            {
                score = mainPlayer.MultiFrameLitScore; // -1 ~ 1
                if (score < 0 && inNVGView) // IR lights are not accounted in the score, process the score for each bot here
                {
                    if (mainPlayer.scoreCalculator.irLight) score /= 2;
                    else if (mainPlayer.scoreCalculator.irLaser) score /= 1.75f;
                    else if (mainPlayer.scoreCalculator.irLightSub) score /= 1.3f;
                    else if (mainPlayer.scoreCalculator.irLaserSub) score /= 1.1f;
                }

                factor = Mathf.Pow(score, ThatsLitMainPlayerComponent.POWER); // -1 ~ 1, the graph is basically flat when the score is between ~0.3 and 0.3
            }

            bool nearestAI = false;
            if (dis <= nearestRecent)
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
            Vector2 bestMatchFoliageDir = Vector2.zero;
            float bestMatchDeg = 360f;
            for (int i = 0; i < Math.Min(ThatsLitPlugin.FoliageSamples.Value, mainPlayer.foliageCount); i++) {
                var f = mainPlayer.foliage[i];
                if (f == default) break;
                var fDeg = Vector2.Angle(new Vector2(-eyeToPlayerBody.x, -eyeToPlayerBody.z), f.dir);
                if (fDeg < bestMatchDeg)
                {
                    bestMatchDeg = fDeg;
                    bestMatchFoliageDir = f.dir;
                }
            }
            if (bestMatchFoliageDir != Vector2.zero) foliageImpact *= 1 - Mathf.Clamp01(bestMatchDeg / 90f); // 0deg -> 1, 90+deg -> 0
                                                                                                             // Maybe randomly lose vision for foliages
            float foliageBlindChance = Mathf.Clamp01(
                                            disFactor // Mainly works for far away enemies
                                            * foliageImpact
                                            * ThatsLitPlugin.FoliageImpactScale.Value
                                            * Mathf.Clamp01(1.35f - poseFactor)); // Lower chance for higher poses
            if (UnityEngine.Random.Range(0f, 1.05f) < foliageBlindChance) // Among bushes, from afar, always at least 5% to be uneffective
            {
                __result *= 1 + rand4 * (10f - caution * 0.5f);
                __result += rand2 + rand3 * 2f * disFactor;
            }

            var cqb5m = 1f - Mathf.Clamp01((dis - 1f) / 5f); // 6+ -> 0, 1f -> 1
            var cqb15m = 1f - Mathf.Clamp01((dis - 1f) / 15f); // 6+ -> 0, 1f -> 1                                                               // Fix for blind bots who are already touching us

            var cqb10mSquared = 1 - Mathf.Clamp01((dis - 1) / 10f); // 11+ -> 0, 1 -> 1, 6 ->0.5
            cqb10mSquared *= cqb10mSquared; // 6m -> 25%, 1m -> 100%

            // Scale down cqb factors for AIs facing away
            cqb5m *=  Mathf.Clamp01((90f - visionAngleDelta) / 90f);
            cqb15m *=  Mathf.Clamp01((90f - visionAngleDelta) / 90f);
            cqb10mSquared *=  Mathf.Clamp01((90f - visionAngleDelta) / 90f);

            var xyFacingFactor = 0f;
            var layingVerticaltInVisionFactor = 0f;
            var detailScore = 0f;
            var detailScoreRaw = 0f;
            if (!inThermalView && !mainPlayer.skipDetailCheck)
            {
                mainPlayer.CalculateDetailScore(-eyeToPlayerBody, dis, visionAngleDeltaVertical, out float terrainScoreProne, out float terrainScoreCrouch);
                if (terrainScoreProne > 0.1f || terrainScoreCrouch > 0.1f)
                {
                    if (isInPronePose) // Handles cases where the player is laying on slopes and being very visible even with grasses
                    {
                        Vector3 playerLegPos = (playerParts[BodyPartType.leftLeg].Position + playerParts[BodyPartType.rightLeg].Position) / 2f;
                        var playerLegToHead = playerParts[BodyPartType.head].Position - playerLegPos;
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

                        detailScore = terrainScoreProne * Mathf.Clamp01(1f - layingVerticaltInVisionFactor * xyFacingFactor);
                    }
                    else
                    {
                        detailScore = terrainScoreCrouch / (poseFactor + 0.1f + 0.25f * (poseFactor-0.45f)/0.55f) * (1f - cqb10mSquared) * Mathf.Clamp01(1f - (5f - visionAngleDeltaVertical) / 30f); // nerf when < looking down
                    }

                    detailScoreRaw = detailScore;
                    detailScore *= 1f + disFactor / 2f; // At 110m+, 1.5x effective
                    if (canSeeLight) detailScore /= 2f - disFactor; // Flashlights impact less from afar

                    switch (caution)
                    {
                        case 0:
                            detailScore /= 2f;
                            detailScore *= 1f - cqb15m * Mathf.Clamp01((5f - visionAngleDeltaVertical) / 30f); // nerf starting from looking 5deg up to down (scaled by down to -25deg) and scaled by dis 15 ~ 0
                            break;
                        case 1:
                            detailScore /= 1.5f;
                            detailScore *= 1f - cqb15m * Mathf.Clamp01((5f - visionAngleDeltaVertical) / 40f); // nerf starting from looking 5deg up (scaled by down to -40deg) and scaled by dis 15 ~ 0
                            break;
                        case 2:
                        case 3:
                        case 4:
                            detailScore *= 1f - cqb10mSquared * Mathf.Clamp01((5f - visionAngleDeltaVertical) / 40f);
                            break;
                        case 5:
                        case 6:
                        case 7:
                        case 8:
                        case 9:
                            detailScore *= 1.2f;
                            detailScore *= 1f - cqb5m * Mathf.Clamp01((5f - visionAngleDeltaVertical) / 50f); // nerf starting from looking 5deg up (scaled by down to -50deg) and scaled by dis 5 ~ 0
                            break;
                    }

                    // Applying terrain detail stealth
                    // 0.1% chance does not work (very low because bots can track after spotting)
                    if (UnityEngine.Random.Range(0f, 1.001f) < Mathf.Clamp01(detailScore))
                    {
                        float detailImpact;
                        if (detailScore > 1 && isInPronePose) // But if the score is high and is proning (because the score is not capped to 1 even when crouching), make it "blink" so there's a chance to get hidden again
                        {
                            detailImpact = UnityEngine.Random.Range(2, 4f) + UnityEngine.Random.Range(0, 5f) * Mathf.Clamp01(lastSeenPosDelta / (10f * Mathf.Clamp01(1f - disFactor + 0.05f))); // Allow diving back into the grass field
                            stealthNegation = 0.6f;
                        }
                        else detailImpact = 9f * Mathf.Clamp01(lastSeenPosDelta / (10f * Mathf.Clamp01(1f - disFactor + 0.05f))); // The closer it is the more the player need to move to gain bonus from grasses, if has been seen;
                        __result *= 1 + detailImpact;
                        if (__result < dis / 10f) __result = dis / 10f;
                        if (nearestAI)
                        {
                            mainPlayer.lastTriggeredDetailCoverDirNearest = -eyeToPlayerBody;
                        }
                    }
                }
                if (nearestAI)
                {
                    mainPlayer.lastFinalDetailScoreNearest = detailScore;
                    mainPlayer.lastDisFactorNearest = disFactor;
                }
            }

            // BUSH RAT ----------------------------------------------------------------------------------------------------------------
            /// Overlook when the bot has no idea the player is nearby and the player is sitting inside a bush
            if (!inThermalView && mainPlayer.foliage != null && botImpactType != BotImpactType.BOSS
             && (!__instance.HaveSeen || lastSeenPosDelta > 30f + rand1 * 20f || sinceSeen > 150f + 150f*rand3 && lastSeenPosDelta > 10f + 10f*rand2))
            {
                float angleFactor = 0, foliageDisFactor = 0, poseScale = 0, enemyDisFactor = 0, yDeltaFactor = 1;
                bool bushRat = true;

                FoliageInfo nearestFoliage = mainPlayer.foliage[0];
                switch (nearestFoliage.name)
                {
                    case "filbert_big01":
                        angleFactor = 1; // works even if looking right at
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.8f) / 0.7f);
                        enemyDisFactor = Mathf.Clamp01(dis / 2.5f); // 100% at 2.5m+
                        poseScale = 1 - Mathf.Clamp01((poseFactor - 0.45f) / 0.55f); // 100% at crouch
                        yDeltaFactor = 1f - Mathf.Clamp01(-visionAngleDeltaVertical / 60f); // +60deg => 1, 0deg => 1, -30deg => 0.5f, -60deg (looking down) => 0 (this flat bush is not effective against AIs up high)
                        break;
                    case "filbert_big02":
                        angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 20f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.1f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 10f);
                        poseScale = poseFactor == 0.05f ? 0.7f : 1f; // 
                        break;
                    case "filbert_big03":
                        angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 30f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.25f) / 0.2f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 15f);
                        poseScale = poseFactor == 0.05f ? 0 : 0.1f + (poseFactor - 0.45f) / 0.55f * 0.9f; // standing is better with this tall one
                        break;
                    case "filbert_01":
                        angleFactor = 1;
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.35f) / 0.25f);
                        enemyDisFactor = Mathf.Clamp01(dis / 12f); // 100% at 2.5m+
                        poseScale = 1 - Mathf.Clamp01((poseFactor - 0.45f) / 0.3f);
                        break;
                    case "filbert_small01":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 35f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.15f) / 0.15f);
                        enemyDisFactor = Mathf.Clamp01(dis / 10f);
                        poseScale = poseFactor == 0.45f ? 1f : 0;
                        break;
                    case "filbert_small02":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 25f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.15f) / 0.15f);
                        enemyDisFactor = Mathf.Clamp01(dis / 8f);
                        poseScale = poseFactor == 0.45f ? 1f : 0;
                        break;
                    case "filbert_small03":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 40f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.1f) / 0.15f);
                        enemyDisFactor = Mathf.Clamp01(dis / 10f);
                        poseScale = poseFactor == 0.45f ? 1f : 0;
                        break;
                    case "filbert_dry03":
                        angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 30f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.3f);
                        enemyDisFactor = Mathf.Clamp01(dis / 30f);
                        poseScale = poseFactor == 0.05f ? 0 : 0.1f + (poseFactor - 0.45f) / 0.55f * 0.9f;
                        break;
                    case "fibert_hedge01":
                        angleFactor = Mathf.Clamp01(visionAngleDelta / 40f);
                        foliageDisFactor = (1f - Mathf.Clamp01((nearestFoliage.dis - 0.1f) / 0.1f)) * (1f - Mathf.Clamp01(nearestFoliage.dis / 0.2f));
                        enemyDisFactor = Mathf.Clamp01(dis / 30f);
                        poseScale = poseFactor == 0.45f ? 1f : 0; // Too narrow for proning
                        break;
                    case "fibert_hedge02":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 40f);
                        foliageDisFactor = (1f - Mathf.Clamp01((nearestFoliage.dis - 0.1f) / 0.2f)) * (1f - Mathf.Clamp01(nearestFoliage.dis / 0.3f));
                        enemyDisFactor = Mathf.Clamp01(dis / 20f);
                        poseScale = poseFactor == 0.45f ? 1f : 0; // Too narrow for proning
                        break;
                    case "privet_hedge":
                    case "privet_hedge_2":
                        angleFactor = Mathf.Clamp01((visionAngleDelta - 30f) / 60f);
                        foliageDisFactor = (1f - Mathf.Clamp01(nearestFoliage.dis / 1f)) * (1f - Mathf.Clamp01(nearestFoliage.dis / 0.3f));
                        enemyDisFactor = Mathf.Clamp01(dis / 50f);
                        poseScale = poseFactor < 0.45f ? 1f : 0; // Prone only
                        break;
                    case "bush_dry01":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 35f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.15f) / 0.15f);
                        enemyDisFactor = Mathf.Clamp01(dis / 25f);
                        poseScale = poseFactor == 0.45f ? 1f : 0;
                        break;
                    case "bush_dry02":
                        angleFactor = 1;
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 1f) / 0.4f);
                        enemyDisFactor = Mathf.Clamp01(dis / 15f);
                        poseScale = 1 - Mathf.Clamp01((poseFactor - 0.45f) / 0.1f);
                        yDeltaFactor = 1f - Mathf.Clamp01(-visionAngleDeltaVertical / 60f); // +60deg => 1, -60deg (looking down) => 0 (this flat bush is not effective against AIs up high)
                        break;
                    case "bush_dry03":
                        angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 20f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.3f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 20f);
                        poseScale = poseFactor == 0.05f ? 0.6f : 1 - Mathf.Clamp01((poseFactor - 0.45f) / 0.55f); // 100% at crouch
                        break;
                    case "tree02":
                        yDeltaFactor = 0.7f + 0.5f * Mathf.Clamp01((-visionAngleDeltaVertical - 10) / 40f); // bonus against bots up high
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 45f); // 0deg -> 0, 75 deg -> 1
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.2f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis * yDeltaFactor / 20f);
                        poseScale = poseFactor == 0.05f ? 0 : 0.1f + (poseFactor - 0.45f) / 0.55f * 0.9f; // standing is better with this tall one
                        break;
                    case "pine01":
                        yDeltaFactor = 0.7f + 0.5f * Mathf.Clamp01((-visionAngleDeltaVertical - 10) / 40f); // bonus against bots up high
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 30f); // 0deg -> 0, 75 deg -> 1
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.35f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis * yDeltaFactor / 25f);
                        poseScale = poseFactor == 0.05f ? 0 : 0.5f + (poseFactor - 0.45f) / 0.55f * 0.5f; // standing is better with this tall one
                        break;
                    case "pine05":
                        angleFactor = 1; // 0deg -> 0, 75 deg -> 1
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.45f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 20f);
                        poseScale = poseFactor == 0.05f ? 0 : 0.5f + (poseFactor - 0.45f) / 0.55f * 0.5f; // standing is better with this tall one
                        yDeltaFactor = Mathf.Clamp01((-visionAngleDeltaVertical - 15) / 45f); // only against bots up high
                        break;
                    case "fern01":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 25f); // 0deg -> 0, 75 deg -> 1
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.1f) / 0.2f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 30f);
                        poseScale = poseFactor == 0.05f ? 1f : (1f - poseFactor) / 5f; // very low
                        break;
                    default:
                        bushRat = false;
                        break;
                }
                var bushRatFactor = Mathf.Clamp01(angleFactor * foliageDisFactor * enemyDisFactor * poseScale * yDeltaFactor);
                if (botImpactType == BotImpactType.FOLLOWER || canSeeLight || (canSeeLaser && rand3 < 0.2f)) bushRatFactor /= 2f;
                if (bushRat && bushRatFactor > 0.01f)
                {
                    if (nearestAI) mainPlayer.foliageCloaking = bushRat;
                    __result = Mathf.Max(__result, dis);
                    switch (caution)
                    {
                        case 0:
                        case 1:
                            if (rand2 > 0.01f) __result *= 1 + 4 * bushRatFactor * UnityEngine.Random.Range(0.2f, 0.4f);
                            cqb5m *= 1f - bushRatFactor * 0.5f;
                            cqb10mSquared *= 1f - bushRatFactor * 0.5f;
                            break;
                        case 2:
                        case 3:
                        case 4:
                            if (rand3 > 0.005f) __result *= 1 + 8 * bushRatFactor * UnityEngine.Random.Range(0.3f, 0.65f);
                            cqb5m *= 1f - bushRatFactor * 0.8f;
                            cqb10mSquared *= 1f - bushRatFactor * 0.8f;
                            break;
                        case 5:
                        case 6:
                        case 7:
                        case 8:
                        case 9:
                            if (rand1 > 0.001f) __result *= 1 + 6 * bushRatFactor * UnityEngine.Random.Range(0.5f, 1.0f);
                            cqb5m *= 1f - bushRatFactor;
                            cqb10mSquared *= 1f - bushRatFactor;
                            break;
                    }
                }
            }
            // BUSH RAT ----------------------------------------------------------------------------------------------------------------

            if (mainPlayer.disabledLit && ThatsLitPlugin.AlternativeReactionFluctuation.Value)
            {
                // https://www.desmos.com/calculator/jbghqfxwha
                float cautionFactor = (caution / 9f - 0.5f) * (0.05f + 0.5f * rand4 * rand4); // -0.5(faster)~0.5(slower) squared curve distribution
                __result += cautionFactor;
                __result *= 1f + cautionFactor / 2f; // Factor in bot class
            }
            else if (!mainPlayer.disabledLit && Mathf.Abs(score) >= 0.05f) // Skip works
            {
                if (mainPlayer.isWinterCache) {
                    var emptiness = 1f - mainPlayer.foliageScore * detailScoreRaw;
                    emptiness *= 1f - insideTime;
                    disFactor *= 0.65f + 0.35f * emptiness; // When player outside is not surrounded by anything in winter, lose dis buff
                }

                if (factor < 0) factor *= 1 + disFactor * Mathf.Clamp01(1.2f - poseFactor) * (canSeeLight ? 0.2f : 1f) * (canSeeLaser ? 0.9f : 1f); // Darkness will be far more effective from afar
                else if (factor > 0) factor /= 1 + disFactor; // Highlight will be less effective from afar

                if (factor < 0 && inNVGView)
                {
                    if (factor < -0.85f)
                        factor *= UnityEngine.Random.Range(0.4f, 0.75f); // It's really dark, slightly scale down
                    else if (factor < -0.7f)
                        factor *= UnityEngine.Random.Range(0.25f, 0.45f); // It's quite dark, scale down
                    else if (factor < 0)
                        factor *= 0.1f; // It's not really that dark, scale down massively
                }

                factor = Mathf.Clamp(factor, -0.975f, 0.975f);

                // Absoulute offset
                // f-0.1 => -0.005~-0.01, factor: -0.2 => -0.02~-0.04, factor: -0.5 => -0.125~-0.25, factor: -1 => 0 ~ -0.5 (1m), -0.5 ~ -1 (10m)
                var secondsOffset = Mathf.Pow(factor, 2) * Mathf.Sign(factor) * UnityEngine.Random.Range(0.5f - 0.5f * cqb10mSquared, 1f - 0.5f * cqb10mSquared);
                secondsOffset *= -1; // Inversed (negative value now delay the visual)
                secondsOffset *= factor > 0 ? ThatsLitPlugin.BrightnessImpactScale.Value : ThatsLitPlugin.DarknessImpactScale.Value;
                __result += secondsOffset;
                if (__result < 0) __result = 0;


                // The scaling here allows the player to stay in the dark without being seen
                // The reason why scaling is needed is because SeenCoef will change dramatically depends on vision angles
                // Absolute offset alone won't work for different vision angles
                if (factor < 0)
                {
                    // At cqb range, nullify the forced stealth belowa accoring to vision angle
                    var cqbCancelChance = Mathf.Clamp01((visionAngleDelta - 15f) / 85f); // 0~15deg (in front) => 0%, 45deg() => 40%, 90deg => 88%
                                                                                         // If the AI is facing 15+ deg away, there's a chance cqb check is bypassed

                    float combinedCqb10 = cqb10mSquared * (0.7f + 0.3f * rand2 * poseFactor) + cqb5m; // at 5m => 0.25 + 0, at 3m => 0.49+0.4, at 1m => 0.81+0.8, at 0m => 1+1
                    combinedCqb10 *= 0.9f + 0.4f * pSpeedFactor;

                    float cancel = UnityEngine.Random.Range(0f, 1f);
                    cancel /= 1f + 0.5f * Mathf.Clamp01(-0.8f - factor) / 0.15f; // -0.8 ~ -0.95f -> At most reduce 33% chance to cancel cqb stealth nullification
                    // 45deg at f-0.95 => 40% -> 26%, 90deg at f-0.95 => 58%
                    var cqbCancel = cancel < cqbCancelChance;

                    // Roll a force stealth
                    if (UnityEngine.Random.Range(-1f, 0f) > factor * Mathf.Clamp01(1f - combinedCqb10 * (cqbCancel ? 0.1f : 1f))) // At 3m, the chance of force stealth is 0.11 or 0.911 (cqb nullification cancelled)
                    {
                        __result *= 100;
                    }
                    else if (factor < -0.85f)  __result *= 1f - (factor * (2f - combinedCqb10) * ThatsLitPlugin.DarknessImpactScale.Value); // -0.9f, ccqb:0 => 2.8x / -0.85f, ccqb:0 => 2.7x
                    else if (factor < -0.6f)   __result *= 1f - (factor * (2f - combinedCqb10) * 0.8f * ThatsLitPlugin.DarknessImpactScale.Value); // -0.6f, ccqb:0 => 1.96x / -0.85f, ccqb:0 => 2.36x
                    else if (factor < -0.4f)   __result *= 1f - (factor * (2f - combinedCqb10) * 0.6f * ThatsLitPlugin.DarknessImpactScale.Value); // -0.4f, ccqb:0 => 1.48x / -0.6f => 1.72f
                    else if (factor < -0.2f)   __result *= 1f - (factor * (2 - combinedCqb10) * 0.5f * ThatsLitPlugin.DarknessImpactScale.Value); // -0.2f, cqb5:0 => 1.2x / -0.4f, cqb5:0 => 1.4x
                    else if (factor < 0f)      __result *= 1f - (factor / 1.5f) * ThatsLitPlugin.DarknessImpactScale.Value; // -0.2f => 1.13x

                }
                else if (factor > 0 && UnityEngine.Random.Range(0, 1) < factor * 0.9f) __result *= (1f - factor * 0.34f * ThatsLitPlugin.BrightnessImpactScale.Value); // At 100% brightness, 90% 0.66x the reaction time regardles angle half of the time
                else if (factor > 0f) __result /= 1f + Mathf.Clamp01((factor / 5f) * ThatsLitPlugin.BrightnessImpactScale.Value);
            }

            // Vanilla is multiplying the final SeenCoef with 1E-05
            // Probably to guarantee the continuance of the bot attention
            // However this includes situations that the player has moved at least a bit and the bot is running/facing side/away
            // This part, in a very conservative way, tries to randomly delay the reaction
            if (sinceSeen < __instance.Owner.Settings.FileSettings.Look.SEC_REPEATED_SEEN
                && lastSeenPosDeltaSqr < __instance.Owner.Settings.FileSettings.Look.DIST_SQRT_REPEATED_SEEN
                && __result < 0.5f)
            {
                __result += (0.5f - __result)
                            * (rand1 * Mathf.Clamp01(visionAngleDelta / 90f)) // Scale-capped by horizontal vision angle delta
                            * (rand3 * Mathf.Clamp01(lastSeenPosDelta / 5f)) // Scale-capped by player position delta to last
                            * (__instance.Owner.Mover.Sprinting? 1f : 0.75f); 
            }

            if (ThatsLitPlugin.EnableMovementImpact.Value)
            {
                if (__instance.Owner.Mover.Sprinting)
                    __result *= 1 + (rand2 / (4f - caution * 0.1f)) * Mathf.Clamp01((visionAngleDelta - 30f) / 60f); // When facing away (30~60deg), sprinting bots takes up to 25% longer to spot the player
                else if (!__instance.Owner.Mover.IsMoving)
                {
                    float delta = __result * (rand4 / (5f + caution * 0.1f)); // When not moving, bots takes up to 20% shorter to spot the player
                    __result -= delta;
                }

                if (pSpeedFactor > 0.01f)
                {
                    float delta = __result * (rand2 / (5f + caution * 0.1f)) * pSpeedFactor * Mathf.Clamp01((score - -1f) / 0.35f) * Mathf.Clamp01(poseFactor); // When the score is -0.7+, bots takes up to 20% shorter to spot the player according to player movement speed (when not proning);
                    __result -= delta;
                }
            }


            __result = Mathf.Lerp(original, __result, ThatsLitPlugin.FinalImpactScale.Value); // just seen (0s) => original, 0
            __result = Mathf.Lerp(__result, original, botImpactType == BotImpactType.DEFAULT? 0f : 0.5f);

            if (__result > original) // That's Lit delaying the bot
            {
                // In ~0.2s after being seen, stealth is nullfied (fading between 0.1~0.2)
                float lerp = 1f - Mathf.Clamp01(sinceSeen - 0.1f / UnityEngine.Random.Range(0.01f, 0.1f)) - stealthNegation;
                __result = Mathf.Lerp(__result, original, Mathf.Clamp01(lerp)); // just seen (0s) => original, 0.1s => modified
            }
            // This probably will let bots stay unaffected until losing the visual.1s => modified

            // Up to 50% penalty
            if (__result < 0.5f * original)
            {
                __result = 0.5f * original;
            }

            __result += ThatsLitPlugin.FinalOffset.Value;
            if (__result < 0.005f) __result = 0.005f;

            if (Time.frameCount % 47 == 46 && nearestAI)
            {
                mainPlayer.lastCalcTo = __result;
                mainPlayer.lastFactor2 = factor;
                mainPlayer.rawTerrainScoreSample = detailScoreRaw;
            }
            mainPlayer.calced++;
            mainPlayer.calcedLastFrame++;

#region BENCHMARK
            _benchmarkSW?.Stop();
#endregion
        }
    }
}