using UnityEngine;

namespace PokeChess.Autobattler
{
    public class SkillProjectileView : MonoBehaviour
    {
        [SerializeField] private Vector3 visualOffset;

        private Vector3 _segmentStart;
        private Vector3 _segmentEnd;
        private float _segmentDuration = 0.1f;
        private float _segmentStartTime;
        private bool _hasSegment;

        public void Initialize(Vector3 worldPosition)
        {
            _segmentStart = worldPosition + visualOffset;
            _segmentEnd = _segmentStart;
            _segmentDuration = 0.1f;
            _segmentStartTime = Time.time;
            _hasSegment = true;
            transform.position = _segmentStart;
        }

        public void SetSegment(Vector3 from, Vector3 to, float durationSeconds)
        {
            _segmentStart = from + visualOffset;
            _segmentEnd = to + visualOffset;
            _segmentDuration = Mathf.Max(0.01f, durationSeconds);
            _segmentStartTime = Time.time;
            _hasSegment = true;
            transform.position = _segmentStart;

            Vector3 direction = _segmentEnd - _segmentStart;
            if (direction.sqrMagnitude > 0.0001f)
                transform.right = direction.normalized;
        }

        private void Update()
        {
            if (!_hasSegment)
                return;

            float t = Mathf.Clamp01((Time.time - _segmentStartTime) / _segmentDuration);
            transform.position = Vector3.Lerp(_segmentStart, _segmentEnd, t);
        }
    }
}
