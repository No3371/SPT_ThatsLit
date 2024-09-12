#define DEBUG_DETAILS
using UnityEngine;

namespace ThatsLit
{
    public class PlayerDebugInfo
    {
        public bool IsBushRatting { get; set;} 
        public float lastCalcFrom, lastScore, lastFactor1, lastFactor2, rawTerrainScoreSample;
        public float lastCalcTo0, lastCalcTo1, lastCalcTo2, lastCalcTo3, lastCalcTo4, lastCalcTo5, lastCalcTo6, lastCalcTo7, lastCalcTo8;
        public int calced = 0, calcedLastFrame = 0, encounter, vagueHint, vagueHintCancel, signalDanger;
        public Vector3 lastTriggeredDetailCoverDirNearest;
        public float lastTiltAngle, lastRotateAngle, lastDisFactorNearest;
        public float lastNearest;
        public float lastFinalDetailScoreNearest;
        internal float scoreRawBase, scoreRaw0, scoreRaw1, scoreRaw2, scoreRaw3, scoreRaw4;
        internal float shinePixelsRatioSample, highLightPixelsRatioSample, highMidLightPixelsRatioSample, midLightPixelsRatioSample, midLowLightPixelsRatioSample, lowLightPixelsRatioSample, darkPixelsRatioSample;
        internal float lastEncounterShotAngleDelta, lastEncounteringShotCutoff;
        internal float lastBushRat;
        internal float lastVisiblePartsFactor;
        internal float lastGlobalOverlookChance;
        internal float lastDisCompThermal, lastDisCompNVG, lastDisCompDay, lastDisComp;
        internal float lastNearestFocusAngleX, lastNearestFocusAngleY;
        internal int cancelledSAINNoBush, attemptToCancelSAINNoBush;
        internal float lastInterruptChance, lastInterruptChanceDis;
        internal int nearestCaution;
        internal Vector3 nearestOffset, sniperHintOffset;
        internal float sniperHintChance;
        internal int flashLightHint;
        internal int forceLooks, sideLooks;
    }
}