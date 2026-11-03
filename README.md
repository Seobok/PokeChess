# PokeChess

Unity 기반 2~8인 방 방식 오토배틀러 프로젝트다. Host가 경기·전투를 판정하고 클라이언트는 입력과 표현을 담당한다.

- [전체 문서 안내](../Docs/README.md)
- [경기 규칙](../Docs/Rules/Match.md) · [전투](../Docs/Rules/Combat.md) · [스킬](../Docs/Rules/Skills.md) · [상태이상](../Docs/Rules/Statuses.md)
- [콘텐츠 작성](../Docs/Development/Content_Authoring.md) · [네트워크](../Docs/Development/Networking.md) · [UI와 표현](../Docs/Development/Presentation.md)
- [검증 방법과 기록 범위](../Docs/Development/Validation.md) · [후속 결정](../Docs/Development/Open_Decisions.md)

Unity에서 이 폴더를 프로젝트로 열고 `Assets/_Project/Scenes/Boot.unity` 또는 Sandbox 메뉴를 사용한다. 현재 엔진/패키지 버전은 `ProjectSettings/ProjectVersion.txt`와 `Packages/manifest.json`을 확인한다.

실제 데이터는 `Assets/_Project/Resources/GameRoster.asset`과 단일 원본 `Assets/_Project/Content/SkillPlan.json`이다. PrototypeRoster는 테스트용이며 현재 로스터 기획의 기준이 아니다.
