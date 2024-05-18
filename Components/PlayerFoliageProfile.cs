// #define DEBUG_DETAILS
using UnityEngine;

namespace ThatsLit
{
    public class PlayerFoliageProfile
    {
        public FoliageInfo[] Foliage { get; internal set; }

        public PlayerFoliageProfile(FoliageInfo[] foliage, Collider[] castedFoliageColliders)
        {
            Foliage = foliage;
            CastedFoliageColliders = castedFoliageColliders;
        }

        public Collider[] CastedFoliageColliders { get; internal set; }
        public int FoliageCount { get; internal set; }
        public float FoliageScore { get; internal set; }
        public float LastCheckedTime { get; internal set; }
        public Vector3 LastCheckedPos { get; internal set; }
        public bool IsFoliageSorted { get; internal set; }
        public FoliageInfo? Nearest
        {
            get
            {
                if (Foliage != null && Foliage.Length > 0) return Foliage[0];
                return null;
            }
        }
    }
}