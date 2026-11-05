# 11. 세션 B — Network (UDP Transport)

- 담당자: **세션 B**
- 브랜치: `feature/network`
- 담당 FR: FR-04 패킷 직렬화 / FR-05 UDP 비동기 송수신
- 수정 가능 폴더: `Assets/02_Scripts/Network/` (asmdef `VisionToWall.Network`, Core만 참조), `Tools/FakeSender/` (프로젝트 루트)
- 선행 조건: Phase 0 완료 ([01_Core_공통계약](01_Core_공통계약.md) 2절 패킷 레이아웃 필독)

## 1. 납품물

| 파일 | 역할 |
|---|---|
| `MotionPacketSerializer.cs` | 순수 C# static. `MotionFrame` ↔ `byte[]` (단위 테스트 대상) |
| `UdpMotionSender.cs` | MonoBehaviour. `IMotionFrameSink` 구현. 송신 스레드 보유 |
| `UdpMotionReceiver.cs` | MonoBehaviour. `IPointSource`, `INetworkStats` 구현. 수신 스레드 보유 |
| `Dev/FakeMotionSender.cs` | 테스트용. 원형으로 움직이는 가짜 포인트를 UDP 송신 (세션 C, D도 사용) |
| `Dev/NetworkDev.unity` | Sender ↔ Receiver 루프백 테스트 씬 |
| `Tools/FakeSender/fake_sender.py` | Unity 없이 빌드본에 가짜 패킷 송신 (현장 점검용) |

## 2. 공개 API (App이 호출)

```csharp
namespace VisionToWall.Network
{
    public static class MotionPacketSerializer
    {
        /// buffer에 기록 후 길이 반환. buffer 크기 >= MotionProtocol.MaxPacketSize
        public static int Write(MotionFrame frame, uint seq, byte[] buffer);
        /// 검증 실패(magic, 길이, count 범위) 시 false. dst 재사용.
        public static bool TryRead(byte[] buffer, int length, MotionFrame dst);
    }

    public class UdpMotionSender : MonoBehaviour, IMotionFrameSink
    {
        public void Initialize(AppConfig config);   // udpIp, udpPort
        public int FramesSent { get; }
        public int FramesSkipped { get; }           // latest-wins로 버려진 수
    }

    public class UdpMotionReceiver : MonoBehaviour, IPointSource, INetworkStats
    {
        public void Initialize(AppConfig config);   // udpPort
    }
}
```

## 3. 처리 설계

### 3.1 FR-04 직렬화
- Little-Endian, `System.Buffers.Binary.BinaryPrimitives` + `BitConverter.SingleToInt32Bits` / `Int32BitsToSingle`.
- `TryRead` 검증: `length >= HeaderSize`, magic 일치, `count <= MaxPoints`, `length == HeaderSize + count * PointSize`.
- 할당 0 (버퍼는 호출자가 제공).

### 3.2 FR-05 송신 (`UdpMotionSender`)
- `Submit`(메인 스레드): 사전 할당된 `MotionFrame` 슬롯에 `CopyFrom` 후 `AutoResetEvent.Set()`. 블로킹 없음.
- 슬롯은 **latest-wins**: 송신 스레드가 못 따라가면 이전 프레임 덮어쓰고 `FramesSkipped++`. (lock 짧게, 또는 3중 버퍼 스왑)
- 송신 스레드: 이벤트 대기 → 직렬화 → `udpClient.Send(buffer, length)`.
- `UdpClient.Connect(ip, port)`를 한 번 호출해 두고 `Send(byte[], int)` 사용 (매 호출 EndPoint 할당 회피).

### 3.3 FR-05 수신 (`UdpMotionReceiver`)
- `UdpClient(port)` 생성. 수신은 **`udpClient.Client.Receive(byte[])`** 사용 (`UdpClient.Receive`는 매번 배열 할당하므로 금지).
- `ReceiveTimeout = 500ms` → 종료 플래그 주기적 확인.
- 프레임 풀: `ConcurrentQueue<MotionFrame> free` (8개 미리 생성), `ConcurrentQueue<MotionFrame> ready`.
  - 수신 스레드: `free`에서 꺼냄 → `TryRead` → `ready`에 넣음. `free`가 비었거나 파싱 실패면 `PacketsDropped` 증가 (`Interlocked`).
  - seq가 직전보다 작으면 드롭 (단, seq가 1로 리셋된 경우 = 송신 측 재시작 → 허용).
- 메인 스레드 `Update`: `ready`를 비우며 `FrameReceived` 발생 → 사용 후 `free`로 반환. 프레임당 최대 8개 처리.
- `LastPacketTime`은 메인 스레드에서 dequeue 시 `Time.unscaledTime`으로 기록.

### 3.4 정리
- `OnDisable`: 종료 플래그 → `udpClient.Close()` → `thread.Join(1000)`. `SocketException`/`ObjectDisposedException`은 종료 중이면 무시.
- 에디터에서 Play 반복 시 "Address already in use" 없어야 함.

### 3.5 FakeMotionSender
- 인스펙터: ip, port, 포인트 수, 회전 속도, 송신 Hz(기본 60).
- 원 궤적 위 3~5개 포인트 + 5초 ON / 7초 OFF 주기 (세션 C의 Idle 전환 테스트용).

## 4. 체크리스트

### 구현 — 담당: 세션 B
- [x] FR-04 `MotionPacketSerializer.Write` / `TryRead`
- [x] FR-04 EditMode 테스트: 왕복 일치, count 0, count MaxPoints, magic 오류, 길이 오류
- [x] FR-05 `UdpMotionSender` 송신 스레드 + latest-wins 슬롯
- [x] FR-05 `UdpMotionReceiver` 수신 스레드 + `Client.Receive(byte[])`
- [x] FR-05 `ConcurrentQueue` 프레임 풀 (free / ready) → 메인 스레드 `FrameReceived`
- [x] `INetworkStats` 카운터 (Interlocked)
- [x] seq 역행 드롭 + 재시작 허용
- [x] `OnDisable` 스레드/소켓 정리
- [x] `Dev/FakeMotionSender` (ON/OFF 주기 포함)
- [x] `Dev/NetworkDev.unity` 루프백 씬
- [x] `Tools/FakeSender/fake_sender.py` (인자: ip, port, hz)
- [x] 세션 C, D에 FakeMotionSender 사용법 공유 (이 문서 하단 작업 로그에 기록)

### 검증 — 담당: 사용자 (Unity 에디터)
- [x] NetworkDev 씬: 송신 수 ≈ 수신 수, Drop 0
- [x] Profiler: 메인 스레드 GC Alloc 0 B/frame
- [x] Play 정지 → 재시작 3회 반복, 포트 에러 없음
- [x] `fake_sender.py` 실행 → 에디터 수신 로그 확인

## 5. 제외 범위
- 패킷 내용 생성 (세션 A), 로그 UI·JSON 표시 (세션 D)
- TCP, 재전송, 암호화 — 로컬 UDP 실시간 스트림이므로 불필요

## 작업 로그
- 2026-11-05 FR-04/05 구현, 커밋 da9a701 (WIP) → origin/main(9658f9d) 병합 08df8dc
  - 오프라인 검증: dotnet netstandard2.1 컴파일 경고/오류 0, 직렬화·seq 테스트 16/16 통과 (NUnit 리플렉션 러너), fake_sender.py 루프백 90/90 수신·포맷 오류 0
  - Unity 측 refresh / EditMode run_tests / 씬 로드는 사용자 검증 대기 (이 세션 MCP 미연결)
- 2026-11-05 Unity 검증 (MCP HTTP 직접 호출, 에디터 6000.3.4f1)
  - refresh + compile: Console 에러/경고 0. EditMode 26/26 통과 (Network 16 + Sensor 10)
  - NetworkDev 루프백 (FakeMotionSender): 송신 852 / 수신 852, Drop 0, Skipped 0
  - fake_sender.py 20초: 1200 송신 / 1200 수신, Drop 0. 재실행(seq 1 리셋) 300/300, Drop 0
  - Play 정지 → 재시작 3회: 포트 에러 없음, 정지 후 9000 포트 해제 확인
  - GC Allocated In Frame: Sender+Receiver 활성 = 기준선(둘 다 비활성) 22159 B 동일 → Network 0 B/frame. 차이 368 B 는 NetworkDevBootstrap.OnGUI (IMGUI, Dev 전용)
  - Drop 원인별 카운터 추가: `DroppedPoolExhausted` / `DroppedInvalid` / `DroppedSeq` / `DroppedSocket` (합 = PacketsDropped). pool 은 메인 스레드 지연(에디터 비포커스 등), seq 는 같은 포트에 송신자 2개
- 구현 메모
  - 와이어 seq: 송신 스레드가 1부터 부여 (Sensor의 Seq 무시). timestampMs = 송신 시 `Environment.TickCount`
  - 수신 seq 필터: `seq == 1 || seq > last` 만 통과 → 역행·중복 드롭, 송신 재시작(1) 허용. `UdpMotionReceiver.IsSeqAccepted`
  - 소켓 오류: 송신 `SocketException` → `UdpMotionSender.SendErrors` 증가 (수신자 없을 때 ICMP). 수신 MessageSize/기타 → Dropped, TimedOut/ConnectionReset → 무시
  - 추가 공개 멤버: `UdpMotionSender.SendErrors`, `IsRunning` / `UdpMotionReceiver.IsRunning`, `LastPacketTime` 첫 패킷 전 -1
  - Initialize 는 OnEnable 전후 어느 쪽이든 호출 가능. 비활성화 시 정지, 재활성화 시 같은 config 로 재시작

### FakeMotionSender 사용법 (세션 C, D)
- 컴포넌트: `VisionToWall.Network.Dev.FakeMotionSender` (Network 어셈블리. App 없이 Dev 씬에서 바로 사용)
- 빈 GameObject 에 추가 → 내부에서 `UdpMotionSender` 를 자동 추가해 실제 와이어 포맷으로 송신
- 인스펙터: `ip`(127.0.0.1) `port`(9000) `sendHz`(60, 프레임레이트가 상한) / `generator`: `pointCount`(4) `rotationSpeed`(90°/s) `radius`(0.3) `intensity`(1) `dutyCycle`(on) `onSec`(5) `offSec`(7). OFF 구간은 count 0 패킷
- 수신 측: 같은 씬에 `UdpMotionReceiver` 추가 → `Initialize(ConfigService.Load())` (또는 `new AppConfig{udpPort=9000}`) → `FrameReceived += handler` (메인 스레드, frame 은 재사용 버퍼 → 필요하면 `CopyFrom`)
- 상태: `INetworkStats` (`PacketsReceived`, `PacketsDropped`, `LastSeq`, `LastPacketTime`)
- 예시 씬: `Assets/02_Scripts/Network/Dev/NetworkDev.unity` (Receiver + Bootstrap 오버레이 + FakeSender)
- Unity 없이: `python Tools/FakeSender/fake_sender.py [ip] [port] [hz] [--points N --on S --off S --no-cycle --duration S]`
- 주의: 같은 포트에 송신자 2개(FakeSender + 실 Sensor/py) 동시 사용 금지 → seq 스트림 섞여 Drop 증가
