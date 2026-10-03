using UnityEngine;

namespace PokeChess.Client.Bootstrap
{
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public bool IsInitialized { get; private set; }

        private void Awake()
        {
            IsInitialized = true;
            Debug.Log("PokeChess bootstrap initialized.", this);
        }
    }
}
