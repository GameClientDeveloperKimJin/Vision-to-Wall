# 14. 세션 E — HandSensor (MediaPipe 손 인식 외부 센서)

- 담당자: **세션 E** (세션 이름: `E HandSensor`)
- 브랜치: `feature/hand-sensor`
- 담당: 손 인식 외부 센서 (요구사항 외 추가 기능, 2026-11-06 사용자 결정)
- 수정 가능 폴더: `Tools/HandSensor/` (그 외 전부 금지. Unity 쪽 준비는 기획 세션이 완료함)
- 작업 폴더: `D:/UnityProject/3D/InteractiveGame` (단일 폴더, 순차 방식)

## 0. 배경과 목표

웹캠 차영상 센서(세션 A)는 **화면에서 밝기가 변한 곳 전부**에 반응한다. 그래서 손을 가만히 두고 몸만 움직여도 파티클이 튄다.
세션 E는 **손에만 반응하는 센서**를 별도 Python 프로세스로 만든다.

```
[HandSensor.py]  웹캠 → MediaPipe HandLandmarker → 손 좌표 → UDP 127.0.0.1:9000 (기존 바이너리 형식)
                                                              │
[Unity]          sensorMode = "external" → 웹캠 안 열고 수신만 → Render / UI (코드 변경 없음)
```

- 핵심: **Unity의 Network·Render·UI는 수정하지 않는다.** 패킷 형식만 맞추면 그대로 동작해야 한다 (센서 교체형 설계 검증).
- 웹캠은 한 프로세스만 열 수 있다. HandSensor 실행 시 Unity는 반드시 `sensorMode: "external"`.

## 1. 기획 세션이 이미 준비한 것 (main)

| 항목 | 내용 |
|---|---|
| `AppConfig.sensorMode` | `"webcam"`(기본) 또는 `"external"`. 재시작 필요 |
| `AppBootstrap` | `external`이면 Unity Sensor 오브젝트 비활성, 송신기 미초기화, F1 패널 카메라 표시 = "외부 센서 (UDP)" |
| `.gitignore` | `Tools/HandSensor/.venv/`, `models/`, `build/`, `dist/`, `*.spec` |
| 검증 | external 모드 + `fake_sender.py` → 수신·Active 전환 확인 (MCP) |

## 2. 납품물

| 파일 | 역할 |
|---|---|
| `Tools/HandSensor/hand_sensor.py` | 메인. 웹캠 → 손 인식 → UDP 송신 |
| `Tools/HandSensor/motion_packet.py` | 순수 함수: 패킷 패킹, 좌표 변환, 강도 계산 (테스트 대상) |
| `Tools/HandSensor/download_model.py` | `hand_landmarker.task` 모델 다운로드 → `models/` (커밋 안 함) |
| `Tools/HandSensor/requirements.txt` | 버전 고정 (`mediapipe`, `opencv-python`, `numpy`, `pytest`) |
| `Tools/HandSensor/tests/test_motion_packet.py` | pytest |
| `Tools/HandSensor/run_hand_sensor.bat` | venv 활성화 + 실행 (전시 현장용) |
| `Tools/HandSensor/README.md` | 설치·실행·옵션·문제 해결 |

## 3. 인터페이스 규격 (변경 금지)

### 3.1 UDP 패킷 — [01_Core_공통계약](01_Core_공통계약.md) 2절과 동일
| 오프셋 | 크기 | 타입 | 필드 |
|---|---|---|---|
| 0 | 4 | uint32 LE | magic `0x57325601` |
| 4 | 4 | uint32 LE | seq (1부터 증가, 재시작 시 1) |
| 8 | 4 | uint32 LE | timestampMs |
| 12 | 2 | uint16 LE | count (0 허용) |
| 14 | 12 × count | float32 LE × 3 | x, y, v |

- Python: `struct.pack("<IIIH", ...)` + `struct.pack("<fff", ...)` × count.
- **매 처리 프레임마다 송신, 손이 없어도 count 0 패킷 송신** (연결 유지).
- 참고 구현: `Tools/FakeSender/fake_sender.py` (세션 B). 읽기만 하고 수정하지 말 것.

### 3.2 좌표계
- 출력: x 0(좌)~1(우), **y 0(아래)~1(위)**. MediaPipe는 y가 아래로 증가 → `y = 1 - y_mp`.
- Mirror: `config.mirror == true`면 `x = 1 - x_mp` (거울처럼). 프레임을 뒤집지 말고 좌표만 변환.
- 범위 밖 값은 0~1로 clamp.

### 3.3 설정 — `config.json` 공유 (단일 설정 원본)
- 기본 경로: `Assets/StreamingAssets/config.json`. 빌드 환경: `--config <빌드폴더>/<앱>_Data/StreamingAssets/config.json`.
- 읽는 필드: `cameraIndex`, `cameraWidth`, `cameraHeight`, `mirror`, `udpIp`, `udpPort`. 없는 필드는 기본값.
- config 파일을 **쓰지 않는다** (Unity F1 패널이 저장 담당).

## 4. 처리 설계

1. **입력**: `cv2.VideoCapture(cameraIndex, cv2.CAP_DSHOW)`, 640×480 요청. 열기 실패 시 2초 간격 재시도 + 로그.
2. **인식**: MediaPipe Tasks `HandLandmarker`, `running_mode=VIDEO`, `num_hands=2`, 신뢰도 0.5.
3. **포인트 생성** (`--points` 옵션):
   - `palm` (기본): 손당 1개, 손바닥 중심 = landmark 0, 5, 9, 13, 17 평균
   - `tips`: 손당 5개, 손가락 끝 4, 8, 12, 16, 20
   - `all`: palm + tips
4. **강도 v** (움직임 기반 — "가만히 둔 손"은 반응 약하게):
   - 손별 palm 속도 = 이전 프레임 대비 정규화 거리 / dt
   - `v = clamp01(speed / speedRef)`, 기본 `speedRef = 1.5` (화면 폭/초)
   - `speed < minSpeed`(기본 0.05)이면 그 손의 포인트는 **보내지 않음** → 손을 가만히 두면 5초 뒤 Idle
   - `--emit-still` 옵션: 정지한 손도 `v = 0.1`로 송신 (손 위치 표시용 연출이 필요할 때)
   - 손 매칭: 이전 프레임 palm과 가장 가까운 손 (handedness 라벨 보조). 새 손은 첫 프레임 송신 안 함
5. **미리보기** (`--preview`): OpenCV 창에 landmark, 송신 포인트, FPS 표시. `q`/`Esc` 종료. 현장 디버그용 (Unity F1의 원본/차분 뷰를 대체).
6. **종료**: Ctrl+C / 창 닫기 → 카메라·소켓 해제, 마지막 통계 출력.
7. **로그**: 1초마다 `fps=.. hands=.. points=.. seq=..` (옵션 `--quiet`로 끔).

### CLI
```
python hand_sensor.py [--config PATH] [--points palm|tips|all] [--speed-ref 1.5] [--min-speed 0.05]
                      [--emit-still] [--preview] [--quiet] [--model models/hand_landmarker.task]
```

## 5. 성능·환경 목표
- 640×480에서 **≥ 20 fps** (CPU). 미달 시 `--points palm`, 입력 해상도 유지, 모델 complexity 확인 후 보고.
- Python 3.10~3.12 + 프로젝트 전용 venv `Tools/HandSensor/.venv` (시스템/conda base에 설치 금지).
- 외부 다운로드: PyPI 패키지, MediaPipe 공식 모델 URL만.

## 6. 체크리스트

### 구현 — 담당: 세션 E
- [ ] venv 생성 + `requirements.txt` (버전 고정)
- [ ] `download_model.py` (이미 있으면 건너뜀)
- [ ] `motion_packet.py`: `pack_packet`, `to_output_coords(mirror)`, `palm_center`, `intensity(speed)`
- [ ] pytest: 패킷 길이·필드 값, count 0, 좌표 변환(mirror on/off, y 반전, clamp), 강도 clamp
- [ ] `hand_sensor.py`: config 로드, 카메라 재시도, HandLandmarker VIDEO 모드
- [ ] 포인트 모드 3종, 손 매칭, 속도 기반 v, minSpeed 필터, `--emit-still`
- [ ] count 0 keep-alive 송신, seq/timestamp
- [ ] `--preview` 창, 1초 로그, 정상 종료
- [ ] `run_hand_sensor.bat`, `README.md`

### 자동 검증 — 담당: 세션 E
- [ ] `pytest` 전부 통과
- [ ] Unity `sensorMode: "external"`로 바꿔 Play (MCP) → `hand_sensor.py` 실행 → `UdpMotionReceiver.PacketsReceived` 증가, `PacketsDropped` 0 (Unity 창 포커스 필요 — 사용자에게 요청)
- [ ] 10초 이상 실행 fps 로그 ≥ 20
- [ ] 검증 후 `config.json`을 `"webcam"`으로 되돌림 (커밋 금지 파일)

### 사람 검증 — 담당: 사용자 (최대 3개)
- [ ] 손을 흔들면 그 위치에 파티클·물결, **손을 가만히 두고 몸만 움직이면 반응 없음**
- [ ] 손을 화면 오른쪽으로 옮기면 월의 오른쪽에서 반응 (거울처럼)
- [ ] 손을 내리고 5초 → 대기 화면 복귀

## 7. 제외 범위
- Unity 코드·씬·프리팹·config 기본값 변경 (기획 세션)
- 제스처 인식, 손 모양 분류 — v2
- PyInstaller exe 패키징 — 시간 남으면 별도 요청

## 8. 완료 보고
`feature/hand-sensor` 커밋·push → 이 문서 작업 로그 갱신 → 기획 세션에 병합 요청 메시지 (측정 fps, 검증 결과 포함).

## 작업 로그
- (세션 E가 기록)
