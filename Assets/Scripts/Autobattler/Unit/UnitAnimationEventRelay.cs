using UnityEngine;

namespace PokeChess.Autobattler
{
    [DisallowMultipleComponent]
    public sealed class UnitAnimationEventRelay : MonoBehaviour
    {
        [SerializeField] private UnitController owner;

        private void Awake()
        {
            owner ??= GetComponentInParent<UnitController>();
        }

        public void OnGeneratedAnimationEvent(string eventName)
        {
            owner ??= GetComponentInParent<UnitController>();
            if (owner == null || string.IsNullOrWhiteSpace(eventName))
                return;

            if (!System.Enum.TryParse(eventName, true, out UnitAnimationClipEventType eventType))
                return;

            owner.NotifyAnimationClipEvent(eventType);
        }
    }
}
