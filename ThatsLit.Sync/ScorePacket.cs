using LiteNetLib.Utils;

namespace ThatsLit.Sync
{
    public struct ScorePacket : INetSerializable
    {
        public int netId;
        public float score, ambienceScore;

        public ScorePacket(int netId, float score, float ambScore) : this()
        {
            this.netId = netId;
            this.score = score;
            this.ambienceScore = ambScore;
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