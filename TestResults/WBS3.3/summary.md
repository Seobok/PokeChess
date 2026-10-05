# WBS 3.3 검증 결과

- 완료일: 2026-10-06 (Asia/Seoul)
- Unity: 6000.3.10f1
- 전체 Core EditMode 테스트: **640/640 통과**, 실패/건너뜀 0, 197.85초.
- 신규 ShadowBattleTests: **24/24 통과**.
- 집중 회귀: MultiPlayerRoundTests 13/13, PairingSystemTests 20/20,
  LocalRoundCoordinatorTests 17/17, RoundFlowControllerTests 31/31,
  AutomaticBoardDeploymentTests 10/10.
- 기존 홀수 거부 3개 사례는 실제 Shadow 전투·전원 정산 검증으로 갱신했다.

## 자동 테스트에서 확인한 동작

3·5·7인 각각 10라운드 자동 전투와 모든 실제 생존자의 단일 결과·수입·XP를 확인했다.
원본의 일반 전투와 Shadow 전투는 유닛 HP·에너지·Shield·타겟 상태·아이템·RNG가 독립적이다.
원본 일반 패배/Shadow 승리 및 원본 일반 승리/Shadow 패배 모두 원본 자신의 결과만 정산한다.

빈 Shadow 보드의 Win/Loss/Draw, 느린 일반/Shadow 전투 대기, 최근 2라운드 원본 회피,
입력 순서·Seed 결정성, 원본 제거 후 불변 스냅샷 재사용, 정산 실패 재시도를 검증했다.
일반 전투 생성 뒤 Shadow Capture 또는 Shadow Battle 생성이 실패해도 모든 계획 배치를 보존한다.

## 실제 UI 확인

Editor Play의 실제 PlacementSandboxView.AdvanceRoundTime 경로를 사용했다.
검증 중 Update를 일시 중지하고 직접 경과 시간을 공급했으며, 동일한 자동 타이머·30Hz 전투 경로를 실행했다.

- 3 players / 5 players / 7 players 버튼의 활성 상태와 onClick 연결 확인.
- 각각 3라운드 자동 배치 → 일반/Shadow 전투 → 전원 수입/XP → 다음 Preparation 확인.
- 준비 화면에서 상대와 Shadow 예정 여부 비공개.
- 3인 2라운드의 본인 Shadow 화면: VS p3 (SHADOW), 대상 p1 / 원본 p3.
- 본인 결과 화면: Shadow 승리, 수입 +7G, 자동 XP +2. 원본 p3는 일반 전투 패배로 정산.
- 7인 1라운드에서 p1이 Shadow 원본일 때 본인 화면은 VS p7 일반 전투를 표시.
  DEV 목록에 p2 vs p1 (S)를 별도 표시하며 전체 4개 전투를 보여준다.
- 본인 Shadow 보드가 빈 경우 Loss / WAITING FOR OTHER PAIRS 표시.
  나머지 일반 전투가 끝나기 전 Result와 해당 라운드 정산은 발생하지 않는다.
- 준비·Shadow 전투·Shadow 결과·7인 전투 PNG를 직접 확인했다.
- 검증 후 timeScale=1, runInBackground=false로 복구하고 Play 종료.

## Windows 빌드

- 결과: **Succeeded**, 오류 0, 경고 2, 19.469초.
- 실행 파일: ../../Builds/WBS3.3/PokeChessShadowBattle.exe
- Scene: Assets/_Project/Scenes/PlacementSandbox.unity
- 옵션: StandaloneWindows64 / Development / DetailedBuildReport
- 기존 환경 경고: RuntimePipelineConfig 미설정으로 Player의 Unity Pipeline 비활성화,
  Unity native symbol 업로드 연결 실패. 원문은 Windows-BuildReport.json에 저장했다.
- 별도 Player 실행은 검증하지 않았으며 실제 UI 동작은 Editor Play에서 확인했다.
- 최종 Editor 컴파일 실패 false, Console 오류 0.

## 저장 파일

- Full-Core-640.json
- ShadowBattleTests.json
- MultiPlayerRoundTests.json
- PairingSystemTests.json
- LocalRoundCoordinatorTests.json
- RoundFlowControllerTests.json
- AutomaticBoardDeploymentTests.json
- UI-Integration.json
- UI-ShadowCombat.json
- UI-ShadowResult.json
- UI-SourcePerspective.json
- UI-ShadowWaiting.json
- 3-player-shadow-preparation.png
- 3-player-shadow-combat.png
- 3-player-shadow-result.png
- 7-player-combat.png
- Windows-BuildReport.json

## 후속 작업

Shadow 패배자의 플레이어 HP 피해는 3.4에서 연결한다.
준비 중 항복의 재매칭과 항복 직전 스냅샷 선택은 후속 항복 작업에서 연결한다.
불변 Snapshot Capture / CreateBattle API와 원본 제거 후 재사용 검증은 이번 작업에 포함했다.
0·1인 생존자의 최종 승자 / Finished 전환은 3.6 범위다.

