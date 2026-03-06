using System.Collections.Generic;
using Fusion;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// 플레이어별 최근 전투 상대(최대 N명)를 기록.
    /// 서버에서만 갱신하는 용도 권장.
    /// </summary>
    public sealed class RecentOpponentHistory
    {
        private readonly int _capacity;
        private readonly Dictionary<PlayerRef, Queue<PlayerRef>> _recent = new();

        public RecentOpponentHistory(int capacity = 4)
        {
            _capacity = capacity;
        }

        public IReadOnlyCollection<PlayerRef> GetRecent(PlayerRef player)
        {
            if (_recent.TryGetValue(player, out var q)) return q;
            return System.Array.Empty<PlayerRef>();
        }

        public bool HasMetRecently(PlayerRef a, PlayerRef b)
        {
            if (_recent.TryGetValue(a, out var qa))
            {
                foreach (var x in qa)
                    if (x == b) return true;
            }
            return false;
        }

        public void RecordMatch(PlayerRef a, PlayerRef b)
        {
            if (a == PlayerRef.None || b == PlayerRef.None) return;
            Push(a, b);
            Push(b, a);
        }

        private void Push(PlayerRef owner, PlayerRef opponent)
        {
            if (!_recent.TryGetValue(owner, out var q))
            {
                q = new Queue<PlayerRef>(_capacity);
                _recent[owner] = q;
            }

            // 이미 있으면(연속 매칭 등) 중복을 제거하고 최신으로 보내고 싶을 수 있음.
            // 여기선 단순히 허용하되, 원하면 "중복 제거" 로직을 추가해도 됨.

            q.Enqueue(opponent);
            while (q.Count > _capacity)
                q.Dequeue();
        }

        public void RemovePlayer(PlayerRef player)
        {
            _recent.Remove(player);
            // 다른 플레이어 큐에 남아있는 player를 제거하고 싶다면 추가 정리 가능(필수는 아님)
        }
    }
}