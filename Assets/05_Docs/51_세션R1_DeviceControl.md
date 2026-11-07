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
- [x] `BossPanDeg` 부호 실측, 작업 로그 기록 (규격과 일치: 오른쪽 = 음수)
- [x] 인코더·디코더 (범위 clamp, 체크섬, big-endian)
- [x] `LogPacketSink`, `DeviceController` (주기, 회전 속도 제한, 비전투 복귀)
- [x] 가상 무빙라이트 프리팹
- [x] `DeviceDev.unity`

### 자동 검증 — 담당: R1 Device Control
- [x] EditMode: 왕복(인코드 → 디코드) 각도 오차 0.01° 이하, 경계값(±180, ±90, 범위 밖), 체크섬 오류 거부, 바이트 순서 고정 예시 1건
- [x] 기존 EditMode 전체 통과 (264/264, 그중 Device 23)
- [x] `deviceEnabled: false`에서 동작 변화 없음 (오브젝트 비활성, 할당 0)
- [x] 켠 상태 프레임 할당 0, fps 영향 기록

### 사람 검증 — 담당: 사용자
- [ ] 보스가 나를 따라 볼 때 가상 조명도 같은 방향으로 따라옴
- [ ] 로그 패킷의 Pan 부호가 내가 선 쪽과 맞음 (오른쪽 = 음수)

## 6. 통합 인계

기획 세션 작업: Main에 `VirtualMovingLight` 배치, `AppBootstrap`에서 `DeviceController.Initialize` 호출, F1 패널에 송신 수와 마지막 패킷 16진수 표시 연결 여부 결정.

## 7. 제외 범위

실제 COM 포트 송신, API 호환 수준 변경, 장비별 프로토콜(DMX 등)

## 작업 로그

### 2026-11-08 R1 Device Control

**BossPanDeg 부호 실측** (`CombatDev.unity`, GameOverTimeout 시나리오, Mock 입력, 0.5초 샘플 40초, Unity 포커스)

| 플레이어 viewport x | BossPanDeg | BossTiltDeg |
|---|---|---|
| 0.80 (화면 오른쪽) | -28.0 | -21.2 |
| 0.20 (화면 왼쪽) | +27.5 | -21.2 |

- 보스 viewport (0.50, 0.72), 플레이어 y 0.5 → 보스가 아래를 봄 = tilt 음수. 10회 반복 동일
- 규격(Pan 오른쪽 = 음수, Tilt 위 = 양수)과 일치. 변환 없음. 근거: `BossLookAt` rest가 카메라를 바라보므로 모델 오른쪽(+) = 화면 왼쪽

**납품물**

| 파일 | 비고 |
|---|---|
| `Device/VisionToWall.Device.asmdef` | Core만 참조 |
| `Device/DevicePacketEncoder.cs` | `TryEncode(Span<byte>, id, pan, tilt)`, `TryDecode(ReadOnlySpan<byte>, out id, out pan, out tilt)`, `Checksum`. clamp, NaN → 0, 반올림 AwayFromZero. 디코드는 길이·STX·ETX·체크섬·각도 범위 검사 |
| `Device/IDevicePacketSink.cs` | `Send(ReadOnlySpan<byte>)` |
| `Device/LogPacketSink.cs` | 마지막 패킷, `Count`, `Rejected`, `LogPackets`, `FormatHex`. 로그는 1초 1줄 `[Device] #n 02 01 ..` (그 줄만 할당) |
| `Device/DeviceSendLoop.cs` | 주기(1~60 clamp)와 회전 속도 제한. Unity 시간 없이 테스트 가능. 멈춤 뒤 몰아서 보내지 않음 |
| `Device/DeviceController.cs` | 공개 API 3절 그대로 + `IsActive`, `SentPanDeg/TiltDeg`, `Tracking`(false = 0으로 복귀), `MaxFrameDeltaSec` 0.1. 직렬화 `maxDegPerSec` 180 |
| `Device/VirtualMovingLight.cs` | `Initialize(DeviceController, Camera)`. 새 패킷이 올 때만 디코딩. 체크섬 오류는 `RejectedPackets` |
| `Device/Shaders/DeviceBeam.shader` | 가산 콘, 끝·가장자리 페이드, Queue Transparent-10, ZTest LEqual |
| `03_Prefabs/Device/VirtualMovingLight.prefab` | Root(레이어 1) / Head / Beam. `DeviceBeamCone.asset`(길이 9, 반각 7°), `Materials/DeviceBeam.mat` |
| `Device/Dev/DeviceDev.unity` + `DeviceDevBootstrap.cs` | 방향키 pan/tilt, R 중앙, 1/2 -90/+90 점프, S 자동 sweep, T tracking, L 로그, O 오버레이. 보스 대용 구(원래 각도)와 조명(패킷 각도) 비교. config.json 안 건드림 (메모리에서 `deviceEnabled` 켬) |

**비전투 복귀**: `BossLookAt`이 전투 밖에서 rest로 돌아가므로 기본(`Tracking = true`)에서도 각도는 0으로 감. 보스를 숨기기만 하고 rest가 아닌 경우를 위해 `Tracking` 속성을 둠 (연결은 기획 세션 판단)

**가상 조명 배치**: 카메라 viewport (0.5, 1.06), 거리 14 (보스 10보다 뒤). 보스가 불투명 깊이를 쓰므로 빔 시작부가 보스 뒤로 가려져 보스 연출을 덮지 않음 (거리 10에서는 빔이 보스 오른쪽 링을 덮어서 14로 변경). 장착 각 `mountTiltDeg` -35로 빔이 화면 아래로 떨어짐. 레이어 1(TransparentFX, OverlayCamera 마스크 2). `MainVolumeProfile`(Bloom) + OverlayCamera post-processing 켠 상태에서 과노출 없음

**검증 수치**

| 항목 | 결과 |
|---|---|
| EditMode | 264/264 통과 (Device 23: 고정 바이트 `02 01 F5 10 04 D2 32 03` = pan -28.00 tilt 12.34, 왕복 2000건 오차 ≤ 0.01°, ±180/±90/범위 밖/∞/NaN, 체크섬·STX·ETX·길이·범위 오류 거부, 주기, 멈춤, 속도 제한, 꺼짐/켜짐 상태) |
| 송신 주기 (DeviceDev, 10초) | 300건 = 30.00 Hz |
| 실제 전투 연결 (CombatDev, 런타임 주입) | 보스 -28.0 ↔ 조명 -28.0, 보스 +27.4 ↔ 조명 +27.4. 로그 `02 01 F5 13 F7 B6 A6 03` = pan -27.97 (오른쪽 = 음수). 19초 571건 |
| 방향 | DeviceDev 목표 -30 → 빔 끝 viewport x 0.763 (오른쪽), +30 → 0.237 (왼쪽) |
| 속도 제한 | +30 → -90 점프 0.30초 뒤 -24.0 (54°/0.3초 = 180°/초) |
| Tracking off | 1.2초 뒤 송신·조명 모두 0.00 |
| 할당 (Play, ProfilerRecorder, 대조군 `new object()` ×600 = 600) | 송신+디코딩 경로(`DeviceSendLoop.Reset` → `DeviceController.Update` → `VirtualMovingLight.LateUpdate`) ×600 = **0**, 대기 경로 ×600 = **0** |
| 꺼진 상태 | `Initialize` 후 컨트롤러 `enabled = false`, 조명 GameObject 비활성 → Update 없음, 할당 0 (EditMode 테스트) |
| fps (에디터, 오버레이·로그 끔, 10초씩) | 켬 498.0 / 끔 506.4. 프레임 GC 카운트 332.48 / 332.37 (에디터 잡음, 차이 0.1) |

**메모**: Play 시작 전 Main.unity가 에디터에서 dirty 상태(디스크 변경 없음)라 `Temp/Main_dirty_backup.unity`에 사본 저장 후 씬 전환함. Main.unity 디스크 파일 변경 없음. Play로 `Assets/06_Font` 수정됨 (커밋 제외)

**통합 인계 (기획 세션)**
1. Main에 `03_Prefabs/Device/VirtualMovingLight.prefab` 배치, `DeviceController`를 담을 오브젝트 추가
2. `AppBootstrap`: `config.deviceEnabled`와 무관하게 아래 순서로 호출 (꺼짐이면 둘 다 스스로 꺼짐)
   ```csharp
   var sink = new LogPacketSink(config.deviceLogPackets);
   deviceController.Initialize(config, combatController, sink);   // ICombatStateSource = BossCombatController
   virtualMovingLight.Initialize(deviceController, overlayCamera); // 보스와 같은 OverlayCamera
   ```
3. `App` asmdef에 `VisionToWall.Device` 참조 추가
4. F1 패널: `PacketsSent`, `TryGetLastPacket(span)` + `LogPacketSink.FormatHex` (FormatHex는 문자열 할당 → 값이 바뀔 때만 갱신 권장)
5. 선택: 보스를 숨기는 단계에서 `deviceController.Tracking = false`
6. 카메라가 움직이거나 해상도가 바뀌면 `virtualMovingLight.Place()`
