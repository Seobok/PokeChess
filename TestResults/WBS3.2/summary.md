# WBS 3.2 검증 결과

- 완료일: 2026-10-06 (Asia/Seoul)
- Unity: 6000.3.10f1
- 전체 Core EditMode 테스트: **616/616 통과**, 실패/건너뜀 0, 191.51초.
- 신규 테스트: PairingSystemTests 20/20, MultiPlayerRoundTests 13/13.
- 집중 회귀: LocalRoundCoordinatorTests 17/17, RoundFlowControllerTests 31/31, AutomaticBoardDeploymentTests 10/10.
- 4·6·8인 각 10라운드 타이머 자동 진행, 모든 Pair 완료 후 플레이어별 한 번 정산 검증.
- 매칭 점수의 전역 최적성은 별도 비트마스크 계산과 대조했다.

## 실제 UI 검증

PlacementSandbox의 실제 PlacementSandboxView.AdvanceRoundTime 경로로 확인했다.
테스트 중 MonoBehaviour Update를 잠시 중지하고 직접 경과 시간을 공급했으며, 동일한 자동 타이머와 30Hz 전투 경로를 실행했다.

- 8인 준비: Bench에 유닛 하나, Board 0/1, 30초 타이머, 상대 비공개.
- 30초 후: 각 플레이어 자동 배치, 4개 Pair 전투, 본인 상대 공개.
- 전투 완료: 본인 상대/승패, 8명 수입/XP, DEV 4/4 completed.
- 4·6·8인 각 3라운드 자동 반복 및 다음 Preparation 확인.
- p1 상대: 4인 p4 → p3 → p2, 6인 p6 → p5 → p2, 8인 p3 → p8 → p6.
- 본인 보드를 비운 4인 사례: 본인 Pair의 패배 확정 뒤 다른 Pair를 기다림.
  전체 Result/정산은 아직 발생하지 않으며 DEV 1/2 completed 표시.
- 준비·전투·결과 PNG를 직접 확인했다.
- 확인 후 timeScale=1, runInBackground=false로 복구하고 Play 종료.

## Windows 빌드

- 결과: **Succeeded**, 오류 0, 경고 2, 19.076초.
- 실행 파일: ../../Builds/WBS3.2/PokeChessPairing.exe
- Scene: Assets/_Project/Scenes/PlacementSandbox.unity
- 옵션: StandaloneWindows64 / Development / DetailedBuildReport
- 기존 환경 경고: RuntimePipelineConfig 미설정으로 Player의 Unity Pipeline 비활성화,
  Unity native symbol 업로드 연결 실패. 빌드 보고서에 원문을 저장했다.
- 별도 Player 실행은 검증 범위에 포함하지 않았으며, 실제 UI 동작은 Editor Play에서 확인했다.

## 저장 파일

- Full-Core-616.json
- PairingSystemTests.json
- MultiPlayerRoundTests.json
- LocalRoundCoordinatorTests.json
- RoundFlowControllerTests.json
- AutomaticBoardDeploymentTests.json
- UI-Integration.json
- 8-player-preparation.png
- 8-player-combat.png
- 8-player-result.png
- Windows-BuildReport.json

## 후속 작업

홀수 생존자는 RequiresShadow / UnpairedPlayerId까지 구현했다. 실제 Shadow 전투는 WBS 3.3 범위다.
0·1인 생존자의 최종 승자/Finished 전환은 WBS 3.6에서 연결한다.
플레이어 HP 피해·탈락은 이번 구현에서 자동 적용하지 않는다.

