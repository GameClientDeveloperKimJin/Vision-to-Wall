# 32. 세션 G1 — Player Input

- 담당자: **G1 Player Input**
- 브랜치: `feature/game-input`
- 담당: G-FR-03 (플레이어 좌표, 모션 강도)
- 수정 가능: `Tools/HandSensor/**`, `Assets/02_Scripts/Game/Input/**`, `Assets/02_Scripts/Game/Tests/Input/**`, `Assets/02_Scripts/Game/Dev/Input/**`, `Assets/02_Scripts/Network/UdpMotionReceiver.cs` (포트 오버로드만), 이 문서
- 선행 조건: 기획 세션 계약 완료

## 1. 목표

게임 로직이 센서 종류를 몰라도 되도록 `IPlayerInputSource` 하나로 입력을 제공한다. 회피에 쓸 몸 위치를 MediaPipe Pose로 추가한다.

## 2. 납품물

| 파일 | 역할 |
|---|---|
| `Tools/HandSensor/body_tracker.py` | 순수 로직: 랜드마크 → 몸 중심, 속도 |
| `Tools/HandSensor/hand_sensor.py` 수정 | PoseLandmarker 추가, 9001 송신, `--no-body` |
| `Tools/HandSensor/download_model.py` 수정 | `pose_landmarker_lite.task` 다운로드 |
| `Tools/HandSensor/sensor_config.py` 수정 | `udpBodyPort` 읽기 |
| `Network/UdpMotionReceiver.cs` | `Initialize(AppConfig config, int port)` 추가, 기존 시그니처 유지 |
| `Game/Input/PlayerInputTracker.cs` | MonoBehaviour. `IPlayerInputSource` 구현 |
| `Game/Input/PlayerInputFilter.cs` | 순수 C#. 위치 선택, 무게중심, 평활 (테스트 대상) |
| `Game/Dev/Input/PlayerInputDev.unity` | 위치 마커, 강도, 소스 표시 테스트 씬 |

공개 API (App이 호출):

```csharp
public void Initialize(AppConfig config, IPointSource motion, IPointSource bodyOrNull);
```

## 3. 처리 설계

### 3.1 Python: 몸 스트림
- 같은 카메라 프레임으로 `PoseLandmarker` (VIDEO 모드, lite 모델, 1인) 실행
- 몸 중심 = 어깨(11, 12)와 엉덩이(23, 24) 중 visibility 0.5 이상인 점의 평균. 어깨 2개가 모두 안 보이면 사람 없음으로 처리. 웹캠 앞에 앉으면 엉덩이가 안 보이므로 어깨만으로도 동작해야 함
- 좌표 변환은 기존 `to_output_coords` 재사용 (Mirror, y 반전, clamp)
- 매 프레임 `udpBodyPort`로 패킷 송신. 사람 없으면 `count` 0
- `seq`는 손 스트림과 독립
- 손 인식, F1 실시간 조절 등 기존 동작 변경 금지
- 손+Pose 추론 시간의 이동 평균이 40 ms를 넘으면 Pose를 격 프레임으로 실행하고 이전 값 유지, 30 ms 미만이면 해제

### 3.2 위치 선택 (`PlayerInputFilter`)
우선순위:
1. 몸 스트림: 마지막 유효 패킷이 0.3초 이내 → `source = Body`
2. 모션 스트림 무게중심 (`v` 가중 평균): `source = MotionCentroid`
3. 둘 다 없음: 마지막 위치를 1초 유지 후 `hasPosition = false`, `source = None`

위치는 지수 평활(시정수 0.1초)을 적용한다. 소스가 바뀔 때 위치가 튀지 않도록 평활을 유지한다.

### 3.3 모션 강도
- 모션 스트림(9000)만 사용. 몸을 움직이는 것과 손을 흔드는 것을 구분하기 위함
- external: `max(v)`. webcam: `clamp01(sum(v) / 8)` (`MediaWallController.activitySaturation`과 같은 기준)
- 상승은 즉시, 하강은 지수 감쇠 (시정수 0.25초)
- 모드 판단은 `AppConfig.UseExternalSensor`

### 3.4 알려진 한계
모션 강도는 "양손"을 구분하지 않는다. 한 손을 빠르게 흔들어도 실드가 발동할 수 있으며 프로토타입 범위에서 허용한다.

## 4. 체크리스트

### 구현 — 담당: G1 Player Input
- [x] `body_tracker.py` + pytest (어깨만, 어깨+엉덩이, visibility 부족, 좌표 변환)
- [x] `hand_sensor.py` Pose 추가, 9001 송신, `--no-body`, 미리보기에 몸 중심 표시
- [x] `download_model.py`, `sensor_config.py`, `README.md` 갱신
- [x] `UdpMotionReceiver` 포트 오버로드 + 테스트
- [x] `PlayerInputFilter`, `PlayerInputTracker`
- [x] `PlayerInputDev.unity`

### 자동 검증 — 담당: G1 Player Input
- [x] 전체 pytest 통과 (64개)
- [x] EditMode: 소스 우선순위, 소스 전환, 1초 유지, 무게중심, 모드별 강도 계산 (전체 74/74)
- [x] `fake_sender.py 127.0.0.1 9001 30`으로 몸 스트림 수신 → `source = Body`, 끊으면 0.3초 뒤 대체 확인
- [x] 손+몸 동시 실행: 추론 합계 40 ms 미만 + poseSkip 0 (16.5~16.9 ms, poseSkip 0. 화면에 손 없는 상태로 측정)
- [x] GC 0 B/frame (`PlayerInputTracker`)

### 사람 검증 — 담당: 사용자
- [x] 몸을 좌우로 움직이면 Dev 씬 마커가 같은 방향으로 따라감 (거울처럼)
- [x] 손을 멈추고 몸만 움직이면 위치는 변하고 강도는 낮게 유지됨
- [x] 손을 빠르게 흔들면 강도가 0.8을 넘고, 멈추면 내려옴

## 5. 통합 인계

기획 세션 작업: Main에 두 번째 `UdpMotionReceiver`와 `PlayerInputTracker` 배치, `AppBootstrap` 연결 (external일 때만 몸 수신기 전달).

## 6. 제외 범위
보스 조준, 실드 판정(G3), F1 표시(G6), `sensorMode` 기본값 변경

## 작업 로그
### 2026-11-06 구현과 자동 검증

| 항목 | 결과 |
|---|---|
| pytest | 64 통과 (기존 43 + 신규 21) |
| EditMode | 74/74 통과 (기존 52 + 신규 22) |
| 소스 전환 (fake_sender) | 몸 패킷 중단 약 0.3초 뒤 `MotionCentroid`, 모션 중단 약 1.3초 뒤 `None` (0.3초 신선도 + 1초 유지) |
| 패킷 손실 | 몸 300, 모션 600 수신, drop 0 |
| GC | `OnMotionFrame` + `OnBodyFrame` + `Update` 600회 0 alloc, 0 B (대조군 `new object()` 600회 600 alloc) |
| fps (`--no-body` / 손+몸) | 15.2 / 15.1 |
| 카메라만 읽을 때 fps | 13.7 |
| 추론 시간 | 손 8.0 ms, Pose 8.8 ms |
| 격 프레임 기준 변경 후 (25초, 손 없음, 사람 인식 중) | fps 15.1, 추론 합계 16.5~16.9 ms, poseSkip 0, 몸 패킷 348/348 프레임 |

이 측정 환경의 fps는 약 15다. 카메라만 읽어도 15 안팎이고 Pose를 켜도 fps가 변하지 않으므로 병목은 카메라다. 밝은 조명에서는 측정하지 않았다. 기획 세션 결정으로 완료 기준을 fps에서 추론 시간으로 바꿨다.

사람 검증 3개는 2026-11-06 사용자 확인으로 통과. 구현 커밋: `61319fd`.

### 구현 결정
- 위치 유지 중(0.3초 신선도 이후 1초)에는 `hasPosition = true`, `source`는 직전 소스를 유지한다. 1초가 지나면 `false` / `None`
- 모션 무게중심도 몸과 같은 0.3초 신선도를 쓴다. 손 방향 전환 때 `count` 0 패킷이 한 번 와도 소스가 끊기지 않는다
- 센서가 죽어 패킷이 끊기면 모션 강도 목표값은 0.3초 뒤 0이 된다 (마지막 강도로 굳어 실드가 계속 발동하는 것을 방지)
- 격 프레임 기준은 손+Pose 추론 시간의 30프레임 이동 평균이다 (기획 세션 결정). fps 기준이면 카메라 병목일 때 CPU 여유가 있어도 몸 스트림이 7.5 Hz로 떨어진다. 평균은 30프레임이 찬 뒤부터 판단하고, Pose를 실행한 프레임만 집계한다
- Pose 모델이 없으면 종료하지 않고 경고 후 손 스트림만 실행한다 (Unity가 무게중심으로 대체)
- `sensor_config.py`도 `udpBodyPort == udpPort`이면 `ConfigService.Sanitize`와 같은 규칙으로 보정한다
- `VisionToWall.Game`은 Network를 참조할 수 없으므로 Dev 스크립트와 수신기 테스트에 자체 asmdef를 뒀다: `VisionToWall.Game.Dev.Input`, `VisionToWall.Game.Input.NetworkTests`
- Dev 씬은 웹캠을 열지 않으므로 `forceExternal`(기본 켜짐)로 메모리에서만 external로 처리한다. `config.json` 수정 불필요