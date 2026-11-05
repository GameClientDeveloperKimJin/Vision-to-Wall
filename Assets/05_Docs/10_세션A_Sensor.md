# 10. 세션 A — Sensor (가상 LiDAR)

- 담당자: **세션 A**
- 브랜치: `feature/sensor`
- 담당 FR: FR-01 웹캠 입력 / FR-02 다운샘플링 / FR-03 차영상 모션 감지
- 수정 가능 폴더: `Assets/02_Scripts/Sensor/` (asmdef `VisionToWall.Sensor`, Core만 참조)
- 선행 조건: Phase 0 완료 ([01_Core_공통계약](01_Core_공통계약.md))

## 1. 납품물

| 파일 | 역할 |
|---|---|
| `WebcamMotionSensor.cs` | MonoBehaviour. 파이프라인 전체. `IDebugViewProvider` 구현 |
| `GridDownsampler.cs` | WebCamTexture → 그리드 RenderTexture (Mirror 포함) |
| `FrameDiffDetector.cs` | 순수 C# 클래스. 밝기 배열 2개 → `MotionFrame` 채움 (단위 테스트 대상) |
| `Dev/SensorDev.unity` + `Dev/LogSink.cs` | 단독 테스트 씬. `IMotionFrameSink` 목으로 Count 로그 |

## 2. 공개 API (App이 호출)

```csharp
namespace VisionToWall.Sensor
{
    public class WebcamMotionSensor : MonoBehaviour, IDebugViewProvider
    {
        public void Initialize(AppConfig config, IMotionFrameSink sink);
        public bool IsRunning { get; }        // 카메라 열림 여부
        public string DeviceName { get; }
    }
}
```
`Initialize` 호출 전엔 아무것도 하지 않음. App이 Awake 순서를 제어.

## 3. 처리 설계

1. **FR-01 입력**: `WebCamTexture.devices[config.cameraIndex]`로 생성, `cameraWidth/Height/Fps` 요청. 인덱스 범위 밖이면 경고 로그 + `IsRunning=false`, 예외 던지지 않음.
2. **FR-02 다운샘플**: `webcam.didUpdateThisFrame`일 때만 진행.
   - `Graphics.Blit`로 중간 RT(예: 128×72) → 그리드 RT(`gridWidth×gridHeight`) 2단계 축소 (에일리어싱 완화).
   - Mirror: 첫 Blit에서 `scale(-1,1), offset(1,0)` 적용. **이후 모든 좌표는 Mirror 반영 상태.**
   - `AsyncGPUReadback.RequestIntoNativeArray`로 그리드 RT 읽기. `NativeArray<Color32>`는 Persistent로 1회 할당, 콜백 델리게이트는 필드에 캐시. 동시에 1건만 요청.
3. **FR-03 차영상** (`FrameDiffDetector`):
   - 밝기 `L = (0.299R + 0.587G + 0.114B) / 255` → `float[] curr`
   - `d = |curr[i] - prev[i]|`, `d > config.threshold`인 셀만 포인트 추가
   - 좌표: `x = (col + 0.5) / W`, `y = (row + 0.5) / H` (row 0 = 아래. 실제 Readback 행 순서 확인 후 필요 시 반전)
   - 강도: `v = saturate((d - threshold) / (1 - threshold))`
   - 첫 프레임은 prev만 채우고 송신 안 함. 이후 `prev ↔ curr` 배열 스왑 (복사 없음)
4. **송신**: 감지 결과를 재사용 `MotionFrame`에 채워 매 처리 프레임마다 `sink.Submit(frame)` (Count 0 포함).
5. **디버그 텍스처**: `DiffTexture`(밝기 차), `GridTexture`(초과 셀 강조)를 그리드 크기 `Texture2D`로 유지. `SetPixelData` + `Apply(false)`로 갱신, 매 프레임 할당 없음.
6. **런타임 값**: `threshold`, `mirror`는 매 프레임 `ConfigService.Current`에서 읽음.
7. **정리**: `OnDestroy`에서 `webcam.Stop()`, RT `Release()`, NativeArray `Dispose()`.

## 4. 체크리스트

### 구현 — 담당: 세션 A
- [x] FR-01 `WebCamTexture` 생성/시작, 장치 없음·인덱스 오류 처리
- [x] FR-01 사용 가능 장치 목록을 시작 시 로그 출력 (현장 인덱스 확인용)
- [x] FR-02 2단계 Blit 다운샘플 + Mirror
- [x] FR-02 `AsyncGPUReadback.RequestIntoNativeArray`, 요청 1건 제한
- [x] FR-03 `FrameDiffDetector` 밝기 변환 + 차분 + 임계값 + 강도 계산
- [x] FR-03 prev/curr 배열 스왑, 첫 프레임 스킵
- [x] `IMotionFrameSink.Submit` 호출 (Count 0 포함)
- [x] `IDebugViewProvider` 텍스처 3종 제공
- [x] `threshold`/`mirror` 런타임 반영
- [x] `OnDestroy` 리소스 정리
- [x] `Dev/SensorDev.unity` + `LogSink` 테스트 씬
- [x] `FrameDiffDetector` EditMode 테스트: 동일 입력 → 0개, 한 셀 변화 → 1개 + 좌표 일치

### 검증 — 담당: 사용자 (Unity 에디터)
- [x] SensorDev 씬 Play → 웹캠 화면 나옴
- [x] 정지 상태에서 Count ≈ 0 (조명 노이즈 수준)
- [x] 손을 화면 오른쪽에서 흔들면 x > 0.5 포인트 발생 (Mirror ON 기준, 거울처럼 동작)
- [x] Profiler: Sensor 관련 GC Alloc 0 B/frame
- [x] Play 정지 → 재시작 반복 3회 시 에러 없음

## 5. 제외 범위
- UDP 송신 (세션 B), UI 표시 (세션 D), 씬 배치 (기획 세션)
- 블롭/중심점(Centroid) 계산 — v2.1 범위

## 작업 로그
- 2026-11-05 FR-01~03 구현 + Dev 씬 + EditMode 테스트 10건 작성. dotnet 빌드(Unity DLL 참조, Unity Analyzer 포함) 경고/오류 0. Unity 에디터 컴파일·Test Runner 실행은 미확인 (이 세션에 Unity MCP 도구 없음). 미커밋
  - 추가 파일: `Dev/SensorDevBootstrap.cs` (Config 로드 + Initialize + IMGUI 디버그 뷰/Threshold 슬라이더/Mirror 토글), `Tests/EditMode/` (asmdef `VisionToWall.Sensor.Tests`)
  - `ModuleInfo.cs` placeholder 삭제
  - 설계 메모: 다운샘플 3단계 (웹캠→Preview 640폭(Mirror 적용, SourceTexture 겸용)→128×72→그리드). Mirror 런타임 토글 시 detector Reset (전체 화면 오탐 방지). `MotionFrame.Seq/TimestampMs`는 Sensor가 채우지만 Network가 덮어써도 무방
  - 확인 필요: Readback 행 순서. 손을 화면 위쪽에서 흔들어 y > 0.5가 아니면 Inspector `flipReadbackY` 켜기 → 결과를 기본값에 반영
  - GC 측정 시: SensorDevBootstrap의 `showOverlay`/`logEnabled` 끄기 (IMGUI·Debug.Log는 Dev 전용 할당)
- 2026-11-05 (기획 세션) worktree → `InteractiveGame` 단일 폴더로 이동. 커밋 `ad6582e`, `main` 병합 `6592c05`. MCP 검증: Unity 컴파일 에러 0, EditMode `VisionToWall.Sensor.Tests` 10/10 통과. 사람 검증 대기
- 2026-11-05 사람 검증: 웹캠 화면 / 좌우(Mirror) / 상하(meanY > 0.5, `flipReadbackY=false` 기본값 확정) / Play 반복 3회 통과. `SensorDevBootstrap.OnGUI` `_sink` null 가드 추가 (Play 중 재컴파일 시 NRE). 정지 Count ≈ 0, GC Alloc 0 B/frame 통과 → 검증 5/5 완료
