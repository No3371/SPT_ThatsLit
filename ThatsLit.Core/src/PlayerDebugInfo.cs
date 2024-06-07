#define DEBUG_DETAILS
using UnityEngine;

namespace ThatsLit
{
    public class PlayerDebugInfo
    {
        public bool IsBushRatting { get; set;} 
        public float lastCalcFrom, lastCalcTo, lastScore, lastFactor1, lastFactor2, rawTerrainScoreSample;
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
    }
}