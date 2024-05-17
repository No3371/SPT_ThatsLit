using Comfort.Common;
using EFT;
using System.Collections.Generic;
using UnityEngine;

namespace ThatsLit
{
    public class GameWorldHandler
    {
        public static void Update()
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null && ThatsLitGameWorld != null)
            {
                Object.Destroy(ThatsLitGameWorld);
            }
            else if (gameWorld != null && ThatsLitGameWorld == null)
            {
                ThatsLitGameWorld = gameWorld.GetOrAddComponent<ThatsLitGameworld>();
            }
        }

        public static ThatsLitGameworld ThatsLitGameWorld { get; private set; }
    }
}
