# AI BT 문서

이 폴더는 **AI BT 패키지**의 구조/규칙/변경 절차를 표준화하기 위한 문서입니다.

- Runtime 네임스페이스: `GGemCo2DAiBt`
- Editor 네임스페이스: `GGemCo2DAiBtEditor`

Unity 공식 문서 참고 링크:
- Assembly Definition(런타임/에디터 분리): https://docs.unity3d.com/6000.3/Documentation/Manual/cus-asmdef.html
- ScriptableObject(데이터 컨테이너/저장 특성): https://docs.unity3d.com/6000.3/Documentation/Manual/class-ScriptableObject.html
- EditorWindow(커스텀 툴): https://docs.unity3d.com/6000.3/Documentation/ScriptReference/EditorWindow.html
- EditorWindow(UI Toolkit 가이드): https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-HowTo-CreateEditorWindow.html
- Addressables(패키지): https://docs.unity3d.com/Packages/com.unity.addressables%40latest/
- Addressables(개요): https://docs.unity3d.com/Packages/com.unity.addressables%401.24/manual/AddressableAssetsOverview.html
- Undo(에디터 Undo/Redo): https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Undo.html
- Serialization(직렬화 규칙): https://docs.unity3d.com/Manual/script-Serialization.html


## 1. 역할

AI BT 패키지는 몬스터/AI의 Behavior Tree 실행을 담당합니다.

- BT 노드/트리 구성(`BT/`)
- 런타임 실행기(Runner) 및 블랙보드/컨텍스트
- Config로 AI 튜닝
- Addressables/부트스트랩(트리 로딩)
- Editor: 트리 편집/디버그/테스트 도구

## 2. 구성 개요

- `BT/` : 노드, 컴포지트, 데코레이터, 액션 노드 등
- `Config/` : 트리/파라미터 설정
- `Core/` : 런타임 실행 기반(컨텍스트/블랙보드)
- `Bootstrapper/`, `AddressableLoader/` : 로딩/초기화

Editor(AiBtEditor)는 트리 편집/가시화/테스트를 담당합니다.

## 3. 실행 흐름(표준)

1) 트리 로딩(Addressables 또는 리소스)
2) Runner가 Tick(프레임 또는 고정 주기)로 트리 평가
3) 노드가 블랙보드 값 읽기/쓰기
4) 액션 노드가 Core(Control/Skill 등) 표준 API 호출
5) 종료/중단 시 노드 정리(리셋)

## 4. 확장 포인트(권장)

- 새로운 액션 노드는 “Core/Control/Skill의 표준 진입점”만 호출합니다.
- BT는 게임 규칙(데미지 계산 등)을 직접 구현하지 않습니다.
