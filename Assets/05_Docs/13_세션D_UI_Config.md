# 13. 세션 D — UI / Config (현장 엔지니어 도구)

- 담당자: **세션 D**
- 브랜치: `feature/ui`
- 담당 FR: FR-08 F1 엔지니어 오버레이 UI / FR-09 외부 환경설정 파일
- 수정 가능 폴더: `Assets/02_Scripts/UI/` (asmdef `VisionToWall.UI`, Core + Unity.InputSystem + Unity.TextMeshPro 참조), `Assets/03_Prefabs/UI/`, `Assets/StreamingAssets/`
- 선행 조건: Phase 0 완료 ([01_Core_공통계약](01_Core_공통계약.md)). `ConfigService` 로드/저장 구현은 Core(기획 세션) 담당, 이 세션은 사용·편집 UI 담당

## 1. 납품물

| 파일 | 역할 |
|---|---|
| `EngineerPanel.cs` | F1 토글, 패널 전체 갱신 |
| `PacketLogView.cs` | 최근 패킷 10줄 JSON 형식 로그 (링 버퍼) |
| `FpsCounter.cs` | 평활 FPS |
| `ConfigEditorView.cs` | Threshold 슬라이더, Mirror 토글, Save 버튼, 재시작 필요 항목 표시 |
| `03_Prefabs/UI/EngineerPanel.prefab` | Screen Space Overlay Canvas, 좌측 고정 |
| `StreamingAssets/config.json` | 필드 설명은 이 문서 4절 (Phase 0 기본 파일을 이어받아 관리) |
| `Dev/UIDev.unity` + `Dev/Mock*.cs` | 목 디버그 뷰, 목 포인트, 목 통계, 목 상태로 단독 테스트 |

## 2. 공개 API (App이 호출)

```csharp
namespace VisionToWall.UI
{
    public class EngineerPanel : MonoBehaviour
    {
        public void Initialize(
            IDebugViewProvider debugViews,
            IPointSource points,
            INetworkStats stats,
            IExhibitionStateSource state);
        public bool IsOpen { get; }
    }
}
```

## 3. 처리 설계

### 3.1 FR-08 패널 레이아웃 (화면 좌측, 폭 약 380px)
```
┌ Vision-to-Wall Engineer [F1] ─┐
│ FPS 60  | State: Active        │
│ Camera: <장치명> (index 0)      │
│ ┌원본────┐┌차분────┐┌그리드───┐ │
│ │        ││        ││ ■ □ ■  │ │
│ └────────┘└────────┘└────────┘ │
│ UDP :9000  Recv 1234 Drop 0    │
│ Last seq 1234 (0.02s ago)      │
│ ─ Packet log ─────────────────  │
│ #1234 {"points":[{"x":0.53,... │
│ ...  (최근 10줄)                │
│ Threshold [====|-----] 0.15    │
│ Mirror    [x]                  │
│ [Save config]  * 재시작 필요: … │
└────────────────────────────────┘
```

### 3.2 동작
- 토글: `Keyboard.current.f1Key.wasPressedThisFrame` (Input System). Player Settings의 Active Input Handling 확인.
- 패널 닫힘 상태: 텍스트 갱신·로그 포맷팅 전부 중지 (비용 0). 커서 숨김 (키오스크).
- 패널 열림: 커서 표시. 텍스트는 **4Hz**로만 갱신.
- 디버그 뷰: `RawImage.texture`에 `IDebugViewProvider` 텍스처 연결 (최초 1회). 그리드 텍스처는 `FilterMode.Point`.
- 패킷 로그: `FrameReceived`에서 seq·Count·앞 3개 포인트만 기록 (재사용 구조체 링 버퍼), 표시 시 `StringBuilder` 하나 재사용해 JSON 형식으로 조립 → `TMP_Text.SetText(StringBuilder)`.
- FPS: `Time.unscaledDeltaTime` 지수 평활.
- Threshold 슬라이더: 범위 0.01~0.50 → `ConfigService.Current.threshold` 수정 + `NotifyChanged()`. 즉시 반영.
- Mirror 토글: 동일 방식, 즉시 반영.
- Save: `ConfigService.Save()` → "저장됨" 2초 표시.

### 3.3 FR-09 config.json 필드

| 필드 | 기본값 | 설명 | 런타임 변경 |
|---|---|---|---|
| `cameraIndex` | 0 | 웹캠 장치 번호 (시작 로그에 목록 출력됨) | 재시작 |
| `cameraWidth` / `cameraHeight` | 640 / 480 | 요청 해상도. C270 기준 1280×720은 약 8fps로 떨어짐 (640×480: 약 25fps) | 재시작 |
| `cameraFps` | 60 | 요청 FPS | 재시작 |
| `mirror` | true | 좌우 반전 | 즉시 |
| `gridWidth` / `gridHeight` | 16 / 16 | 감지 그리드 (최대 64×36 권장) | 재시작 |
| `threshold` | 0.15 | 밝기 차 임계값 0~1 | 즉시 |
| `udpIp` | 127.0.0.1 | 송신 대상 IP | 재시작 |
| `udpPort` | 9000 | 송수신 포트 | 재시작 |
| `idleTimeoutSec` | 5 | 무입력 → Idle 전환 시간 | 즉시 |

## 4. 체크리스트

### 구현 — 담당: 세션 D
- [ ] FR-08 F1 토글 (Input System), 닫힘 시 갱신 중지 + 커서 숨김
- [ ] FR-08 웹캠 원본 / 차분 / 그리드 뷰 3종 `RawImage`
- [ ] FR-08 FPS 표시 (4Hz 갱신)
- [ ] FR-08 UDP 통계 (Recv, Drop, Last seq, 경과 시간)
- [ ] FR-08 패킷 로그 10줄, JSON 형식, `StringBuilder` 재사용
- [ ] FR-08 상태(Idle/Active), 카메라 장치명 표시
- [ ] FR-08 Threshold 슬라이더 → 즉시 반영
- [ ] FR-08 Mirror 토글 → 즉시 반영
- [ ] FR-09 Save 버튼 → `ConfigService.Save()`
- [ ] FR-09 재시작 필요 항목 안내 표시
- [ ] FR-09 `config.json` 필드 표(3.3) 기준 기본 파일 검토
- [ ] `EngineerPanel.prefab`
- [ ] `Dev/UIDev.unity` + 목 4종

### 검증 — 담당: 사용자 (Unity 에디터 + 빌드)
- [ ] UIDev: F1로 열고 닫힘, 모든 항목 표시
- [ ] 슬라이더 움직이면 `ConfigService.Current.threshold` 값 변경 확인 (로그)
- [ ] Save 후 `StreamingAssets/config.json` 파일 내용 변경 확인
- [ ] 패널 닫힘 상태 Profiler GC Alloc 0 B/frame
- [ ] (통합 후) 빌드본에서 `config.json`의 `udpPort` 수정 → 재실행 시 반영

## 5. 제외 범위
- `ConfigService` 내부 구현 (기획 세션, Core)
- 대기 화면 안내 문구 (세션 C)

## 작업 로그
- (세션 D가 기록)
