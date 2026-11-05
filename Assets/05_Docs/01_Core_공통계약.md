# 01. Core 공통 계약

- 담당자: **기획 세션 (P)**
- 단계: Phase 0
- 위치: `Assets/02_Scripts/Core/` (asmdef `VisionToWall.Core`)
- 사용: 세션 A~D 전원 (읽기 전용)

이 문서의 타입·시그니처가 모듈 간 유일한 약속이다. 변경은 기획 세션만 하며, 변경 시 하단 `변경 이력`에 기록한다.

## 1. 데이터 흐름

```
[Sensor]  WebCamTexture → GPU 다운샘플 → AsyncGPUReadback → 차영상
             │ IMotionFrameSink.Submit(MotionFrame)        (메인 스레드)
             ▼
[Network] UdpMotionSender ── 송신 스레드 ── UDP 127.0.0.1:9000 (바이너리)
          UdpMotionReceiver ── 수신 스레드 → ConcurrentQueue
             │ IPointSource.FrameReceived(MotionFrame)     (메인 스레드)
             ▼
[Render]  ExhibitionStateMachine (Idle/Active) + 파티클/물결
             │ IExhibitionStateSource.StateChanged
             ▼
[UI]      F1 패널: 패킷 로그, FPS, 디버그 뷰, 상태, Threshold/Mirror → AppConfig
```

## 2. 와이어 포맷 결정

요구사항 FR-04는 "JSON 또는 바이트 배열"을 허용한다. **송신은 바이너리**로 확정한다.

- 이유: JSON 직렬화는 매 패킷 문자열 할당 → Zero-GC 목표와 충돌.
- JSON 표현 `{"points":[{"x":..,"y":..,"v":..}]}`은 **F1 패널 로그 표시용**으로만 생성 (패널 열려 있을 때만).

### 패킷 레이아웃 (Little-Endian)

| 오프셋 | 크기 | 타입 | 필드 | 설명 |
|---|---|---|---|---|
| 0 | 4 | uint32 | magic | `0x57325601` ("V2W" + 버전 1) |
| 4 | 4 | uint32 | seq | 송신 순번, 1부터 증가 |
| 8 | 4 | uint32 | timestampMs | 송신 측 `Environment.TickCount` |
| 12 | 2 | uint16 | count | 포인트 개수 (0 허용 = 움직임 없음) |
| 14 | 12 × count | float × 3 | x, y, v | 정규화 좌표 0~1, 강도 0~1 |

- 최대 포인트: `MotionProtocol.MaxPoints = 4096` (64×36 = 2304 수용)
- 최대 패킷: 14 + 4096 × 12 = 49,166 bytes (UDP 한계 65,507 이내)
- 좌표계: **원점 좌하단**, x 오른쪽 증가, y 위쪽 증가 (Unity 텍스처/뷰포트와 동일). Mirror는 Sensor 단계에서 적용 완료된 좌표를 보냄.
- count = 0 패킷도 송신함 (연결 살아있음 확인용). 단, FSM의 "모션 감지"는 count > 0일 때만.

## 3. 코드 스펙

### 3.1 MotionProtocol.cs
```csharp
namespace VisionToWall.Core
{
    public static class MotionProtocol
    {
        public const uint Magic = 0x57325601;
        public const int HeaderSize = 14;
        public const int PointSize = 12;
        public const int MaxPoints = 4096;
        public const int MaxPacketSize = HeaderSize + PointSize * MaxPoints;
    }
}
```

### 3.2 MotionPoint.cs / MotionFrame.cs
```csharp
namespace VisionToWall.Core
{
    [System.Serializable]
    public struct MotionPoint
    {
        public float x; // 0~1, 좌 → 우
        public float y; // 0~1, 하 → 상
        public float v; // 0~1, 강도
    }

    /// 재사용 버퍼. 수신자는 이벤트 콜백 밖으로 참조를 보관하지 말 것 (필요하면 복사).
    public sealed class MotionFrame
    {
        public readonly MotionPoint[] Points = new MotionPoint[MotionProtocol.MaxPoints];
        public int Count;
        public uint Seq;
        public uint TimestampMs;

        public void CopyFrom(MotionFrame other)
        {
            Count = other.Count;
            Seq = other.Seq;
            TimestampMs = other.TimestampMs;
            System.Array.Copy(other.Points, Points, other.Count);
        }
    }
}
```

### 3.3 인터페이스
```csharp
namespace VisionToWall.Core
{
    /// Sensor → Network. 구현: Network.UdpMotionSender
    public interface IMotionFrameSink
    {
        /// 메인 스레드에서 호출. 구현체는 즉시 내부 버퍼로 복사하고 반환 (블로킹 금지).
        void Submit(MotionFrame frame);
    }

    /// Network → Render/UI. 구현: Network.UdpMotionReceiver (테스트: 각 모듈의 Mock)
    public interface IPointSource
    {
        /// 메인 스레드에서 발생. frame은 재사용 버퍼.
        event System.Action<MotionFrame> FrameReceived;
    }

    /// Network → UI. 구현: Network.UdpMotionReceiver
    public interface INetworkStats
    {
        int PacketsReceived { get; }
        int PacketsDropped { get; }   // magic 불일치, 길이 오류, seq 역행
        uint LastSeq { get; }
        float LastPacketTime { get; } // Time.unscaledTime 기준
    }

    /// Sensor → UI. 구현: Sensor.WebcamMotionSensor
    public interface IDebugViewProvider
    {
        UnityEngine.Texture SourceTexture { get; } // 웹캠 원본 (Mirror 반영)
        UnityEngine.Texture DiffTexture { get; }   // 차분 흑백 (그리드 해상도)
        UnityEngine.Texture GridTexture { get; }   // 임계값 초과 셀 표시
        int GridWidth { get; }
        int GridHeight { get; }
    }

    public enum ExhibitionState { Idle, Active }

    /// Render → UI. 구현: Render.ExhibitionStateMachine
    public interface IExhibitionStateSource
    {
        ExhibitionState Current { get; }
        event System.Action<ExhibitionState> StateChanged; // 메인 스레드
    }
}
```

### 3.4 AppConfig.cs / ConfigService.cs
```csharp
namespace VisionToWall.Core
{
    [System.Serializable]
    public class AppConfig
    {
        // Sensor source (restart required): "webcam" | "external" (2026-11-06 추가)
        public string sensorMode = "webcam";

        // Camera
        public int cameraIndex = 0;
        public int cameraWidth = 640;
        public int cameraHeight = 480;
        public int cameraFps = 60;
        public bool mirror = true;

        // Sensor
        public int gridWidth = 16;
        public int gridHeight = 16;
        public float threshold = 0.15f;  // ΔLuminance 0~1

        // Hand sensor (external, runtime — Tools/HandSensor가 config.json 재로드) (2026-11-06 추가)
        public float handSpeedRef = 1.5f;
        public float handMinSpeed = 0.05f;
        public bool handEmitStill = false;

        // Network
        public string udpIp = "127.0.0.1";
        public int udpPort = 9000;

        // Exhibition
        public float idleTimeoutSec = 5f;
    }

    public static class ConfigService
    {
        public const string FileName = "config.json";

        /// 런타임 공유 인스턴스. UI가 필드를 수정하고 NotifyChanged() 호출.
        public static AppConfig Current { get; private set; }

        /// 값 변경 알림 (메인 스레드).
        public static event System.Action<AppConfig> Changed;

        /// StreamingAssets/config.json 로드. 없거나 파싱 실패 시 기본값 + 경고 로그 + 기본 파일 생성.
        public static AppConfig Load();

        /// 현재 값을 StreamingAssets/config.json에 저장 (pretty print).
        public static void Save();

        public static void NotifyChanged();
    }
}
```
- 로드 시점: `AppBootstrap.Awake` 최상단 1회. 각 모듈은 `ConfigService.Current`를 읽기만 함.
- 실행 중 바뀌어도 되는 값: `threshold`, `mirror` (Sensor가 매 프레임 읽음).
- 재시작 필요한 값: `cameraIndex`, 해상도, `grid*`, `udpIp`, `udpPort`. → UI에 "재시작 필요" 표시.

## 4. 어셈블리 구성

| asmdef | 참조 | 담당 |
|---|---|---|
| `VisionToWall.Core` | 없음 | 기획 세션 |
| `VisionToWall.Sensor` | Core | 세션 A |
| `VisionToWall.Network` | Core | 세션 B |
| `VisionToWall.Render` | Core, Unity.TextMeshPro | 세션 C |
| `VisionToWall.UI` | Core, Unity.InputSystem, Unity.TextMeshPro(필요 시) | 세션 D |
| `VisionToWall.App` | 전부 | 기획 세션 |

## 5. Phase 0 체크리스트 — 담당: 기획 세션

- [x] `git init`, Unity용 `.gitignore` (Library/, Temp/, Logs/, UserSettings/, *.csproj, *.sln, ProfilerCaptures/)
- [x] Project Settings: Asset Serialization = Force Text (기본값 확인), Version Control = Visible Meta Files
- [x] 폴더 생성: `02_Scripts/{Core,Sensor,Network,Render,UI,App}`, `StreamingAssets/`
- [x] asmdef 6개 생성 (4절 표대로). 모듈 폴더엔 `ModuleInfo.cs` placeholder (담당 세션이 삭제 가능)
- [x] Core 코드 작성: `MotionProtocol`, `MotionPoint`, `MotionFrame`, 인터페이스 5개, `ExhibitionState`, `AppConfig`, `ConfigService`
- [x] `StreamingAssets/config.json` 기본값 파일 생성
- [x] `App/AppBootstrap.cs` 골격 (Config 로드만)
- [x] 컴파일 에러 0 확인 — MCP `refresh_unity` + `execute_code`로 `scriptCompilationFailed=False`, `ConfigService.Load()` 정상
- [x] `.mcp.json` (UnityMCP) 등록
- [x] 초기 커밋 → `feature/sensor`, `feature/network`, `feature/render`, `feature/ui` 브랜치 생성
- [x] ~~worktree 4개 생성~~ → 2026-11-05 완전 순차 방식으로 변경, worktree 삭제 (브랜치 유지)
- [x] `00_개발_개요.md` 4절 상태 갱신

참고: Active Input Handling = Input System 전용(`activeInputHandler: 1`). 구 `UnityEngine.Input` API 사용 금지.

## 변경 이력

| 일시 | 변경 | 영향 세션 |
|---|---|---|
| 2026-11-05 | 최초 작성 | 전체 |
| 2026-11-06 | `AppConfig.sensorMode` 추가 (`webcam`/`external`), `AppBootstrap` external 분기 | 세션 E, D(config 표) |
| 2026-11-06 | `AppConfig.handSpeedRef / handMinSpeed / handEmitStill` 추가 (손 센서 런타임 조절, F1 편집) | 세션 E, D |
