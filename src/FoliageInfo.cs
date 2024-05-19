// #define DEBUG_DETAILS
using System;
using UnityEngine;

namespace ThatsLit
{
    public struct FoliageInfo : IComparable<FoliageInfo>
    {
        public string name;
        public Vector2 dir;
        public float dis;
        public FoliageInfo(string name, Vector2 dir, float dis) { this.name = name; this.dir = dir; this.dis = dis; }
        public int CompareTo(FoliageInfo other)
        {
            return Math.Sign(dis - other.dis);
        }
        public static bool operator == (FoliageInfo x, FoliageInfo y)
        {
            return x.name == y.name && x.dis == y.dis && x.dir == y.dir;
        }
        public static bool operator != (FoliageInfo x, FoliageInfo y)
        {
            return !(x == y);
        }
        public override bool Equals(object obj)
        {
            return this == (FoliageInfo)obj;
        }
        public override int GetHashCode() => (name, dir, dis).GetHashCode();
    }
}