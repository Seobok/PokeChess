using Fusion;
using UnityEngine;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// 서버 권한에서 유닛 NetworkObject를 생성하고 초기 배치합니다.
    /// </summary>
    public class UnitSpawner : NetworkBehaviour
    {
        [SerializeField] private NetworkPrefabRef unitPrefab;
        [SerializeField] private BoardManager boardManager;

        public override void Spawned()
        {
            if (boardManager == null)
            {
                boardManager = FindObjectOfType<BoardManager>();
            }
        }

        public UnitController SpawnUnit(HexCoord spawnCell)
        {
            if (Runner == null || Runner.IsServer == false || boardManager == null)
            {
                return null;
            }

            NetworkObject unitObject = Runner.Spawn(unitPrefab, Vector3.zero, Quaternion.identity);
            if (unitObject == null)
            {
                return null;
            }

            UnitController unit = unitObject.GetComponent<UnitController>();
            if (unit == null || unit.InitializeAt(spawnCell) == false)
            {
                Runner.Despawn(unitObject);
                return null;
            }

            return unit;
        }
    }
}
