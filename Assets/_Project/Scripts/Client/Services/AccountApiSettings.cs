using UnityEngine;
namespace PokeChess.Client.Services
{
    [CreateAssetMenu(menuName = "PokeChess/Account API Settings")]
    public sealed class AccountApiSettings : ScriptableObject
    {
        [Tooltip("HTTPS API base URL. Empty in Web builds uses the page origin. Contains no secrets.")]
        public string BaseUrl = "";
        public string UgsEnvironment = "production";
        [Tooltip("Editor only: allow HTTP to loopback for a local development server. Ignored in all player builds.")]
        public bool AllowLoopbackHttpInEditor;
    }
}
