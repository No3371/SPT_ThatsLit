namespace ThatsLit
{
    public struct TerrainDetailScore
    {
        public bool cached;
        public float prone;
        public float regular;

        public TerrainDetailScore(bool item1, float item2, float item3)
        {
            cached = item1;
            prone = item2;
            regular = item3;
        }

        public override bool Equals(object obj)
        {
            return obj is TerrainDetailScore other &&
                   cached == other.cached &&
                   prone == other.prone &&
                   regular == other.regular;
        }

        public override int GetHashCode()
        {
            int hashCode = 1044908159;
            hashCode = hashCode * -1521134295 + cached.GetHashCode();
            hashCode = hashCode * -1521134295 + prone.GetHashCode();
            hashCode = hashCode * -1521134295 + regular.GetHashCode();
            return hashCode;
        }

        public void Deconstruct(out bool item1, out float item2, out float item3)
        {
            item1 = cached;
            item2 = prone;
            item3 = regular;
        }

        public static implicit operator (bool, float, float)(TerrainDetailScore value)
        {
            return (value.cached, value.prone, value.regular);
        }

        public static implicit operator TerrainDetailScore((bool, float, float, float, float) value)
        {
            return new TerrainDetailScore(value.Item1, value.Item2, value.Item3);
        }
    }
}