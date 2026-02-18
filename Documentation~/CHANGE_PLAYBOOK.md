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


## 1. 신규 노드 추가(Action/Condition)

1) 노드 타입 정의(입력/출력, 블랙보드 키)
2) 런타임 평가/실행 구현
3) Editor 표현(노드 UI/포트/라벨) 추가
4) 샘플 트리에 배치하여 검증
5) 테스트
- [ ] 성공/실패/진행 중 전이
- [ ] 중단 시 정리(타이머/코루틴/이동 요청)

---

## 2. 트리 로딩/배포 변경(Addressables)

1) Addressables 키/그룹 정책 확인
2) 로딩 실패 시 fallback/에러 로그
3) Release/해제 정책 확인
4) 테스트
- [ ] 씬 전환/재시작 반복
- [ ] 메모리 누수(Release 누락) 점검

---

## 3. Skill Test/시뮬레이션 모드와의 연동

- 스킬 테스트 중 BT 비활성 등 “테스트 환경 격리”가 필요하면,
  Runner에 Suspend/Resume 훅을 제공하고, 테스트 툴에서 토큰 기반으로 제어합니다.
