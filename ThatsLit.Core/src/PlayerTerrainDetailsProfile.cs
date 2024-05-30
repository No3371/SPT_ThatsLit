// #define DEBUG_DETAILS
using UnityEngine;

namespace ThatsLit
{
    public class PlayerTerrainDetailsProfile
    {
        public CastedDetailInfo[] Details5x5 { get; internal set; }
        public TerrainDetailScore[] detailScoreCache = new TerrainDetailScore[20];
        public int RecentDetailCount3x3 { get; internal set; }
        public int RecentDetailCount5x5 { get; internal set; }
        public float LastCheckedTime { get; internal set; }
        public Vector3 LastCheckedPos { get; internal set; }
        internal int GetDetailInfoIndex(int x5x5, int y5x5, int detailId, int maxDetailTypes) => (y5x5 * 5 + x5x5) * maxDetailTypes + detailId;
    }
}