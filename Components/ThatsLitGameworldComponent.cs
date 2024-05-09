using Comfort.Common;
using EFT;
using ThatsLit.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ThatsLit.Components
{
    public class ThatsLitGameworldComponent : MonoBehaviour
    {
        private void Awake()
        {
        }

        private void Update()
        {
            if (Singleton<GameWorld>.Instantiated && ThatsLitMainPlayer == null && ThatsLitMainPlayerComponent.CanLoad())
                ThatsLitMainPlayer = ComponentHelpers.AddOrDestroyComponent(ThatsLitMainPlayer, GameWorld?.MainPlayer);
        }

        private void OnDestroy()
        {
            try
            {
                ComponentHelpers.DestroyComponent(ThatsLitMainPlayer);
            }
            catch
            {
                Logger.LogError("Dispose Component Error");
            }
        }

        public GameWorld GameWorld => Singleton<GameWorld>.Instance;
        public ThatsLitMainPlayerComponent ThatsLitMainPlayer { get; private set; }
    }

}
