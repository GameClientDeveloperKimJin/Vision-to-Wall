# 51. 세션 R1 — Device Control (v2.1)

- 담당자: **R1 Device Control**
- 브랜치: `feature/roadmap-device`
- 담당: 보스 시선 각도(Pan/Tilt)를 RS-232/485 형식 바이트 패킷으로 변환, 패킷 로그, 화면 속 가상 무빙라이트
- 수정 가능: `Assets/02_Scripts/Device/**` (새 어셈블리 `VisionToWall.Device`), `Assets/02_Scripts/Device/Tests/**`, `Assets/02_Scripts/Device/Dev/**`, `Assets/03_Prefabs/Device/**`, 이 문서
- 선행 조건: 기획 세션 선행 작업 `main` 반영
- 사용자 결정: **시뮬레이션만**. 실제 COM 포트 송신 없음

## 1. 목표

보스가 플레이어를 바라보는 각도를 실제 무빙라이트·서보 모터가 받을 수 있는 패킷으로 만든다. 장비 대신 로그와 화면 속 가상 조명으로 동작을 보여준다. 나중에 실제 장비를 붙일 때 송신부만 바꾸면 되도록 패킷 생성과 송신을 분리한다.

## 2. 기획 세션 선행 작업

`Core/DeviceProtocol.cs`와 config 필드를 `main`에 넣는다.

### 패킷 규격 (8바이트, Pan/Tilt는 big-endian)

| 바이트 | 내용 | 값 |
|---|---|---|
| 0 | STX | `0x02` |
| 1 | ID | `deviceId` (기본 1) |
| 2~3 | Pan | int16, 0.01° 단위. -180.00~+180.00 |
| 4~5 | Tilt | int16, 0.01° 단위. -90.00~+90.00 |
| 6 | Checksum | 바이트 1~5의 XOR |
| 7 | ETX | `0x03` |

- 범위를 넘는 각도는 잘라서 보냄 (clamp)
- 부호: Pan은 화면 오른쪽이 음수 (`BossPanDeg`와 같음. 착수 시 실제 값으로 확인, 4절), Tilt는 위가 양수

### config 필드 (기본값에서 기존 동작 변화 없음)

| 필드 | 기본값 | 뜻 |
|---|---|---|
| `deviceEnabled` | `false` | 패킷 생성과 가상 조명 켜기 |
| `deviceId` | `1` | 패킷 ID 바이트 |
| `deviceSendHz` | `30` | 송신 주기 (1~60) |
| `deviceLogPackets` | `false` | 콘솔에 16진수 패킷 로그 (1초에 1줄로 제한) |

## 3. 납품물

| 파일 | 역할 |
|---|---|
| `Device/DevicePacketEncoder.cs` | 각도 → 8바이트. 순수 함수, 할당 없음 (호출자가 버퍼 제공) |
| `Device/IDevicePacketSink.cs` | 송신부 인터페이스 `Send(ReadOnlySpan<byte>)` |
| `Device/LogPacketSink.cs` | 시뮬레이션 송신부: 마지막 패킷 보관, 횟수 집계, 선택 로그 |
| `Device/DeviceController.cs` | `ICombatStateSource.BossPanDeg/TiltDeg`를 `deviceSendHz`로 읽어 인코딩 → 송신 |
| `Device/VirtualMovingLight.cs` + `03_Prefabs/Device/VirtualMovingLight.prefab` | 마지막 **패킷을 디코딩한 값**으로 회전하는 화면 상단 스포트라이트. 각도가 아니라 패킷을 거쳐 움직여야 실제 장비와 같은 경로임을 보여줌 |
| `Device/Dev/DeviceDev.unity` | 각도를 키보드로 바꾸며 패킷과 조명 확인 |

공개 API:

```csharp
// DeviceController
public void Initialize(AppConfig config, ICombatStateSource combat, IDevicePacketSink sink);
public int PacketsSent { get; }
// F1 표시용 (G6 패널 확장은 기획 세션이 연결)
public bool TryGetLastPacket(Span<byte> destination);
```

## 4. 처리 설계

- `BossPanDeg` 부호를 먼저 확인: 플레이어가 화면 오른쪽일 때 값 기록 ([40번](40_게임_통합_검증.md) 미확인 항목). 문서 규격과 다르면 변환하지 말고 기획 세션에 보고
- 전투가 아닐 때(보스 숨김) 각도 0으로 복귀. 급격한 변화는 장비 보호를 위해 초당 최대 회전 속도로 제한 (기본 180°/초, `DeviceController` 직렬화 필드)
- 송신 주기는 `Time.unscaledTime` 기준. Result 슬로우 모션에 영향받지 않음
- 가상 조명은 보스 연출을 가리지 않게 화면 위쪽 바깥에서 비춤. 레이어와 블룸 영향 확인
- Zero-GC: 인코딩, 송신, 조명 갱신 모두 프레임당 할당 0
- 디코더(`DevicePacketEncoder.TryDecode`)도 함께 만들어 왕복 테스트와 가상 조명에 사용. 체크섬 오류 패킷은 버림

## 5. 체크리스트

### 구현 — 담당: R1 Device Control
- [ ] `BossPanDeg` 부호 실측, 작업 로그 기록
- [ ] 인코더·디코더 (범위 clamp, 체크섬, big-endian)
- [ ] `LogPacketSink`, `DeviceController` (주기, 회전 속도 제한, 비전투 복귀)
- [ ] 가상 무빙라이트 프리팹
- [ ] `DeviceDev.unity`

### 자동 검증 — 담당: R1 Device Control
- [ ] EditMode: 왕복(인코드 → 디코드) 각도 오차 0.01° 이하, 경계값(±180, ±90, 범위 밖), 체크섬 오류 거부, 바이트 순서 고정 예시 1건
- [ ] 기존 EditMode 전체 통과
- [ ] `deviceEnabled: false`에서 동작 변화 없음 (오브젝트 비활성, 할당 0)
- [ ] 켠 상태 프레임 할당 0, fps 영향 기록

### 사람 검증 — 담당: 사용자
- [ ] 보스가 나를 따라 볼 때 가상 조명도 같은 방향으로 따라옴
- [ ] 로그 패킷의 Pan 부호가 내가 선 쪽과 맞음 (오른쪽 = 음수)

## 6. 통합 인계

기획 세션 작업: Main에 `VirtualMovingLight` 배치, `AppBootstrap`에서 `DeviceController.Initialize` 호출, F1 패널에 송신 수와 마지막 패킷 16진수 표시 연결 여부 결정.

## 7. 제외 범위

실제 COM 포트 송신, API 호환 수준 변경, 장비별 프로토콜(DMX 등)

## 작업 로그
- (R1 Device Control이 기록)
