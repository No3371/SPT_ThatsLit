#define DEBUG_DETAILS
using Aki.Reflection.Patching;
using EFT;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using EFT.InventoryLogic;
using System.Diagnostics;
using EFT.HealthSystem;


namespace ThatsLit
{
    public class SeenCoefPatch : ModulePatch
    {
        private static PropertyInfo _enemyRel;

        protected override MethodBase GetTargetMethod()
        {
            return ReflectionHelper.FindMethodByArgTypes(typeof(EnemyInfo), new Type[] { typeof(BifacialTransform), typeof(BifacialTransform), typeof(BotDifficultySettingsClass), typeof(AIData), typeof(float), typeof(Vector3) }); ;
        }

        private static float nearestRecent;

        [PatchPostfix]
        public static void PatchPostfix(EnemyInfo __instance, BifacialTransform BotTransform, BifacialTransform enemy, float personalLastSeenTime, Vector3 personalLastSeenPos, ref float __result)
        {
            // Don't use GoalEnemy here because it only change when engaging new enemy (it'll stay forever if not engaged with new enemy)
            // Also they could search without having visual?

            if (__result == 8888 || !ThatsLitPlugin.EnabledMod.Value || ThatsLitPlugin.FinalImpactScale.Value == 0) return;

            WildSpawnType spawnType = __instance.Owner?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
            BotImpactType botImpactType = Utility.GetBotImpactType(spawnType);
            if ((!ThatsLitPlugin.IncludeBosses.Value && botImpactType == BotImpactType.BOSS)
             || Utility.IsBossNerfExcluded(spawnType)) return;

            ThatsLitMainPlayerComponent player = null;
            if (Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(__instance.Person, out player) != true
             || player == null
             || player.Player == null)
                return;

            var original = __result;

            if (player.DebugInfo != null && ThatsLitMainPlayerComponent.IsDebugSampleFrame)
            {
                player.DebugInfo.calcedLastFrame = 0;
                player.DebugInfo.IsBushRatting = false;
            }

            ThatsLitPlugin.swSeenCoef.MaybeResume();

            float pSpeedFactor = Mathf.Clamp01((player.Player.Velocity.magnitude - 1f) / 4f);

            nearestRecent += 0.5f;
            var caution = __instance.Owner.Id % 10; // 0 -> HIGH, 1 -> HIGH-MID, 2,3,4 -> MID, 5,6,7,8,9 -> LOW
            float sinceSeen = Time.time - personalLastSeenTime;
            float lastSeenPosDelta = (__instance.Person.Position - __instance.EnemyLastPosition).magnitude;
            float lastSeenPosDeltaSqr = lastSeenPosDelta * lastSeenPosDelta;

            var sinceSeenFactorSqr = Mathf.Clamp01(sinceSeen / __instance.Owner.Settings.FileSettings.Look.SEC_REPEATED_SEEN);
            var sinceSeenFactorSqrSlow = Mathf.Clamp01(sinceSeen / __instance.Owner.Settings.FileSettings.Look.SEC_REPEATED_SEEN * 2f);
            var seenPosDeltaFactorSqr = Mathf.Clamp01((float) (lastSeenPosDelta / __instance.Owner.Settings.FileSettings.Look.DIST_REPEATED_SEEN / 4f));
            sinceSeenFactorSqr = sinceSeenFactorSqr * sinceSeenFactorSqr;
            sinceSeenFactorSqrSlow = sinceSeenFactorSqrSlow * sinceSeenFactorSqrSlow;
            seenPosDeltaFactorSqr = seenPosDeltaFactorSqr * seenPosDeltaFactorSqr;

            float notSeenRecentAndNear = Mathf.Clamp01(seenPosDeltaFactorSqr + sinceSeenFactorSqrSlow) + sinceSeenFactorSqr / 3f;

            float deNullification = 0;

            System.Collections.Generic.Dictionary<BodyPartType, EnemyPart> playerParts = player.Player.MainParts;
            Vector3 eyeToPlayerBody = playerParts[BodyPartType.body].Position - __instance.Owner.MainParts[BodyPartType.head].Position;

            var pPoseFactor = __instance.Person.AIData.Player.PoseLevel / __instance.Person.AIData.Player.Physical.MaxPoseLevel * 0.6f + 0.4f; // crouch: 0.4f
            bool isInPronePose = __instance.Person.AIData.Player.IsInPronePose;
            if (isInPronePose) pPoseFactor -= 0.4f; // prone: 0
            pPoseFactor += 0.05f; // base -> prone -> 0.05f, crouch -> 0.45f (Prevent devide by zero)
            pPoseFactor = Mathf.Clamp01(pPoseFactor);

            float rand1 = UnityEngine.Random.Range(0f, 1f);
            float rand2 = UnityEngine.Random.Range(0f, 1f);
            float rand3 = UnityEngine.Random.Range(0f, 1f);
            float rand4 = UnityEngine.Random.Range(0f, 1f);
            float rand5 = UnityEngine.Random.Range(0f, 1f);

            Vector3 botVisionDir = __instance.Owner.GetPlayer.LookDirection;
            var visionAngleDelta = Vector3.Angle(botVisionDir, eyeToPlayerBody);
            var visionAngleDelta90Clamped = Mathf.InverseLerp(0, 90, visionAngleDelta);
            var visionAngleDeltaHorizontal = Vector3.Angle(new Vector3(botVisionDir.x, 0, botVisionDir.z), new Vector3(eyeToPlayerBody.x, 0, eyeToPlayerBody.z));
            // negative if looking down (from higher pos), 0 when looking straight...
            var visionAngleDeltaVertical = Vector3.Angle(new Vector3(eyeToPlayerBody.x, 0, eyeToPlayerBody.z), eyeToPlayerBody); 
            var visionAngleDeltaVerticalSigned = visionAngleDeltaVertical * (eyeToPlayerBody.y >= 0 ? 1f : -1f); 

            var dis = eyeToPlayerBody.magnitude;
            float disFactor = Mathf.Clamp01((dis - 10) / 100f);
            float disFactorSmooth = 0;
            bool inThermalView = false;
            bool inNVGView = false;
            bool gearBlocking = false; // Not blokcing for now, because AIs don't look around (nvg/thermal still ineffective when out of FOV)
            float insideTime = Mathf.Max(0, Time.time - player.lastOutside);

            ThatsLitCompat.ScopeTemplate activeScope = null;
            BotNightVisionData nightVision = __instance.Owner.NightVision;
            ThatsLitCompat.GoggleTemplate activeGoggle = null;
            if (nightVision?.UsingNow == true) 
                if (ThatsLitCompat.Goggles.TryGetValue(nightVision.NightVisionItem.Item.TemplateId, out var goggle))
                    activeGoggle = goggle?.TemplateInstance;
            if (activeGoggle != null) 
            {
                if (nightVision.NightVisionItem?.Template?.Mask == NightVisionComponent.EMask.Thermal
                 && activeGoggle.thermal != null
                 && activeGoggle.thermal.effectiveDistance > dis)
                 {
                    if (activeGoggle.thermal.verticalFOV > visionAngleDeltaVertical
                     && activeGoggle.thermal.horizontalFOV > visionAngleDeltaHorizontal)
                    {
                        inThermalView = true;
                    }
                    else
                    {
                        gearBlocking = true;
                    }
                 }
                else if (nightVision.NightVisionItem?.Template?.Mask != NightVisionComponent.EMask.Thermal
                      && activeGoggle.nightVision != null)
                {
                    if (activeGoggle.nightVision.verticalFOV > visionAngleDeltaVertical
                     && activeGoggle.nightVision.horizontalFOV > visionAngleDeltaHorizontal)
                    {
                        inNVGView = true;
                    }
                    else
                    {
                        gearBlocking = true;
                    }
                }
            }
            else if (UnityEngine.Random.Range((__instance.Owner.Mover?.IsMoving ?? false) ? -4f : -1f, 1f) > Mathf.Clamp01(visionAngleDelta / 15f)) // ADS
            {
                EFT.InventoryLogic.SightComponent sightMod = __instance.Owner?.GetPlayer?.ProceduralWeaponAnimation?.CurrentAimingMod;
                if (sightMod != null)
                    if (ThatsLitCompat.Scopes.TryGetValue(sightMod.Item.TemplateId, out var scope))
                        activeScope = scope?.TemplateInstance;
                if (activeScope != null) {
                    if (rand1 < 0.1f) sightMod.SetScopeMode(UnityEngine.Random.Range(0, sightMod.ScopesCount), UnityEngine.Random.Range(0, 2));
                    float currentZoom = sightMod.GetCurrentOpticZoom();
                    if (currentZoom == 0) currentZoom = 1;

                    if (visionAngleDelta <= 60f / currentZoom) // In scope fov
                    {
                        disFactor = Mathf.Clamp01((dis / currentZoom - 10) / 100f);
                        if (activeScope?.thermal != null  && dis <= activeScope.thermal.effectiveDistance)
                        {
                            inThermalView = true;
                        }
                        else if (activeScope?.nightVision != null)
                            inNVGView = true;
                    }
                }
            }

            if (disFactor > 0)
            {
                // var disFactorLong = Mathf.Clamp01((dis - 10) / 300f);
                // To scale down various sneaking bonus
                // The bigger the distance the bigger it is, capped to 110m
                disFactorSmooth = (disFactor + disFactor * disFactor) * 0.5f; // 0.25df => 0.156dfs / 0.5df => 0.325dfs / 0.75df => 0.656dfs
                disFactor = disFactor * disFactor; // A slow accelerating curve, 110m => 1, 10m => 0, 50m => 0.16
                                                   // The disFactor is to scale up effectiveness of various mechanics by distance
                                                   // Once player is seen, it should be suppressed unless the player is out fo visual for sometime, to prevent interrupting long range fight
                float t = sinceSeen / (8f * (1.2f - disFactor)) / (0.33f + 0.67f * seenPosDeltaFactorSqr * sinceSeenFactorSqr);
                disFactor = Mathf.Lerp(0, disFactor, t); // Takes 1.6 seconds out of visual for the disFactor to reset for AIs at 110m away, 9.6s for 10m, 8.32s for 50m, if it's targeting the player, 3x the time
                                                                                                                          // disFactorLong = Mathf.Lerp(0, disFactorLong, sinceSeen / (8f * (1.2f - disFactorLong)) / (isGoalEnemy ? 0.33f : 1f)); // Takes 1.6 seconds out of visual for the disFactor to reset for AIs at 110m away, 9.6s for 10m, 8.32s for 50m, if it's targeting the player, 3x the time
                disFactorSmooth = Mathf.Lerp(0, disFactorSmooth, t);
            }

            var canSeeLight = player.LightAndLaserState.VisibleLight;
            if (!canSeeLight && inNVGView && player.LightAndLaserState.IRLight) canSeeLight = true;
            var canSeeLightSub = player.LightAndLaserState.VisibleLightSub;
            if (!canSeeLightSub && inNVGView && player.LightAndLaserState.IRLightSub) canSeeLightSub = true;
            var canSeeLaser = player.LightAndLaserState.VisibleLaser;
            if (!canSeeLaser && inNVGView && player.LightAndLaserState.IRLaser) canSeeLaser = true;
            var canSeeLaserSub = player.LightAndLaserState.VisibleLaserSub;
            if (!canSeeLaserSub && inNVGView && player.LightAndLaserState.IRLaserSub) canSeeLaserSub = true;
            if (visionAngleDelta > 110) canSeeLight = false;
            if (visionAngleDelta > 85) canSeeLaser = false;
            if (visionAngleDelta > 110) canSeeLightSub = false;
            if (visionAngleDelta > 85) canSeeLaserSub = false;

            // ======
            // Overhead overlooking
            // ======
            // Overlook close enemies at higher attitude and in low pose
            if (!canSeeLight)
            {
                var overheadChance = Mathf.InverseLerp(15f, 90f, visionAngleDeltaVerticalSigned);
                overheadChance *= overheadChance;
                overheadChance = overheadChance / Mathf.Clamp(pPoseFactor, 0.2f, 1f);
                overheadChance *= notSeenRecentAndNear;
                overheadChance *= Mathf.Clamp01(1f - pSpeedFactor * 2f);
                overheadChance *= 1 + disFactor;

                switch (caution)
                {
                    case 0:
                        overheadChance /= 1.5f;
                        break;
                    case 1:
                        overheadChance /= 1.2f;
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

                if (rand1 < Mathf.Clamp(overheadChance, 0, 0.995f))
                {
                    __result += rand5 * 0.1f;
                    __result *= 10 + rand2 * 100;
                }
            }
            // ======
            // Inside overlooking
            // ======
            if (!canSeeLight)
            {
                bool botIsInside = __instance.Owner.AIData.IsInside;
                bool playerIsInside = player.Player.AIData.IsInside;
                if (!botIsInside && playerIsInside && insideTime >= 1)
                {
                    var insideImpact = dis * Mathf.Clamp01(visionAngleDeltaVerticalSigned / 40f) * Mathf.Clamp01(visionAngleDelta / 60f) * (0.3f * seenPosDeltaFactorSqr + 0.7f * sinceSeenFactorSqr); // 50m -> 2.5/25 (BEST ANGLE), 10m -> 0.5/5
                    __result *= 1 + insideImpact * (0.75f + rand3 * 0.05f * caution);
                }
            }

            // ======
            // Global random overlooking
            // ======
            float globalOverlookChance = 0.01f / pPoseFactor; // Not scaled by disFactor because this is intended to mess with bot aiming and tracking for a bit.
            if (canSeeLight) globalOverlookChance /= 2f - disFactor;
            if (rand5 < globalOverlookChance * (0.5f + notSeenRecentAndNear))
            {
                __result *= 10 + rand1 * 10; // Instead of set it to flat 8888, so if the player has been in the vision for quite some time, this don't block
            }

            float score, factor;

            if (player.PlayerLitScoreProfile == null)
            {
                score = factor = 0;
            }
            else if (inThermalView)
            {
                score = factor = 0.7f;
                if (player.CheckEffectDelegate(EStimulatorBuffType.BodyTemperature))
                    score = factor = 0.7f;
            }
            else
            {
                score = player.PlayerLitScoreProfile.frame0.multiFrameLitScore; // -1 ~ 1

                if (score < 0 && inNVGView)
                {
                    float fluctuation = 1f + (rand2 - 0.5f) * 0.2f;
                    if (activeGoggle?.nightVision != null)
                    {
                        if (score < -0.85f)
                        {
                            score *= 1f - Mathf.Clamp01(activeGoggle.nightVision.nullificationExtremeDark * fluctuation); // It's really dark, slightly scale down
                        }
                        else if (score < -0.65f)
                            score *= 1f - Mathf.Clamp01(activeGoggle.nightVision.nullificationDarker * fluctuation); // It's quite dark, scale down
                        else if (score < 0)
                            score *= 1f - Mathf.Clamp01(activeGoggle.nightVision.nullification); // It's not really that dark, scale down massively
                    }
                    else if (activeScope?.nightVision != null)
                    {
                        if (score < -0.85f)
                            score *= 1f - Mathf.Clamp01(activeScope.nightVision.nullificationExtremeDark * fluctuation); // It's really dark, slightly scale down
                        else if (score < -0.65f)
                            score *= 1f - Mathf.Clamp01(activeScope.nightVision.nullificationDarker * fluctuation); // It's quite dark, scale down
                        else if (score < 0)
                            score *= 1f - Mathf.Clamp01(activeScope.nightVision.nullification); // It's not really that dark, scale down massively
                    }
                }

                if (inNVGView) // IR lights are not accounted in the score, process the score for each bot here
                {
                    float compensation = 0;
                    if (player.LightAndLaserState.IRLight)          compensation = Mathf.Clamp(0.4f - score, 0, 2) * player.LightAndLaserState.deviceStateCache.irLight;
                    else if (player.LightAndLaserState.IRLaser)     compensation = Mathf.Clamp(0.2f - score, 0, 2) * player.LightAndLaserState.deviceStateCache.irLaser;
                    else if (player.LightAndLaserState.IRLightSub)  compensation = Mathf.Clamp(0f - score, 0, 2) * player.LightAndLaserState.deviceStateCacheSub.irLight;
                    else if (player.LightAndLaserState.IRLaserSub)  compensation = Mathf.Clamp(0f - score, 0, 2) * player.LightAndLaserState.deviceStateCacheSub.irLaser;
                    score += compensation * Mathf.InverseLerp(0f, -1f, score);
                }

                factor = Mathf.Pow(score, ThatsLitMainPlayerComponent.POWER); // -1 ~ 1, the graph is basically flat when the score is between ~0.3 and 0.3

                if (factor < 0) factor *= 1 + disFactor * Mathf.Clamp01(1.2f - pPoseFactor) * (canSeeLight ? 0.2f : 1f) * (canSeeLaser ? 0.9f : 1f); // Darkness will be far more effective from afar
                else if (factor > 0) factor /= 1 + disFactor; // Highlight will be less effective from afar
            }

            bool nearestAI = false;
            if (player.DebugInfo != null && dis <= nearestRecent)
            {
                nearestRecent = dis;
                nearestAI = true;
                player.DebugInfo.lastNearest = nearestRecent;
                if (Time.frameCount % ThatsLitMainPlayerComponent.DEBUG_INTERVAL == ThatsLitMainPlayerComponent.DEBUG_INTERVAL - 1)
                {
                    player.DebugInfo.lastCalcFrom = original;
                    player.DebugInfo.lastScore = score;
                    player.DebugInfo.lastFactor1 = factor;
                }
            }

            if (player.Foliage != null)
            {
                var foliageImpact = player.Foliage.FoliageScore * (1f - factor);
                Vector2 bestMatchFoliageDir = Vector2.zero;
                float bestMatchDeg = 360f;
                for (int i = 0; i < Math.Min(ThatsLitPlugin.FoliageSamples.Value, player.Foliage.FoliageCount); i++) {
                    var f = player.Foliage.Foliage[i];
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
                                                * Mathf.Clamp01(1.35f - pPoseFactor)); // Lower chance for higher poses
                if (UnityEngine.Random.Range(0f, 1.05f) < foliageBlindChance) // Among bushes, from afar, always at least 5% to be uneffective
                {
                    __result *= 1 + rand4 * (10f - caution * 0.5f);
                    __result += rand2 + rand3 * 2f * disFactor;
                }
            }

            // CBQ Factors =====
            // The closer it is, the higher the factors
            var cqb6mTo1m = Mathf.InverseLerp(5f, 0f, dis - 1f); // 6+ -> 0, 1f -> 1
            var cqb16mTo1m = Mathf.InverseLerp(15f, 0f, dis - 1f); // 16+ -> 0, 1f -> 1                                                               // Fix for blind bots who are already touching us

            var cqb11mTo1mSquared = Mathf.InverseLerp(10f, 0, dis - 1f); // 11+ -> 0, 1 -> 1, 6 ->0.5
            cqb11mTo1mSquared *= cqb11mTo1mSquared; // 6m -> 25%, 1m -> 100%

            // Scale down cqb factors for AIs facing away
            // not scaled down when ~15deg
            cqb6mTo1m *= Mathf.InverseLerp(100f, 15f, visionAngleDelta);
            cqb16mTo1m *= Mathf.InverseLerp(100f, 15f, visionAngleDelta);
            cqb11mTo1mSquared *= Mathf.InverseLerp(100f, 15f, visionAngleDelta);

            var xyFacingFactor = 0f;
            var layingVerticaltInVisionFactor = 0f;
            var detailScore = 0f;
            var detailScoreRaw = 0f;
            if (!inThermalView && player.TerrainDetails != null)
            {
                var terrainScore = Singleton<ThatsLitGameworld>.Instance.CalculateDetailScore(player.TerrainDetails, -eyeToPlayerBody, dis, visionAngleDeltaVerticalSigned);
                if (terrainScore.prone > 0.1f || terrainScore.regular > 0.1f)
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
                        if (player.DebugInfo != null && nearestAI) player.DebugInfo.lastRotateAngle = facingAngleDelta;
#endif
                        xyFacingFactor = 1f - xyFacingFactor; // 0 ~ 1

                        // Calculate how flat it is in the vision
                        var normal = Vector3.Cross(BotTransform.up, -playerLegToBotEye);
                        var playerLegToHeadAlongVision = Vector3.ProjectOnPlane(playerLegToHead, normal);
                        layingVerticaltInVisionFactor = Vector3.SignedAngle(playerLegToBotEye, playerLegToHeadAlongVision, normal); // When the angle is 90, it means the player looks straight up in the vision, vice versa for -90.
#if DEBUG_DETAILS
                        if (player.DebugInfo != null && nearestAI)
                        {
                            if (layingVerticaltInVisionFactor >= 90f) player.DebugInfo.lastTiltAngle = (180f - layingVerticaltInVisionFactor);
                            else if (layingVerticaltInVisionFactor <= 0)  player.DebugInfo.lastTiltAngle = layingVerticaltInVisionFactor;
                        }
#endif

                        if (layingVerticaltInVisionFactor >= 90f) layingVerticaltInVisionFactor = (180f - layingVerticaltInVisionFactor) / 15f; // the player is laying head up feet down in the vision...   "-> /"
                        else if (layingVerticaltInVisionFactor <= 0 && layingVerticaltInVisionFactor >= -90f) layingVerticaltInVisionFactor = layingVerticaltInVisionFactor / -15f; // "-> /"
                        else layingVerticaltInVisionFactor = 0; // other cases grasses should take effect

                        detailScore = terrainScore.prone * Mathf.Clamp01(1f - layingVerticaltInVisionFactor * xyFacingFactor);
                    }
                    else
                    {
                        detailScore = terrainScore.regular / (1f + 0.35f * Mathf.InverseLerp(0.45f, 1f, pPoseFactor));
                        detailScore *= (1f - cqb11mTo1mSquared) * Mathf.InverseLerp(-25f, 5, visionAngleDeltaVerticalSigned); // nerf when high pose or < 10m or looking down
                    }

                    detailScore = Mathf.Min(detailScore, 2.5f - pPoseFactor); // Cap extreme grasses for high poses

                    detailScoreRaw = detailScore;
                    detailScore *= 1f + disFactor / 2f; // At 110m+, 1.5x effective
                    if (canSeeLight) detailScore /= 2f - disFactor; // Flashlights impact less from afar
                    if (canSeeLaser) detailScore *= 0.8f + 0.2f * disFactor; // Flashlights impact less from afar

                    switch (caution)
                    {
                        case 0:
                            detailScore /= 1.5f;
                            detailScore *= 1f - cqb16mTo1m * Mathf.Clamp01((5f - visionAngleDeltaVerticalSigned) / 30f); // nerf starting from looking 5deg up to down (scaled by down to -25deg) and scaled by dis 15 ~ 0
                            break;
                        case 1:
                            detailScore /= 1.25f;
                            detailScore *= 1f - cqb16mTo1m * Mathf.Clamp01((5f - visionAngleDeltaVerticalSigned) / 40f); // nerf starting from looking 5deg up (scaled by down to -40deg) and scaled by dis 15 ~ 0
                            break;
                        case 2:
                        case 3:
                        case 4:
                            detailScore *= 1f - cqb11mTo1mSquared * Mathf.Clamp01((5f - visionAngleDeltaVerticalSigned) / 40f);
                            break;
                        case 5:
                        case 6:
                        case 7:
                        case 8:
                        case 9:
                            detailScore *= 1.2f;
                            detailScore *= 1f - cqb6mTo1m * Mathf.Clamp01((5f - visionAngleDeltaVerticalSigned) / 50f); // nerf starting from looking 5deg up (scaled by down to -50deg) and scaled by dis 5 ~ 0
                            break;
                    }

                    // Applying terrain detail stealth
                    // 0.1% chance does not work (very low because bots can track after spotting)
                    if (UnityEngine.Random.Range(0f, 1.001f) < Mathf.Clamp01(detailScore))
                    {
                        float detailImpact;
                        detailImpact = 19f * Mathf.Clamp01(notSeenRecentAndNear + 0.25f * Mathf.Clamp01(detailScore - 1f)) * (0.05f + disFactorSmooth); // The closer it is the more the player need to move to gain bonus from grasses, if has been seen;

                        // After spotted, the palyer could be tracked and lose all detail impact
                        // If the score is extra high and is proning, gives a change to get away  (the score is not capped to 1 even when crouching)
                        if (detailScore > 1 && isInPronePose)
                        {
                            detailImpact += (2f + rand5 * 3f) * (1f - disFactorSmooth) * (2f - visionAngleDelta90Clamped);
                            deNullification = 0.5f;
                        }
                        __result *= 1 + detailImpact;

                        if (player.DebugInfo != null && nearestAI)
                        {
                            player.DebugInfo.lastTriggeredDetailCoverDirNearest = -eyeToPlayerBody;
                        }
                    }
                }
                if (player.DebugInfo != null && nearestAI)
                {
                    player.DebugInfo.lastFinalDetailScoreNearest = detailScore;
                    player.DebugInfo.lastDisFactorNearest = disFactor;
                }
            }

            // BUSH RAT ----------------------------------------------------------------------------------------------------------------
            /// Overlook when the bot has no idea the player is nearby and the player is sitting inside a bush
            if (!inThermalView && player.Foliage != null && botImpactType != BotImpactType.BOSS
             && (!__instance.HaveSeen || lastSeenPosDelta > 30f + rand1 * 20f || sinceSeen > 150f + 150f*rand3 && lastSeenPosDelta > 10f + 10f*rand2))
            {
                float angleFactor = 0, foliageDisFactor = 0, poseScale = 0, enemyDisFactor = 0, yDeltaFactor = 1;
                bool bushRat = true;

                FoliageInfo nearestFoliage = player.Foliage.Foliage[0];
                switch (nearestFoliage.name)
                {
                    case "filbert_big01":
                        angleFactor = 1; // works even if looking right at
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.8f) / 0.7f);
                        enemyDisFactor = Mathf.Clamp01(dis / 2.5f); // 100% at 2.5m+
                        poseScale = 1 - Mathf.Clamp01((pPoseFactor - 0.45f) / 0.55f); // 100% at crouch
                        yDeltaFactor = 1f - Mathf.Clamp01(-visionAngleDeltaVerticalSigned / 60f); // +60deg => 1, 0deg => 1, -30deg => 0.5f, -60deg (looking down) => 0 (this flat bush is not effective against AIs up high)
                        break;
                    case "filbert_big02":
                        angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 20f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.1f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 10f);
                        poseScale = pPoseFactor == 0.05f ? 0.7f : 1f; // 
                        break;
                    case "filbert_big03":
                        angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 30f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.25f) / 0.2f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 15f);
                        poseScale = pPoseFactor == 0.05f ? 0 : 0.1f + (pPoseFactor - 0.45f) / 0.55f * 0.9f; // standing is better with this tall one
                        break;
                    case "filbert_01":
                        angleFactor = 1;
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.35f) / 0.25f);
                        enemyDisFactor = Mathf.Clamp01(dis / 12f); // 100% at 2.5m+
                        poseScale = 1 - Mathf.Clamp01((pPoseFactor - 0.45f) / 0.3f);
                        break;
                    case "filbert_small01":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 35f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.15f) / 0.15f);
                        enemyDisFactor = Mathf.Clamp01(dis / 10f);
                        poseScale = pPoseFactor == 0.45f ? 1f : 0;
                        break;
                    case "filbert_small02":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 25f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.15f) / 0.15f);
                        enemyDisFactor = Mathf.Clamp01(dis / 8f);
                        poseScale = pPoseFactor == 0.45f ? 1f : 0;
                        break;
                    case "filbert_small03":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 40f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.1f) / 0.15f);
                        enemyDisFactor = Mathf.Clamp01(dis / 10f);
                        poseScale = pPoseFactor == 0.45f ? 1f : 0;
                        break;
                    case "filbert_dry03":
                        angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 30f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.3f);
                        enemyDisFactor = Mathf.Clamp01(dis / 30f);
                        poseScale = pPoseFactor == 0.05f ? 0 : 0.1f + (pPoseFactor - 0.45f) / 0.55f * 0.9f;
                        break;
                    case "fibert_hedge01":
                        angleFactor = Mathf.Clamp01(visionAngleDelta / 40f);
                        foliageDisFactor = (1f - Mathf.Clamp01((nearestFoliage.dis - 0.1f) / 0.1f)) * (1f - Mathf.Clamp01(nearestFoliage.dis / 0.2f));
                        enemyDisFactor = Mathf.Clamp01(dis / 30f);
                        poseScale = pPoseFactor == 0.45f ? 1f : 0; // Too narrow for proning
                        break;
                    case "fibert_hedge02":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 40f);
                        foliageDisFactor = (1f - Mathf.Clamp01((nearestFoliage.dis - 0.1f) / 0.2f)) * (1f - Mathf.Clamp01(nearestFoliage.dis / 0.3f));
                        enemyDisFactor = Mathf.Clamp01(dis / 20f);
                        poseScale = pPoseFactor == 0.45f ? 1f : 0; // Too narrow for proning
                        break;
                    case "privet_hedge":
                    case "privet_hedge_2":
                        angleFactor = Mathf.Clamp01((visionAngleDelta - 30f) / 60f);
                        foliageDisFactor = (1f - Mathf.Clamp01(nearestFoliage.dis / 1f)) * (1f - Mathf.Clamp01(nearestFoliage.dis / 0.3f));
                        enemyDisFactor = Mathf.Clamp01(dis / 50f);
                        poseScale = pPoseFactor < 0.45f ? 1f : 0; // Prone only
                        break;
                    case "bush_dry01":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 35f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.15f) / 0.15f);
                        enemyDisFactor = Mathf.Clamp01(dis / 25f);
                        poseScale = pPoseFactor == 0.45f ? 1f : 0;
                        break;
                    case "bush_dry02":
                        angleFactor = 1;
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 1f) / 0.4f);
                        enemyDisFactor = Mathf.Clamp01(dis / 15f);
                        poseScale = 1 - Mathf.Clamp01((pPoseFactor - 0.45f) / 0.1f);
                        yDeltaFactor = 1f - Mathf.Clamp01(-visionAngleDeltaVerticalSigned / 60f); // +60deg => 1, -60deg (looking down) => 0 (this flat bush is not effective against AIs up high)
                        break;
                    case "bush_dry03":
                        angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 20f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.3f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 20f);
                        poseScale = pPoseFactor == 0.05f ? 0.6f : 1 - Mathf.Clamp01((pPoseFactor - 0.45f) / 0.55f); // 100% at crouch
                        break;
                    case "tree02":
                        yDeltaFactor = 0.7f + 0.5f * Mathf.Clamp01((-visionAngleDeltaVerticalSigned - 10) / 40f); // bonus against bots up high
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 45f); // 0deg -> 0, 75 deg -> 1
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.2f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis * yDeltaFactor / 20f);
                        poseScale = pPoseFactor == 0.05f ? 0 : 0.1f + (pPoseFactor - 0.45f) / 0.55f * 0.9f; // standing is better with this tall one
                        break;
                    case "pine01":
                        yDeltaFactor = 0.7f + 0.5f * Mathf.Clamp01((-visionAngleDeltaVerticalSigned - 10) / 40f); // bonus against bots up high
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 30f); // 0deg -> 0, 75 deg -> 1
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.35f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis * yDeltaFactor / 25f);
                        poseScale = pPoseFactor == 0.05f ? 0 : 0.5f + (pPoseFactor - 0.45f) / 0.55f * 0.5f; // standing is better with this tall one
                        break;
                    case "pine05":
                        angleFactor = 1; // 0deg -> 0, 75 deg -> 1
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.45f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 20f);
                        poseScale = pPoseFactor == 0.05f ? 0 : 0.5f + (pPoseFactor - 0.45f) / 0.55f * 0.5f; // standing is better with this tall one
                        yDeltaFactor = Mathf.Clamp01((-visionAngleDeltaVerticalSigned - 15) / 45f); // only against bots up high
                        break;
                    case "fern01":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 25f); // 0deg -> 0, 75 deg -> 1
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.1f) / 0.2f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 30f);
                        poseScale = pPoseFactor == 0.05f ? 1f : (1f - pPoseFactor) / 5f; // very low
                        break;
                    default:
                        bushRat = false;
                        break;
                }
                var bushRatFactor = Mathf.Clamp01(angleFactor * foliageDisFactor * enemyDisFactor * poseScale * yDeltaFactor);
                if (botImpactType == BotImpactType.FOLLOWER || canSeeLight || (canSeeLaser && rand3 < 0.2f)) bushRatFactor /= 2f;
                if (bushRat && bushRatFactor > 0.01f)
                {
                    if (player.DebugInfo != null && nearestAI) player.DebugInfo.IsBushRatting = bushRat;
                    __result = Mathf.Max(__result, dis);
                    switch (caution)
                    {
                        case 0:
                        case 1:
                            if (rand2 > 0.01f) __result *= 1 + 4 * bushRatFactor * UnityEngine.Random.Range(0.2f, 0.4f);
                            cqb6mTo1m *= 1f - bushRatFactor * 0.5f;
                            cqb11mTo1mSquared *= 1f - bushRatFactor * 0.5f;
                            break;
                        case 2:
                        case 3:
                        case 4:
                            if (rand3 > 0.005f) __result *= 1 + 8 * bushRatFactor * UnityEngine.Random.Range(0.3f, 0.65f);
                            cqb6mTo1m *= 1f - bushRatFactor * 0.8f;
                            cqb11mTo1mSquared *= 1f - bushRatFactor * 0.8f;
                            break;
                        case 5:
                        case 6:
                        case 7:
                        case 8:
                        case 9:
                            if (rand1 > 0.001f) __result *= 1 + 6 * bushRatFactor * UnityEngine.Random.Range(0.5f, 1.0f);
                            cqb6mTo1m *= 1f - bushRatFactor;
                            cqb11mTo1mSquared *= 1f - bushRatFactor;
                            break;
                    }
                }
            }
            // BUSH RAT ----------------------------------------------------------------------------------------------------------------


            /// -0.7 -> 0, -0.8 -> 0.33, -0.9 -> 0.66, -1 -> 1
            var extremeDarkFactor = Mathf.Clamp01((score - -0.7f) / -0.3f);
            extremeDarkFactor *= extremeDarkFactor;
            var notSoExtremeDarkFactor = Mathf.Clamp01((score - -0.5f) / -0.5f);
            notSoExtremeDarkFactor *= notSoExtremeDarkFactor;

            if (player.PlayerLitScoreProfile == null && ThatsLitPlugin.AlternativeReactionFluctuation.Value)
            {
                // https://www.desmos.com/calculator/jbghqfxwha
                float cautionFactor = (caution / 9f - 0.5f) * (0.05f + 0.5f * rand4 * rand4); // -0.5(faster)~0.5(slower) squared curve distribution
                __result += cautionFactor;
                __result *= 1f + cautionFactor / 2f; // Factor in bot class
            }
            else if (player.PlayerLitScoreProfile != null && Mathf.Abs(score) >= 0.05f) // Skip works
            {
                if (Singleton<ThatsLitGameworld>.Instance.IsWinter && player.Foliage != null)
                {
                    var emptiness = 1f - player.Foliage.FoliageScore * detailScoreRaw;
                    emptiness *= 1f - insideTime;
                    disFactor *= 0.7f + 0.3f * emptiness; // When player outside is not surrounded by anything in winter, lose dis buff
                }

                factor = Mathf.Clamp(factor, -0.975f, 0.975f);

                // Absoulute offset
                // f-0.1 => -0.005~-0.01, factor: -0.2 => -0.02~-0.04, factor: -0.5 => -0.125~-0.25, factor: -1 => 0 ~ -0.5 (1m), -0.5 ~ -1 (10m)
                var secondsOffset = -1f * Mathf.Pow(factor, 2) * Mathf.Sign(factor) * (UnityEngine.Random.Range(0.5f, 1f) - 0.5f * cqb11mTo1mSquared); // Base
                secondsOffset += (original * (10f + rand1 * 20f) * (0.1f + 0.9f * sinceSeenFactorSqr * seenPosDeltaFactorSqr) * extremeDarkFactor) / pPoseFactor; // Makes night factory makes sense (filtered by extremeDarkFactor)
                secondsOffset *= botImpactType == BotImpactType.DEFAULT? 1f : 0.5f;
                secondsOffset *= secondsOffset > 0 ? ThatsLitPlugin.BrightnessImpactScale.Value : ThatsLitPlugin.DarknessImpactScale.Value;
                __result += secondsOffset;
                if (__result < 0) __result = 0;


                // The scaling here allows the player to stay in the dark without being seen
                // The reason why scaling is needed is because SeenCoef will change dramatically depends on vision angles
                // Absolute offset alone won't work for different vision angles
                if (factor < 0)
                {
                    float combinedCqb10x5To1 = 0.5f * cqb11mTo1mSquared * (0.7f + 0.3f * rand2 * pPoseFactor);
                    combinedCqb10x5To1 += 0.5f * cqb6mTo1m;
                    combinedCqb10x5To1 *= 0.9f + 0.4f * pSpeedFactor; // Buff bot CQB reaction when the player is moving fast
                    combinedCqb10x5To1 = Mathf.Clamp01(combinedCqb10x5To1);

                    // cqb factors are already scaled down by vision angle

                    var attentionCancelChanceScaleByExDark = 0.2f * rand5 * Mathf.InverseLerp(-0.8f, -1f, score);

                    // === Roll a forced stealth boost ===
                    // negative because factor is negative
                    float forceStealthChance = factor * Mathf.Clamp01(1f - combinedCqb10x5To1);
                    // 60% nullified by bot attention (if not cancelled by extreme darkness)
                    forceStealthChance *= 0.4f + 0.6f * Mathf.Clamp01(notSeenRecentAndNear + attentionCancelChanceScaleByExDark);
                    if (UnityEngine.Random.Range(-1f, 0f) > forceStealthChance)
                    {
                        __result *= 100 * ThatsLitPlugin.DarknessImpactScale.Value;
                    }
                    else
                    {
                        var scale = factor * factor * 0.5f + 0.5f* Mathf.Abs(factor * factor * factor);
                        scale *= 3f;
                        scale *= ThatsLitPlugin.DarknessImpactScale.Value;
                        scale *= 1f - combinedCqb10x5To1;
                        // -1 => 3
                        // -0.5 => 0.5625
                        // -0.2 => 0.072

                        scale *= 0.7f + 0.3f * notSeenRecentAndNear;
                        __result *= 1f+scale;
                    }

                }
                else if (factor > 0)
                {
                    if (rand5 < factor * factor) __result *= 1f - 0.5f * ThatsLitPlugin.BrightnessImpactScale.Value;
                    else __result /= 1f + factor / 5f * ThatsLitPlugin.BrightnessImpactScale.Value;
                }
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
                    float delta = __result * (rand2 / (5f + caution * 0.1f)) * pSpeedFactor * (1f - extremeDarkFactor) * Mathf.Clamp01(pPoseFactor); // When the score is -0.7+, bots takes up to 20% shorter to spot the player according to player movement speed (when not proning);
                    __result -= delta;
                }
            }


            __result = Mathf.Lerp(original, __result, ThatsLitPlugin.FinalImpactScale.Value); // just seen (0s) => original, 0
            __result = Mathf.Lerp(__result, original, botImpactType == BotImpactType.DEFAULT? 0f : 0.5f);

            if (__result > original) // That's Lit delaying the bot
            {
                // In ~0.2s after being seen, stealth is nullfied (fading between 0.1~0.2)
                // To prevent interruption of ongoing fight
                float nullification = 1f - sinceSeen / 0.2f; // 0.1s => 50%, 0.2s => 0%
                nullification *= rand5;
                nullification -= deNullification; // Allow features to interrupt the nullification
                __result = Mathf.Lerp(__result, original, Mathf.Clamp01(nullification)); // just seen (0s) => original, 0.1s => modified
            }
            // This probably will let bots stay unaffected until losing the visual.1s => modified

            // Up to 50% penalty
            if (__result < 0.5f * original)
            {
                __result = 0.5f * original;
            }

            __result += ThatsLitPlugin.FinalOffset.Value;
            if (__result < 0.005f) __result = 0.005f;

            if (player.DebugInfo != null)
            {
                if (Time.frameCount % ThatsLitMainPlayerComponent.DEBUG_INTERVAL == ThatsLitMainPlayerComponent.DEBUG_INTERVAL - 1 && nearestAI)
                {
                    player.DebugInfo.lastCalcTo = __result;
                    player.DebugInfo.lastFactor2 = factor;
                    player.DebugInfo.rawTerrainScoreSample = detailScoreRaw;
                }
                player.DebugInfo.calced++;
                player.DebugInfo.calcedLastFrame++;
            }
            

            ThatsLitPlugin.swSeenCoef.Stop();
        }
    }
}