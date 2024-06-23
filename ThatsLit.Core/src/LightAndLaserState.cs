using System.Collections.Generic;

namespace ThatsLit
{
    public struct LightAndLaserState
    {
        public bool VisibleLight
        {
            get => (storage & (1 << 7)) > 0;
            set => storage = value? storage | (1 << 7) : storage & ~(1 << 7);
        }
        public bool VisibleLaser
        {
            get => (storage & (1 << 6)) > 0;
            set => storage = value? storage | (1 << 6) : storage & ~(1 << 6);
        }
        public bool IRLight
        {
            get => (storage & (1 << 5)) > 0;
            set => storage = value? storage | (1 << 5) : storage & ~(1 << 5);
        }
        public bool IRLaser
        {
            get => (storage & (1 << 4)) > 0;
            set => storage = value? storage | (1 << 4) : storage & ~(1 << 4);
        }
        public bool VisibleLightSub
        {
            get => (storage & (1 << 3)) > 0;
            set => storage = value? storage | (1 << 3) : storage & ~(1 << 3);
        }
        public bool VisibleLaserSub
        {
            get => (storage & (1 << 2)) > 0;
            set => storage = value? storage | (1 << 2) : storage & ~(1 << 2);
        }
        public bool IRLightSub
        {
            get => (storage & (1 << 1)) > 0;
            set => storage = value? storage | (1 << 1) : storage & ~(1 << 1);
        }
        public bool IRLaserSub
        {
            get => (storage & (1 << 0)) > 0;
            set => storage = value? storage | (1 << 0) : storage & ~(1 << 0);
        }
        public long storage;

        public bool AnyVisible
        {
            get => (storage & 0b_1100_1100) > 0;
        }
        public bool AnyVisibleLight
        {
            get => (storage & 0b_1000_1000) > 0;
        }
        public bool AnyVisibleLaser
        {
            get => (storage & 0b_0100_0100) > 0;
        }
        public bool AnyIRLight
        {
            get => (storage & 0b_0010_0010) > 0;
        }
        public bool AnyIRLaser
        {
            get => (storage & 0b_0001_0001) > 0;
        }
        public bool AnyVisibleMain
        {
            get => (storage & 0b_1100_0000) > 0;
        }
        public bool AnyIRMain
        {
            get => (storage & 0b_0011_0000) > 0;
        }
        public bool AnyVisibleSub
        {
            get => (storage & 0b_0000_1100) > 0;
        }
        public bool AnyIRSub
        {
            get => (storage & 0b_0000_0011) > 0;
        }
        public bool AnyIR
        {
            get => (storage & 0b_0011_0011) > 0;
        }
        public bool AnyLight
        {
            get => (storage & 0b_1010_1010) > 0;
        }
        public bool AnyLightMain
        {
            get => (storage & 0b_1010_0000) > 0;
        }
        public bool AnyLaser
        {
            get => (storage & 0b_0101_0101) > 0;
        }
        public bool AnyMain
        {
            get => (storage & 0b_1111_0000) > 0;
        }
        public bool AnySub
        {
            get => (storage & 0b_0000_1111) > 0;
        }
        public bool Any
        {
            get => (storage & 0b_1111_1111) > 0;
        }

        static Dictionary<long, string> cache = new Dictionary<long, string>();
        public string Format ()
        {
            if (cache.TryGetValue(storage, out var str)) return str;

            str = $"  V { (VisibleLight? "◆" : "◇") }{ (VisibleLaser? "◆" : "◇") } IR { (IRLight? "◆" : "◇") }{ (IRLaser? "◆" : "◇") } / V { (VisibleLightSub? "◆" : "◇") }{ (VisibleLaserSub? "◆" : "◇") } IR { (IRLightSub? "◆" : "◇") }{ (IRLaserSub? "◆" : "◇") } " ;
            cache.Add(storage, str);
            return str;
        }

        public ThatsLitCompat.DeviceMode deviceStateCache, deviceStateCacheSub;
    }
}