# AI BT 패키지 중요한 클래스 정리

이 문서는 업로드된 `ai_bt_runtime.zip`, `ai_bt_editor.zip` 소스를 기준으로, **AI BT 패키지에서 핵심적으로 봐야 하는 클래스**를 Runtime / Editor 로 나누어 정리한 문서입니다.

AI BT 패키지는 몬스터/AI의 Behavior Tree 실행을 담당하며, 런타임에서는 트리 평가와 블랙보드/디버그 상태를 관리하고, 에디터에서는 트리 작성, 검증, 디버깅, Addressables 설정을 담당합니다. 또한 패키지 의존성은 **Core ← Control ← Skill ← AI_BT** 방향을 따라야 하며, AI_BT는 하위 패키지의 표준 진입점만 호출하는 구조를 유지해야 합니다.

---

## 1. 패키지 개요

- **Runtime 네임스페이스**: `GGemCo2DAiBt`
- **Editor 네임스페이스**: `GGemCo2DAiBtEditor`
- **주요 역할**
  - Behavior Tree 데이터 정의
  - 몬스터 AI 트리 평가 및 실행
  - 런타임 블랙보드/디버그 상태 관리
  - Addressables 기반 BT 에셋 로딩
  - Editor 기반 BT 그래프 작성, 파라미터 편집, 검증, 디버그 확인

### 설계 관점에서 중요한 점

1. **런타임 실행 중심 클래스는 `MonsterBtRunner`** 입니다.  
   실제 게임 중 BT를 평가하고, 액션 노드를 통해 Control/Skill/Core 쪽 표준 API를 호출합니다.

2. **데이터 중심 클래스는 `MonsterBehaviorTreeAsset`** 입니다.  
   GraphView 자체를 저장하지 않고, 런타임/에디터 공용 직렬화 데이터만 보관합니다.

3. **에디터 중심 클래스는 `CreateBtWindow`** 입니다.  
   노드 생성, 그래프 연결, 인스펙터 편집, 런타임 디버그 표시를 한 곳에서 담당합니다.

4. **확장 시 기준점은 `BtTypeIds` + `BtNodeTypeCatalog` 조합** 입니다.  
   런타임이 이해하는 타입 ID와, 에디터가 생성/편집 가능한 타입 정의가 반드시 일치해야 합니다.

---

## 2. Runtime 중요한 클래스

## 2.1 최우선 핵심 클래스

### `MonsterBtRunner`
- **경로**: `BT/MonsterBtRunner.cs`
- **역할**: 몬스터 전투용 Behavior Tree 런타임 실행기
- **왜 중요한가**
  - AI BT 패키지의 실질적인 중심입니다.
  - Tick 주기(`_tickRateHz`)에 따라 트리를 평가합니다.
  - 현재 트리 에셋, 런타임 상태, 블랙보드, 디버그 정보, 브레이크포인트를 함께 관리합니다.
  - `IMonsterCombatDriver`, `IMonsterSkillDriver`, `IMonsterBrainSuspendProvider` 같은 하위 계층 인터페이스를 통해 실제 행동을 위임합니다.
- **핵심 포인트**
  - `SetTree()`로 런타임 중 BT 교체가 가능합니다.
  - 실행 중 교체 요청은 즉시 바꾸지 않고 지연 적용합니다.
  - 디버그 프레임/이벤트/메트릭/히스토리를 축적합니다.
  - 브레이크포인트, 프리즈, 스텝 실행을 지원합니다.
- **실무에서 볼 때 체크할 부분**
  - 새로운 노드 실행 규칙을 추가할 때 가장 먼저 확인해야 할 클래스입니다.
  - `UseSkillAndWait`, `RequestRestartRoot`, `RandomWeighted` 같은 BT 의미론이 실제로 반영되는 지점입니다.

### `MonsterBehaviorTreeAsset`
- **경로**: `BT/MonsterBehaviorTreeAsset.cs`
- **역할**: BT 정의를 담는 ScriptableObject 에셋
- **왜 중요한가**
  - 런타임과 에디터가 함께 사용하는 **단일 소스 오브 트루스**입니다.
  - 루트 노드 ID, 노드 목록, 블랙보드 스키마를 직렬화합니다.
- **핵심 포인트**
  - `rootNodeId`
  - `nodes`
  - `blackboardSchema`
  - `FindNode()` 제공
- **실무 메모**
  - GraphView 저장 데이터가 아니라, 순수 데이터 모델로 유지되고 있다는 점이 중요합니다.
  - 에디터 확장 시에도 이 클래스 자체를 UI 의존적으로 만들지 않는 것이 좋습니다.

### `RuntimeBlackboard`
- **경로**: `BT/RuntimeBlackboard.cs`
- **역할**: 스키마 기반 런타임 블랙보드 저장소
- **왜 중요한가**
  - BT의 조건/액션 노드가 읽고 쓰는 상태값 저장소입니다.
  - 단순 변수 저장소를 넘어서, 스키마 기반 초기화와 내부 인덱스 캐시를 제공합니다.
- **핵심 포인트**
  - `BlackboardSchema`를 기반으로 초기화됩니다.
  - bool/int/float/string/vector 값을 다룹니다.
  - 스키마 외 런타임 캐시로 스킬 사용 횟수도 관리합니다.
  - 트리 교체 시 `CopyCommonValuesFrom()` 기반 상태 유지가 가능합니다.
- **실무 메모**
  - 트리 교체 후 일부 상태를 이어가고 싶을 때 이 클래스의 복사 정책이 중요합니다.
  - Skill 사용 횟수 기반 조건 노드와 직접 연결됩니다.

### `BtRuntimeState`
- **경로**: `BT/BtRuntimeState.cs`
- **역할**: 노드별 런타임 실행 상태 저장
- **왜 중요한가**
  - Running 상태 유지, 데코레이터 타이머, 실행 중인 자식 추적 등 BT의 상태성 로직을 담는 기반입니다.
- **핵심 포인트**
  - 노드 단위 상태(`BtNodeState`)를 관리합니다.
  - 여러 Tick 사이에서 지속되는 실행 정보를 보관합니다.
- **실무 메모**
  - Running 유지가 필요한 Composite/Decorator/Action 의미론을 분석할 때 반드시 같이 봐야 합니다.

---

## 2.2 데이터 모델 / 공통 타입

### `BtNodeRecord`
- **경로**: `BT/BtNodeRecord.cs`
- **역할**: 개별 BT 노드의 직렬화 데이터 모델
- **중요 이유**
  - 노드 ID, 제목, 타입 ID, 부모/자식 연결, 파라미터 등을 담는 핵심 데이터 구조입니다.
  - 에디터 GraphView와 런타임 평가가 모두 이 구조를 기준으로 동작합니다.
- **언제 보나**
  - 노드 저장 포맷을 바꿀 때
  - 다중 부모, 자식 순서, 파라미터 구조를 조정할 때

### `BtParamValue`
- **경로**: `BT/BtParamValue.cs`
- **역할**: 노드 파라미터 Variant 값
- **중요 이유**
  - 다양한 타입의 파라미터를 공통 구조로 저장합니다.
  - 노드 파라미터 UI 자동 생성과 런타임 파라미터 해석을 이어주는 지점입니다.

### `BlackboardSchema` / `BlackboardKeyDef`
- **경로**: `BT/BlackboardSchema.cs`
- **역할**: 블랙보드 키 정의와 기본값 스키마
- **중요 이유**
  - 블랙보드 키를 무질서한 문자열 사전이 아니라, 명시적 스키마로 관리하게 해줍니다.
  - 초기값, 타입, 키 이름을 정의할 수 있습니다.

### `BtEnums`
- **경로**: `BT/BtEnums.cs`
- **역할**: BT 공통 enum / 타입 ID 정의
- **중요 이유**
  - `BtStatus`, `BtNodeKind`, `BtValueType`, `BtAbortMode` 같은 기반 규약을 제공합니다.
  - `BtTypeIds`가 런타임 노드 종류의 정식 식별자 역할을 합니다.
- **실무 메모**
  - 노드 추가 시 `BtTypeIds`와 Editor의 `BtNodeTypeCatalog`를 함께 수정해야 합니다.

### `BtDebugTypes`
- **경로**: `BT/BtDebugTypes.cs`
- **역할**: 디버그 프레임, 이벤트, 메트릭, 브레이크포인트 구조 정의
- **중요 이유**
  - 런타임 디버그 표시가 어떤 데이터 모델로 구성되는지 보여줍니다.
  - 디버깅 기능이 많은 패키지이므로, 구조 이해에 중요합니다.
- **주요 타입**
  - `BtDebugNodeResult`
  - `BtDebugMetric`
  - `BtDebugEvent`
  - `BtDebugFrame`
  - `BtDebugBreakpoint`
  - `BtDebugBreakInfo`

---

## 2.3 로딩 / 초기화 / 설정 관련 클래스

### `BootstrapperBt`
- **경로**: `Bootstrapper/BootstrapperBt.cs`
- **역할**: AI BT 패키지 초기 연결 담당
- **중요 이유**
  - 패키지가 게임 흐름에 어떻게 연결되는지 보여줍니다.
  - 코멘트 기준으로 Core의 캐릭터 생성 이벤트를 구독해 필요한 BT 관련 연결을 수행합니다.
- **언제 보나**
  - 몬스터 생성 시 Runner 자동 부착/초기화 흐름을 추적할 때

### `AddressableLoaderMonsterBt`
- **경로**: `AddressableLoader/AddressableLoaderMonsterBt.cs`
- **역할**: Addressables에서 몬스터 BT 트리 에셋을 로드하여 `MonsterBtRunner`에 적용
- **중요 이유**
  - 런타임에서 BT 에셋이 어디서, 어떤 방식으로 들어오는지 보여줍니다.
  - 트리 교체나 몬스터별 BT 주입 경로를 추적할 때 중요합니다.

### `AddressableLoaderSettingsAiBt`
- **경로**: `AddressableLoader/AddressableLoaderSettingsAiBt.cs`
- **역할**: AI BT 설정 ScriptableObject 로딩
- **중요 이유**
  - 디버그/틱 레이트 같은 설정값의 로딩 진입점일 가능성이 높습니다.

### `GGemCoAiBtSettings`
- **경로**: `ScriptableSettings/GGemCoAiBtSettings.cs`
- **역할**: AI BT 패키지 전역 설정
- **중요 이유**
  - Tick 주기, 디버그 옵션 등 패키지 전역 동작 정책을 담는 중심 설정 자산입니다.
- **실무 메모**
  - 러너의 기본 동작을 바꾸고 싶을 때 코드보다 먼저 확인해야 할 가능성이 높습니다.

### `AiBtPackageManager`
- **경로**: `Core/AiBtPackageManager.cs`
- **역할**: 패키지 매니저 성격의 초기화/생명주기 관리 클래스
- **중요 이유**
  - 다른 패키지와의 초기화 연계를 볼 때 기준점이 됩니다.

### `SceneLoadingAiBt`
- **경로**: `Scene/SceneLoadingAiBt.cs`
- **역할**: 로딩 씬 단계에서 AI BT 관련 초기화 처리
- **중요 이유**
  - 게임 시작 시 Addressables / settings / bootstrap 흐름 연결을 파악할 때 참고할 클래스입니다.

### `ConfigScriptableObjectAiBt` 및 Addressables Config 계열
- **경로**: `Config/*`
- **역할**: 메뉴명, Addressables 키/그룹/경로 등 패키지 설정 상수 관리
- **중요 이유**
  - 에디터 툴과 런타임 로더가 동일한 키/경로 규약을 사용하도록 해줍니다.

---

## 2.4 Runtime에서 특히 중요한 노드 타입

`MonsterBtRunner`가 해석하는 노드 타입은 `BtTypeIds`에 정의되어 있으며, 실제 설계 의도를 빠르게 이해하려면 아래 타입들을 우선 보면 좋습니다.

### Composite
- `Composite.Selector`
- `Composite.Sequence`
- `Composite.RandomWeighted`

### Decorator
- `Decorator.Cooldown`
- `Decorator.Timeout`

### Condition
- `Condition.HasAggroTarget`
- `Condition.InAttackRange`
- `Condition.TargetWithinDistance`
- `Condition.CanUseSkill`
- `Condition.IsSkillInCastRange`
- `Condition.SkillUseCountCompare`
- `Condition.LastSkillResult`
- `Condition.LastSkillCombatOutcome`
- `Condition.HasAffect`

### Action
- `Action.Wait`
- `Action.WaitOneTick`
- `Action.Stop`
- `Action.FaceToTarget`
- `Action.MoveToTarget`
- `Action.AttackBasic`
- `Action.UseSkill`
- `Action.UseSkillAndWait`
- `Action.RequestRestartRoot`
- `Action.ClearAggro`
- `Action.ResetSkillUseCount`

---

## 3. Editor 중요한 클래스

## 3.1 최우선 핵심 클래스

### `CreateBtWindow`
- **경로**: `GGemCoTool/CreateBT/CreateBtWindow.cs`
- **역할**: BT 생성/편집/테스트용 메인 EditorWindow
- **왜 중요한가**
  - 이 패키지의 에디터 워크플로우 중심입니다.
  - GraphView, Inspector, 하단 Debug 패널을 한 윈도우에 통합합니다.
  - Runner attach, Apply Tree To Runner, Validate, Save, Debug Freeze/Step/History 확인까지 연결합니다.
- **핵심 포인트**
  - UI Toolkit 기반 구성
  - 노드 선택 시 Inspector 갱신
  - 런타임 Runner와 연결해 디버그 프레임 표시
  - 노드 자식 재정렬(ReorderableList) 지원
- **실무 메모**
  - AI BT 편집 UX를 개선하려면 가장 먼저 이 클래스를 봐야 합니다.
  - 구조가 커질수록 패널 분리/서비스 추출 리팩터링 후보가 될 가능성이 큽니다.

### `BtGraphView`
- **경로**: `GGemCoTool/CreateBT/Designer/BtGraphView.cs`
- **역할**: BT 그래프 편집 뷰
- **왜 중요한가**
  - 그래프 기반 편집의 핵심 UI 레이어입니다.
  - 실제 저장은 `MonsterBehaviorTreeAsset`/`BtNodeRecord`에 하고, GraphView는 표현과 편집 상호작용을 담당합니다.
- **핵심 포인트**
  - 노드 배치 및 연결
  - 선택 상태 전달
  - 디버그 하이라이트 반영
  - 에셋 → 그래프 / 그래프 → 에셋 동기화
- **실무 메모**
  - 멀티 부모, 포트 정책, 노드 생성 UX를 바꿀 때 중심 클래스입니다.

### `BtNodeView`
- **경로**: `GGemCoTool/CreateBT/Designer/BtNodeView.cs`
- **역할**: GraphView 상의 개별 노드 표시 클래스
- **왜 중요한가**
  - 노드 UI, 포트, 제목, 시각적 상태를 책임집니다.
  - 런타임 데이터와 UI 표현을 연결하는 단위입니다.

### `BtNodeTypeCatalog`
- **경로**: `GGemCoTool/CreateBT/Designer/BtNodeTypeCatalog.cs`
- **역할**: 에디터에서 생성 가능한 노드 목록과 파라미터 정의 제공
- **왜 중요한가**
  - 디자이너가 어떤 노드를 생성할 수 있는지 정의합니다.
  - 노드별 파라미터 자동 UI 생성 기준을 제공합니다.
- **핵심 포인트**
  - `All`: 생성 가능한 노드 목록
  - `GetParamDefs(typeId)`: 타입별 파라미터 정의
  - `BtParamDef`: 키, 타입, required, default, min/max 정의
- **실무 메모**
  - 새 노드 추가 시 Editor 쪽에서 반드시 수정해야 하는 클래스입니다.

### `BtEditorParamUtility`
- **경로**: `GGemCoTool/CreateBT/Designer/BtEditorParamUtility.cs`
- **역할**: 정의 기반 노드 파라미터 자동 렌더링/편집 유틸리티
- **왜 중요한가**
  - `BtNodeTypeCatalog`의 파라미터 정의를 실제 Inspector UI로 그려줍니다.
  - 파라미터 에디팅 일관성을 유지하는 데 핵심입니다.

---

## 3.2 검증 / 프리셋 / Undo / 규칙 클래스

### `MonsterBtValidator`
- **경로**: `GGemCoTool/CreateBT/BT/MonsterBtValidator.cs`
- **역할**: BT 에셋 정합성 검증
- **왜 중요한가**
  - 루트 누락, 순환 참조, 잘못된 연결, 필수 파라미터 누락 등을 검사하는 핵심 안정장치입니다.
- **실무 메모**
  - 저장 전 검증, CI 점검, 자동 검사 메뉴와 연결하기 좋은 클래스입니다.

### `MonsterBtPresetBuilder`
- **경로**: `GGemCoTool/CreateBT/BT/MonsterBtPresetBuilder.cs`
- **역할**: 예시 BT 프리셋 생성
- **왜 중요한가**
  - 새 트리 생성 시 출발 템플릿을 제공합니다.
  - 기획/디자인 초안 자동 생성 기능으로 확장하기 좋습니다.

### `MonsterBehaviorTreeAssetEditor`
- **경로**: `GGemCoTool/CreateBT/BT/MonsterBehaviorTreeAssetEditor.cs`
- **역할**: BT 에셋 전용 커스텀 인스펙터
- **왜 중요한가**
  - 에셋 자체를 선택했을 때 검증, 예시 트리 생성, 노드/블랙보드 목록 확인 기능을 제공합니다.

### `BtUndoUtility`
- **경로**: `GGemCoTool/CreateBT/Designer/BtUndoUtility.cs`
- **역할**: BT 편집용 Undo 처리 공통화
- **왜 중요한가**
  - 구조 변경은 스냅샷 기반, 단일 값 변경은 `RecordObject` 기반으로 처리하는 기준점을 제공합니다.
  - Editor 도구 안정성에 직접 연결됩니다.

### `BtNodeParentPolicy`
- **경로**: `GGemCoTool/CreateBT/Designer/BtNodeParentPolicy.cs`
- **역할**: 노드 부모 연결 정책 정의
- **왜 중요한가**
  - 어떤 노드가 다중 부모를 허용하는지 규정합니다.
  - 런타임 상태 충돌을 방지하는 설계 규칙이 담겨 있습니다.

---

## 3.3 디버그 / 내보내기 관련 클래스

### `BtDebugExportBuilder`
- **경로**: `GGemCoTool/CreateBT/Debug/BtDebugExportBuilder.cs`
- **역할**: 런타임 디버그 상태를 내보내기용 데이터 모델로 변환
- **왜 중요한가**
  - 디버그 프레임, 이벤트, 메트릭, 히스토리, 브레이크포인트 정보를 JSON 저장 전 단계로 정리합니다.
  - 디버그 리포트 기능의 중심입니다.

### `BtDebugExportModels`
- **경로**: `GGemCoTool/CreateBT/Debug/BtDebugExportModels.cs`
- **역할**: 디버그 내보내기 DTO 모델 정의
- **왜 중요한가**
  - 외부 파일 저장 포맷을 고정하는 구조입니다.
  - 로그 분석기나 QA 리포트와 연결하기 좋습니다.

### `BtDebugExportWriter`
- **경로**: `GGemCoTool/CreateBT/Debug/BtDebugExportWriter.cs`
- **역할**: 디버그 내보내기 데이터 실제 저장
- **왜 중요한가**
  - `CreateBtWindow`의 Save Debug 기능과 연결되는 최종 출력 지점입니다.

---

## 3.4 드롭다운 / 편의 기능 클래스

### `BtSkillDropdownProvider`
- **경로**: `GGemCoTool/CreateBT/Designer/BtSkillDropdownProvider.cs`
- **역할**: 스킬 UID 파라미터 입력용 드롭다운 옵션 제공
- **왜 중요한가**
  - `UseSkill`, `CanUseSkill`, `IsSkillInCastRange` 계열 노드 편집성을 높입니다.

### `BtAffectDropdownProvider`
- **경로**: `GGemCoTool/CreateBT/Designer/BtAffectDropdownProvider.cs`
- **역할**: Affect UID 관련 파라미터 입력용 드롭다운 옵션 제공
- **왜 중요한가**
  - `HasAffect` 같은 조건 노드 편집 UX를 담당합니다.

### `BtClipboardData`
- **경로**: `GGemCoTool/CreateBT/Designer/BtClipboardData.cs`
- **역할**: 노드 복사/붙여넣기용 순수 직렬화 데이터
- **왜 중요한가**
  - GraphView 객체 자체가 아니라 데이터 모델만 보관하는 구조라서 안정적입니다.

---

## 3.5 Addressables / 설정 도구 클래스

### `AddressableEditorAiBt`
- **경로**: `GGemCoTool/Addressables/AddressableEditorAiBt.cs`
- **역할**: AI BT 관련 Addressables 설정 편집 윈도우
- **왜 중요한가**
  - BT 에셋, 설정 ScriptableObject 등을 Addressables 그룹에 등록/관리하는 에디터 진입점입니다.

### `SettingMonsterBt`
- **경로**: `GGemCoTool/Addressables/SettingMonsterBt.cs`
- **역할**: Monster BT 관련 Addressables 등록/구성 처리
- **왜 중요한가**
  - BT 에셋 배포/로딩 파이프라인 구성 시 확인해야 할 클래스입니다.

### `SettingScriptableObjectAiBt`
- **경로**: `GGemCoTool/Addressables/SettingScriptableObjectAiBt.cs`
- **역할**: AI BT 설정 ScriptableObject를 Addressables에 등록
- **왜 중요한가**
  - 런타임 설정 로더와 에디터 배포 설정을 연결해 줍니다.

### `ConfigEditorAiBt`
- **경로**: `GGemCoTool/Config/ConfigEditorAiBt.cs`
- **역할**: 툴 메뉴 경로와 정렬 순서 정의
- **왜 중요한가**
  - 에디터 메뉴 구조를 통일하는 기준입니다.

---

## 3.6 씬 관련 에디터 클래스

### `DefaultSceneEditorAiBt`
### `SceneEditorGameAiBt`
### `SceneEditorLoadingAiBt`
- **경로**: `GGemCoTool/Scene/*`
- **역할**: AI BT 패키지의 기본/게임/로딩 씬 관련 편의 설정 도구
- **왜 중요한가**
  - 패키지 단위 개발 시 씬 전환/테스트 환경을 맞추는 용도로 사용될 가능성이 높습니다.

---

## 4. 우선순위 기준으로 다시 보는 핵심 클래스 TOP 10

### Runtime TOP 5
1. `MonsterBtRunner`
2. `MonsterBehaviorTreeAsset`
3. `RuntimeBlackboard`
4. `BtRuntimeState`
5. `BtNodeRecord`

### Editor TOP 5
1. `CreateBtWindow`
2. `BtGraphView`
3. `BtNodeTypeCatalog`
4. `BtEditorParamUtility`
5. `MonsterBtValidator`

---

## 5. 유지보수 관점에서 보는 추천 읽기 순서

### Runtime 읽기 순서
1. `MonsterBehaviorTreeAsset`
2. `BtNodeRecord`
3. `BtEnums`
4. `RuntimeBlackboard`
5. `BtRuntimeState`
6. `MonsterBtRunner`
7. `AddressableLoaderMonsterBt`
8. `GGemCoAiBtSettings`

### Editor 읽기 순서
1. `CreateBtWindow`
2. `BtGraphView`
3. `BtNodeView`
4. `BtNodeTypeCatalog`
5. `BtEditorParamUtility`
6. `MonsterBtValidator`
7. `BtDebugExportBuilder`
8. `AddressableEditorAiBt`

---

## 6. 확장할 때의 실무 기준

### 새 노드를 추가할 때
1. Runtime의 `BtTypeIds`에 타입 ID 추가
2. `MonsterBtRunner`에 실행 해석 로직 추가
3. Editor의 `BtNodeTypeCatalog`에 노드 정의와 파라미터 정의 추가
4. `BtEditorParamUtility`에서 필요한 편집 UI 확인
5. `MonsterBtValidator`에 정합성 검사 규칙 추가 여부 검토

### 새 디버그 정보를 추가할 때
1. Runtime의 `BtDebugTypes` 확장
2. `MonsterBtRunner`에서 프레임/이벤트/메트릭 기록 추가
3. Editor의 `BtDebugExportBuilder` / `CreateBtWindow`에 표시 로직 추가

### 새 로딩 정책을 추가할 때
1. `AddressableLoaderMonsterBt` / `AddressableLoaderSettingsAiBt` 확인
2. `GGemCoAiBtSettings`와 Addressables 설정 툴 동기화 확인
3. `BootstrapperBt` 초기 연결 흐름 확인

---

## 7. 문서용 한 줄 요약

### Runtime
- `MonsterBtRunner`: BT 평가와 디버그를 책임지는 실행 중심 클래스
- `MonsterBehaviorTreeAsset`: BT 정의를 담는 핵심 에셋
- `RuntimeBlackboard`: 조건/액션 노드가 공유하는 런타임 상태 저장소
- `BtRuntimeState`: Running 상태를 유지하는 노드별 실행 상태 저장소
- `BtNodeRecord`: 노드 데이터 구조의 중심 모델

### Editor
- `CreateBtWindow`: BT 작성/테스트/디버그 메인 툴
- `BtGraphView`: 그래프 편집 UI 핵심 뷰
- `BtNodeTypeCatalog`: 생성 가능한 노드와 파라미터 정의 카탈로그
- `BtEditorParamUtility`: 정의 기반 파라미터 편집 UI 유틸리티
- `MonsterBtValidator`: 트리 정합성 보장 도구

---

## 8. 참고

### 프로젝트 내부 문서 기준
- AI BT 패키지는 몬스터/AI의 Behavior Tree 실행과 에디터 도구를 담당하도록 정의되어 있습니다.
- 실행 흐름은 **트리 로딩 → Runner Tick 평가 → 블랙보드 읽기/쓰기 → Core/Control/Skill 표준 API 호출 → 종료/리셋** 순서를 따릅니다.
- 의존성은 **Core ← Control ← Skill ← AI_BT** 방향을 유지해야 합니다.

### Unity 공식 문서 참고
- Assembly Definition: 패키지 코드에는 `.asmdef`가 필요하며, `Editor` 코드는 `Runtime`을 참조할 수 있지만 반대는 허용되지 않습니다.
- ScriptableObject: 에디터에서 스크립트로 값을 수정할 때는 변경 사항이 저장되도록 dirty 처리와 저장 흐름을 고려해야 합니다.
- EditorWindow / UI Toolkit: `CreateGUI`에서 에디터 UI를 구성하는 패턴이 권장됩니다.

