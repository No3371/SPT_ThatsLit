#define DEBUG_DETAILS
using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace ThatsLit
{
    public class PlayerLitScoreProfile : IDisposable
    {
        public bool IsProxy { get; set; }
        internal float lum3s, lum1s, lum10s;
        public FrameStats frame0, frame1, frame2, frame3, frame4, frame5;
        internal float detailBonusSmooth;
        internal float litScoreFactor;
        public ThatsLitPlayer Player { get; }
        public CountPixelsJob PixelCountingJob { get; set; }
        public JobHandle CountingJobHandle { get; set; }
        internal object ScoreCalcData { get; set; }

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

        public void Dispose()
        {
            CountingJobHandle.Complete();
            PixelCountingJob.Dispose();
        }

        public struct CountPixelsJob : IJobParallelFor, IDisposable
        {
            [Unity.Collections.LowLevel.Unsafe.NativeSetThreadIndex]
            public int threadIndex;
            [ReadOnly]
            public NativeArray<Color32> tex;
            [WriteOnly]
            [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction]
            public NativeArray<int> counted; // pxS, pxH, pxHM, pxM, pxML, pxL, pxD, valid
            [WriteOnly]
            [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction]
            public NativeArray<float> lum; // lum, lumNonDark
            [ReadOnly]
            public NativeArray<float> thresholds; // thresholdShine, thresholdHigh, thresholdHighMid, thresholdMid, thresholdMidLow, thresholdLow

            public void Dispose()
            {
                tex.Dispose();
                counted.Dispose();
                lum.Dispose();
                thresholds.Dispose();
            }

            public void Execute(int index)
            {
                Color32 c = tex[index];
                if (c == Color.white || c.a <= 0.5f)
                    return;

                var pxLum = (c.r + c.g + c.b) / 765f;

                int threadIndexOffset = threadIndex * 8;
                lum[threadIndexOffset + 0] += pxLum;
                if (pxLum < thresholds[5])
                {
                    counted[threadIndexOffset + 6] += 1;
                    lum[threadIndexOffset + 1] += pxLum;
                }
                else if (pxLum >= thresholds[0]) counted[threadIndexOffset + 0] += 1;
                else if (pxLum >= thresholds[1]) counted[threadIndexOffset + 1] += 1;
                else if (pxLum >= thresholds[2]) counted[threadIndexOffset + 2] += 1;
                else if (pxLum >= thresholds[3]) counted[threadIndexOffset + 3] += 1;
                else if (pxLum >= thresholds[4]) counted[threadIndexOffset + 4] += 1;
                else if (pxLum >= thresholds[5]) counted[threadIndexOffset + 5] += 1;

                counted[threadIndexOffset + 7] += 1;
            }
        }
    }
}