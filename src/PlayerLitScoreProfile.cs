#define DEBUG_DETAILS
using UnityEngine;

namespace ThatsLit
{
    public class PlayerLitScoreProfile
    {
        internal float lum3s, lum1s, lum10s;
        public FrameStats frame0, frame1, frame2, frame3, frame4, frame5;
        internal float detailBonusSmooth;
        internal float litScoreFactor;
        public ThatsLitPlayer Player { get; }

        public PlayerLitScoreProfile(ThatsLitPlayer player)
        {
            Player = player;
        }

        internal void UpdateLumTrackers (float avgLumMultiFrames)
        {
            lum1s = Mathf.Lerp(lum1s, avgLumMultiFrames, Time.deltaTime);
            lum3s = Mathf.Lerp(lum3s, avgLumMultiFrames, Time.deltaTime / 3f);
            lum10s = Mathf.Lerp(lum10s, avgLumMultiFrames, Time.deltaTime / 10f);
        }

        internal float FindHighestAvgLumRecentFrame(bool includeThis, float thisframe)
        {
            float avgLum = includeThis ? thisframe : frame1.avgLum;
            if (frame1.avgLum > avgLum) avgLum = frame1.avgLum;
            if (frame2.avgLum > avgLum) avgLum = frame2.avgLum;
            if (frame3.avgLum > avgLum) avgLum = frame3.avgLum;
            if (frame4.avgLum > avgLum) avgLum = frame4.avgLum;
            if (frame5.avgLum > avgLum) avgLum = frame5.avgLum;
            return avgLum;
        }

        internal float FindLowestAvgLumRecentFrame(bool includeThis, float calculating)
        {
            float avgLum = includeThis ? calculating : frame1.avgLum;
            if (frame1.avgLum < avgLum) avgLum = frame1.avgLum;
            if (frame2.avgLum < avgLum) avgLum = frame2.avgLum;
            if (frame3.avgLum < avgLum) avgLum = frame3.avgLum;
            if (frame4.avgLum < avgLum) avgLum = frame4.avgLum;
            if (frame5.avgLum < avgLum) avgLum = frame5.avgLum;
            return avgLum;
        }

        internal float FindHighestMFAvgLumRecentFrame(bool includeThis, float thisframe)
        {
            float mfAvgLum = includeThis ? thisframe : frame1.avgLumMultiFrames;
            if (frame1.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame1.avgLumMultiFrames;
            if (frame2.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame2.avgLumMultiFrames;
            if (frame3.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame3.avgLumMultiFrames;
            if (frame4.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame4.avgLumMultiFrames;
            if (frame5.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame5.avgLumMultiFrames;
            return mfAvgLum;
        }

        internal float FindLowestMFAvgLumRecentFrame(bool includeThis, float calculating)
        {
            float mfAvgLum = includeThis ? calculating : frame1.avgLumMultiFrames;
            if (frame1.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame1.avgLumMultiFrames;
            if (frame2.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame2.avgLumMultiFrames;
            if (frame3.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame3.avgLumMultiFrames;
            if (frame4.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame4.avgLumMultiFrames;
            if (frame5.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame5.avgLumMultiFrames;
            return mfAvgLum;
        }

        internal float FindHighestScoreRecentFrame (bool includeThis, float calculating)
        {
            float score = includeThis ? calculating : frame1.score;
            if (frame1.score > score) score = frame1.score;
            if (frame2.score > score) score = frame2.score;
            if (frame3.score > score) score = frame3.score;
            if (frame4.score > score) score = frame4.score;
            if (frame5.score > score) score = frame5.score;
            return score;
        }

        internal float FindLowestScoreRecentFrame(bool includeThis, float calculating)
        {
            float score = includeThis ? calculating : frame1.score;
            if (frame1.score < score) score = frame1.score;
            if (frame2.score < score) score = frame2.score;
            if (frame3.score < score) score = frame3.score;
            if (frame4.score < score) score = frame4.score;
            if (frame5.score < score) score = frame5.score;
            return score;
        }
        
    }
}