# Photon Fusion 기반 오토배틀러(TFT 레퍼런스) 인게임 로직 구현 가이드

## 1) 전체 구조 제안

- **전투는 Host/Server Authority(리스너 서버 기준)** 로 고정합니다.
- 리슨서버(Host Mode)에서는 **한 명의 플레이어가 Host가 되어 서버 시뮬레이션을 담당**하고, 나머지 플레이어는 Client로 접속합니다.
- 매 Tick(`FixedUpdateNetwork`)에서 아래 순서로 처리합니다.
  1. 타겟 탐색
  2. 이동 결정(A*)
  3. 공격 가능 여부 판정
  4. 공격/피해/마나 처리
  5. 사망 처리
- 클라이언트는 결과를 보간/연출만 수행하고, **판정은 항상 네트워크 권한 객체**에서 처리합니다.

### 1-1. 리슨서버 방 생성/참여 규칙

- 게임 시작 버튼을 누른 플레이어가 `GameMode.Host`로 세션을 시작합니다.
- 다른 플레이어는 동일 세션에 `GameMode.Client`로 조인합니다.
- 호스트 이탈 시 매치 중단 또는 재호스팅 정책이 필요합니다(초기 MVP에서는 매치 중단 권장).

```csharp
// 예시: 방 생성(호스트)
await runner.StartGame(new StartGameArgs
{
    GameMode = GameMode.Host,
    SessionName = roomCode,
    Scene = SceneRef.FromIndex(activeSceneIndex),
    SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
});

// 예시: 방 참가(클라이언트)
await runner.StartGame(new StartGameArgs
{
    GameMode = GameMode.Client,
    SessionName = roomCode
});
```

---

## 2) 맵 설계 (7 x 8 육각 타일)

## 2-1. 좌표계

Hex는 **Axial 좌표(q, r)** 를 추천합니다.

- 저장은 `HexCell[q, r]` 또는 `Dictionary<HexCoord, HexCell>`
- 인접 6방향:
  - `( +1,  0 )`
  - `( +1, -1 )`
  - `(  0, -1 )`
  - `( -1,  0 )`
  - `( -1, +1 )`
  - `(  0, +1 )`

맵 크기(가로 7, 세로 8)는 내부 인덱스 검증 함수로 제한합니다.

```csharp
public bool IsInside(HexCoord c) => c.Q >= 0 && c.Q < 7 && c.R >= 0 && c.R < 8;
```

## 2-2. 배치 가능 구역(아래쪽 7 x 4)

플레이어 기준 하단 절반만 배치 가능:

```csharp
public bool IsDeployZone(HexCoord c) => IsInside(c) && c.R >= 4; // 0~7 기준 하단 4줄
```

> 상대 진영은 `c.R <= 3`로 대칭 처리하거나, 플레이어별로 로컬-월드 변환 함수(미러링)를 둡니다.

## 2-3. 타일 점유 규칙

- 타일당 1유닛: `HexCell.OccupantNetId`(없으면 `None`)
- 이동 예약 충돌 방지:
  - 같은 Tick에 여러 유닛이 같은 목적지를 선택하면
  - 우선순위(거리 짧음 → 유닛 ID 낮음 등)로 1명만 승인

---

## 3) 유닛 데이터 모델

`UnitStats`(SO 또는 테이블) + `NetworkUnitState`(Fusion Networked 프로퍼티) 이원화 추천

- 정적 스탯: 공격력, 최대체력, 최대마나, 이동속도, 사거리, 방어력, 공격속도
- 동적 상태: 현재체력, 현재마나, 현재타일, 현재타겟, 쿨다운, 생존 여부

피해 계산 예시:

```text
finalDamage = max(1, AttackPower - Defense)
```

마나 규칙(예시):
- 기본 공격 적중 시 +10
- 피격 시 +5

---

## 4) 타겟 선정 로직

요구사항: **가까운 적 우선 공격**

1. 살아있는 적 목록 필터
2. Hex 거리 계산(`hexDistance`) 최소값 선택
3. 동률이면
   - 현재 타겟 유지(타겟 스위칭 최소화)
   - 그 다음 네트워크 ID 낮은 순

Hex 거리(axial):

```csharp
int HexDistance(HexCoord a, HexCoord b)
{
    int dx = a.Q - b.Q;
    int dz = a.R - b.R;
    int dy = -dx - dz;
    return (Mathf.Abs(dx) + Mathf.Abs(dy) + Mathf.Abs(dz)) / 2;
}
```

---

## 5) 이동 로직 (최선 동선 + 막히면 정지)

요구사항 핵심: **상대를 공격하기 위한 최단 경로로 이동**, **경로가 완전히 막히면 이동하지 않음**

### 5-1. 경로 탐색

- 알고리즘: A* (휴리스틱 = HexDistance)
- 이동 가능 조건:
  - 맵 내부
  - 비점유 타일 (목표가 적 점유 타일일 경우, 그 인접 타일까지를 목표로 설정)

### 5-2. 목적지 정의

- 적 유닛 타일 자체가 아니라, **적을 공격 가능한 타일 집합**을 만듭니다.
- 즉 `HexDistance(candidate, enemyPos) <= Range` 인 타일들 중
  - 점유되지 않았고
  - 도달 가능한 타일
  - 비용 최소 타일 선택

### 5-3. 막힘 처리

- A* 결과 없음 ⇒ `Idle` 유지
- 다음 Tick에 재탐색 (전장 상황 변동 대응)

---

## 6) 공격 로직

Tick 루프에서:

1. 타겟 유효성 검사(생존/거리)
2. 사거리 내면 공격 쿨다운 확인 후 공격
3. 사거리 밖이면 이동 단계 수행

공격 처리:
- 데미지 적용
- 마나 증가
- 죽음 체크(HP <= 0)
- 처치 시 타겟 해제 및 즉시 재탐색 플래그 설정

---

## 7) Fusion 구현 포인트

## 7-1. 네트워크 객체 분리

- `BattleManager`(NetworkBehaviour): 라운드 상태, Tick 진행, 승패 판정
- `BoardManager`(NetworkBehaviour/Singleton): Hex 셀 점유 정보
- `UnitController`(NetworkBehaviour): 유닛 상태 + 의사결정

### 7-1-1. 리슨서버에서의 권한 처리 원칙

- `Runner.IsServer == true`(호스트)에서만 전투 판정, 경로 계산, 데미지 계산을 수행합니다.
- 클라이언트는 입력(배치 요청 등)만 RPC/Input으로 전달하고, 상태는 `Networked<>` 동기화 결과를 사용합니다.
- 유닛 생성/제거는 호스트만 `Runner.Spawn / Runner.Despawn` 호출합니다.

```csharp
public override void FixedUpdateNetwork()
{
    if (Runner.IsServer == false)
        return; // 리슨서버에서 Host만 시뮬레이션 수행

    SimulateCombatTick();
}
```

## 7-2. Networked 속성 예시

- `Networked<int> HP`
- `Networked<int> Mana`
- `Networked<NetworkId> TargetId`
- `Networked<HexCoordNet> Cell`
- `Networked<TickTimer> AttackCooldown`

> 경로 전체(List)는 매 Tick 동기화하지 말고, 서버에서 계산 후 **다음 한 칸 이동만 상태로 반영**하는 방식이 대역폭에 유리합니다.

## 7-3. 결정론/재현성

- 랜덤 필요 시 `NetworkRNG`/시드 고정
- 물리 기반 충돌 대신 타일 점유 규칙으로 판정
- 클라 연출(투사체, 이펙트)은 RPC/State 변경 이벤트 기반

---

## 8) 추천 개발 순서 (MVP)

1. Hex 보드 + 배치 제한(하단 4줄)
2. 단일 유닛 이동(A*)
3. 1:1 전투(타겟/사거리/공격)
4. 다수 유닛 충돌 및 점유 우선순위
5. 스킬/마나/시너지 확장

---

## 9) 최소 의사코드

```csharp
// BattleManager.FixedUpdateNetwork()
foreach (var unit in aliveUnits)
{
    unit.RefreshTargetIfNeeded();

    if (unit.HasTarget == false)
        continue;

    if (unit.InAttackRange(unit.Target))
    {
        unit.TryAttack();
    }
    else
    {
        var next = pathService.GetNextStepTowardAttackableCell(unit, unit.Target);
        if (next.HasValue)
            board.TryMove(unit, next.Value);
    }
}
```

---

## 10) 검증 체크리스트

- 배치: 하단 4줄 이외 배치 차단
- 점유: 한 타일 1유닛 보장
- 경로: 장애물 배치 시 우회, 완전 봉쇄 시 정지
- 타겟: 항상 가장 가까운 적 우선
- 네트워크: Host와 Client의 HP/위치/사망 상태 일치

이 순서로 구현하면, TFT 스타일의 기본 오토배틀 루프(배치 → 자동 이동/공격 → 전투 종료)를 Photon Fusion 환경에서 안정적으로 확장할 수 있습니다.
