using LiteNetLib.Utils;

namespace ThatsLit.Sync
{
    public struct ScorePacket : INetSerializable
    {
        public int netId;
        public float score, ambienceScore;
        private object netId1;
        private float ambScore;

        public ScorePacket(object netId1, float score, float ambScore) : this()
        {
            this.netId1 = netId1;
            this.score = score;
            this.ambScore = ambScore;
        }

        public void Deserialize(NetDataReader reader)
        {
            netId = reader.GetInt();
            score = reader.GetFloat();
            ambienceScore = reader.GetFloat();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(netId);
            writer.Put(score);
            writer.Put(ambienceScore);
        }
    }
}