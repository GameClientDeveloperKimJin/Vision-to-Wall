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
- [ ] FR-04 `MotionPacketSerializer.Write` / `TryRead`
- [ ] FR-04 EditMode 테스트: 왕복 일치, count 0, count MaxPoints, magic 오류, 길이 오류
- [ ] FR-05 `UdpMotionSender` 송신 스레드 + latest-wins 슬롯
- [ ] FR-05 `UdpMotionReceiver` 수신 스레드 + `Client.Receive(byte[])`
- [ ] FR-05 `ConcurrentQueue` 프레임 풀 (free / ready) → 메인 스레드 `FrameReceived`
- [ ] `INetworkStats` 카운터 (Interlocked)
- [ ] seq 역행 드롭 + 재시작 허용
- [ ] `OnDisable` 스레드/소켓 정리
- [ ] `Dev/FakeMotionSender` (ON/OFF 주기 포함)
- [ ] `Dev/NetworkDev.unity` 루프백 씬
- [ ] `Tools/FakeSender/fake_sender.py` (인자: ip, port, hz)
- [ ] 세션 C, D에 FakeMotionSender 사용법 공유 (이 문서 하단 작업 로그에 기록)

### 검증 — 담당: 사용자 (Unity 에디터)
- [ ] NetworkDev 씬: 송신 수 ≈ 수신 수, Drop 0
- [ ] Profiler: 메인 스레드 GC Alloc 0 B/frame
- [ ] Play 정지 → 재시작 3회 반복, 포트 에러 없음
- [ ] `fake_sender.py` 실행 → 에디터 수신 로그 확인

## 5. 제외 범위
- 패킷 내용 생성 (세션 A), 로그 UI·JSON 표시 (세션 D)
- TCP, 재전송, 암호화 — 로컬 UDP 실시간 스트림이므로 불필요

## 작업 로그
- (세션 B가 기록)
