# G6.1 Disconnect / Reconnect

non-Host 단절 시 기존 경기 참가 상태를 120초 유지하며, 같은 인증 PlayerId로 전체 Snapshot과 현재 전투를 복구한다. 복구 중 입력 차단, 미확인 명령 재확인, 유예 만료 탈락, Host 상실 종료를 지원한다. [G6.1 구현·검증 기록](../Docs/WBS_G6/PokeChess_Reconnect_WBS_6.1.md).

# 기존 클라이언트 온라인 통합

기존 상점·드래그 배치·유닛 정보·HUD·전투·결과 화면을 온라인 Host 상태 및 Command/Ack에 연결했다. 상대 보드와 다른 대진 관전, 탈락 후 관전, 최종 결과에서 방 복귀를 지원한다. [구현·검증 기록](../Docs/WBS_G5/PokeChess_Online_Client_Integration_2026-10-08.md).

# G5.7 Server Combat / Combat Event Playback

Host가 자동 라운드와 30 tick/s 전투를 실행하고, 각 참가자는 본인 전투 이벤트·유닛 애니메이션·투사체·결과를 재생한다. 재동기화와 누락 복구, Editor/Windows 양방향 전체 경기를 검증했다. [G5.7 구현·검증 기록](../Docs/WBS_G5/PokeChess_Server_Combat_WBS_5.7.md).

# G5.6 Public / OwnerOnly / HostOnly State Sync

공개 보드·플레이어 상태와 본인 상점·경제·벤치를 분리하고, 전체 snapshot 복구·버전 검사·상대 보드 관찰 UI를 추가했다. 경기 seed는 Host 내부에만 보관한다. [G5.6 구현·검증 기록](../Docs/WBS_G5/PokeChess_State_Sync_WBS_5.6.md). 자동 라운드·전투 스트림은 G5.7에서 연결했다.

# G5.5 Command / Validation / Mutation / Ack

Host 권한 명령 처리와 소유자 상태 응답, 중복 실행 방지 및 온라인 준비 명령 UI를 추가했다. [G5.5 구현·검증 기록](../Docs/WBS_G5/PokeChess_Command_Pipeline_WBS_5.5.md). 온라인 자동 라운드·전투 동기화는 G5.7에서 연결했다.

# G5.4 Lobby / Ready / Host Start

참가자 Ready/취소, Host 시작 조건 검사, 방 잠금·확인 응답·시작 취소와 공통 경기 시작 정보를 추가했다. [G5.4 구현·검증 기록](../Docs/WBS_G5/PokeChess_Lobby_Ready_Start_WBS_5.4.md). 온라인 경기 동기화는 후속 단계다.

# G5.3 Room List / Create / Join by Code

공개 방 목록·페이지 조회, 공개/비공개 방 생성, 목록 및 코드 참가, 참가 인원·Host·코드 복사·퇴장 UI를 추가했다. [G5.3 구현·검증 기록](../Docs/WBS_G5/PokeChess_Room_Flow_WBS_5.3.md). Ready와 Host Start는 5.4 범위다.

# G5.2 Online Connection

Sessions와 Relay를 통한 NGO 연결, 참가 코드 UI, 버전 검사 메시지 왕복, 퇴장·재참가 및 연결 상실 처리를 추가했다. [G5.2 구현·검증 기록](../Docs/WBS_G5/PokeChess_Online_Connection_WBS_5.2.md).

# G5.1 Anonymous Authentication

Boot에서 UGS 익명 인증을 시작하고 재실행 시 저장된 세션으로 PlayerId를 복원한다. 상태·재시도 UI와 로컬 경기 독립 실행을 제공한다. [G5.1 구현·검증 기록](../Docs/WBS_G5/PokeChess_Anonymous_Authentication_WBS_5.1.md). 두 PC 최종 검증은 대기 중이다.

# G4.10 Content Alpha QA

12종 × 3성급 스킬 검사, 기본 경기 20회와 레벨 7 콘텐츠 보완 경기 3회, 회귀 검사 및 UI/Windows 빌드 검증을 진행했다. 결과와 재실행 방법은 [G4.10 검증 기록](../Docs/WBS_G4/PokeChess_Content_Alpha_QA_WBS_4.10.md)을 따른다.

# G4.9 Result / Elimination / Final Result UI

라운드 승패·HP·보상 요약과 상세 펼치기, 탈락 안내/계속 보기, 최종 8인 순위표를 정리했다. 조기 전투 종료는 전체 정산을 기다리고 최종 결과는 뒤쪽 입력을 차단한다. [G4.9 구현·검증 기록](../Docs/WBS_G4/PokeChess_Result_UI_WBS_4.9.md).

# G4.8 Unit Context / Rank / Item UI

유닛 선택 정보에 역할·타입·성급별 능력치와 실제 스킬 수치를 표시한다. 연쇄/지연 합성 선택 유지와 아이템 반환 피드백, 내 전투 유닛 클릭 선택을 추가했다. [G4.8 구현·검증 기록](../Docs/WBS_G4/PokeChess_Unit_Context_UI_WBS_4.8.md).

# G4.7 Shop / Economy UI

상점 카드 5개와 접힘 경제 헤더·XP 바·잠금·증감 피드백을 정리했다. 기존 Combat 구매·지연 합성 정책을 유지한다. [G4.7 구현·검증 기록](../Docs/WBS_G4/PokeChess_Shop_Economy_UI_WBS_4.7.md).

# G4.6 Match HUD

Round 배지·타이머 바·플레이어 HP/상태 프로필을 추가했다. Play에서 `8 players` 또는 기존 로스터 데모로 확인한다. [G4.6 구현·검증 기록](../Docs/WBS_G4/PokeChess_Match_HUD_WBS_4.6.md).

# G4.3 P09~P12

코일·꼬부기·캐이시·파이리 초기 수치와 구현 기준은 [G4.3 로스터](../Docs/WBS_G4/PokeChess_High_Cost_Roster_WBS_4.3.md)를 따른다. Play → `P09-P12 demo` → `DEV: Start battle`로 확인한다.

# PokeChess

G4.2 꼬마돌·고오스·캐터피·이상해씨의 초기 수치 및 기본형 스킬 구현 기준은 [G4.2 로스터](../Docs/WBS_G4/PokeChess_Advanced_Roster_WBS_4.2.md)를 따른다. Play에서 `P01-P04 demo`와 `P05-P08 demo`를 선택해 확인한다.

현재 개발은 기본 경기와 성급 합성 검증을 우선한다. 진화는 이후 개발로 이관했으며, 범위와 WBS 변경은 [진화 개발 후속 이관](../Docs/PokeChess_Evolution_Deferred_2026-10-06.md)을 따른다.

G4.1 야돈·망키·뿔충이·럭키의 기본형 스킬을 구현했다. 성급별 수치, 데모 실행 방법과 검증 상태는 [G4.1 구현 내역](../Docs/WBS_G4/PokeChess_Base_Roster_Skills_WBS_4.1.md)을 참고한다.
