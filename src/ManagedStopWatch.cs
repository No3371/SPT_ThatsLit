#define DEBUG_DETAILS
using System;
using System.Diagnostics;

namespace ThatsLit
{
    public struct ManagedStopWatch
    {
        Stopwatch sw;
        string id;

        public ManagedStopWatch(string id) : this()
        {
            this.id = id;
        }

        public void MaybeResume ()
        {
            if (ThatsLitPlugin.EnableBenchmark.Value && ThatsLitPlugin.DebugInfo.Value)
            {
                if (sw == null) sw = new System.Diagnostics.Stopwatch();
                if (sw.IsRunning)
                {
                    string message = $"[That's Lit] Benchmark stopwatch is not stopped! ({id})";
                    NotificationManagerClass.DisplayWarningNotification(message);
                    Logger.LogWarning(message);
                }
                sw.Start();
            }
            else sw = null;
        }

        public void Stop ()
        {
            sw?.Stop();
        }

        public float ConcludeMs ()
        {
            if (sw == null) return 0;

            var ms = sw.ElapsedMilliseconds;
            sw.Reset();
            return ms;
        }

        // !! THIS SOMEHOW DOES NOT WORK
        // It's disposed immediately despite IL does not looks like so
        // public struct RunningScope : IDisposable
        // {
        //     public ManagedStopWatch Host { get; }
        //     public RunningScope(ManagedStopWatch host)
        //     {
        //         Host = host;
        //         Host.MaybeResume();
        //     }

        //     public void Dispose()
        //     {
        //         Host.Stop();
        //     }
        // }
    }
}