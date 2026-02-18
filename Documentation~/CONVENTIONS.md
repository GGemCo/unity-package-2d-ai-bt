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


## 1. 노드 규칙

- 노드는 단일 책임(감지/결정/실행)을 지킵니다.
- Update 당 할당/탐색을 피하고, 블랙보드 캐시를 활용합니다.
- 실패/성공/진행 중 상태를 명확히 합니다.

## 2. 블랙보드 규칙

- 키는 상수/enum으로 중앙화합니다.
- 값 타입은 명확히(Nullable/기본값) 하고, 키 누락 시 안전하게 처리합니다.

## 3. Tick 정책

- 너무 잦은 Tick은 성능을 악화시킵니다.
- 감지(센서)와 의사결정(트리 평가) 주기를 분리하는 것을 권장합니다.

## 4. 외부 시스템 호출

- 이동은 Control, 스킬은 Skill, 상태효과는 Affect의 API를 호출합니다.
- Runner/Node에서 직접 캐릭터 내부 상태를 변경하지 않습니다.

## 5. Editor 디버그

- 실행 중인 노드 하이라이트, 블랙보드 값 표시를 제공하면 유지보수성이 크게 향상됩니다.
