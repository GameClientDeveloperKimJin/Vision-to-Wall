# HandSensor — 손 인식 외부 센서

웹캠 → MediaPipe HandLandmarker → 손 좌표 → UDP(기존 MotionProtocol 바이너리)로 Unity에 송신.
Unity는 `sensorMode: "external"`이면 웹캠을 열지 않고 수신만 한다. Unity 코드 변경 없음.

웹캠 차영상 센서와 달리 **움직이는 손에만** 반응한다 (몸만 움직이면 반응 없음).

같은 카메라 프레임으로 PoseLandmarker도 실행해 **몸 위치를 별도 스트림**(`udpBodyPort`, 기본 9001)으로 보낸다. 게임의 회피 판정에 쓴다. 손 스트림(9000)은 그대로다.

## 1. 설치 (최초 1회)

Python 3.10 ~ 3.12 필요 (3.12에서 검증). 시스템/conda base에 설치하지 말고 전용 venv 사용.

```bat
cd Tools\HandSensor
py -3.12 -m venv .venv
.venv\Scripts\python.exe -m pip install -r requirements.txt
.venv\Scripts\python.exe download_model.py
```

- 모델: MediaPipe 공식 `hand_landmarker.task` (float16, 약 7.8 MB), `pose_landmarker_lite.task` (float16, 약 5.8 MB) → `models/` (git 제외)
- `.venv/`, `models/`는 커밋하지 않음

## 2. 실행

1. Unity `config.json`에 `"sensorMode": "external"` (F1 패널 또는 파일 직접 수정) → Unity 재시작
2. 센서 실행

```bat
run_hand_sensor.bat              :: 기본 (palm, 로그만)
run_hand_sensor.bat --preview    :: 디버그 창 표시
```

또는 `.venv\Scripts\python.exe hand_sensor.py [옵션]`.

**웹캠은 한 프로세스만 열 수 있다.** Unity가 `webcam` 모드면 둘 중 하나가 카메라를 못 연다.

종료: 콘솔에서 `Ctrl+C`, 미리보기 창에서 `q` / `Esc` / 창 닫기.

## 3. 옵션

| 옵션 | 기본값 | 설명 |
|---|---|---|
| `--config PATH` | `Assets/StreamingAssets/config.json` | 빌드 환경: `<빌드폴더>/<앱>_Data/StreamingAssets/config.json` |
| `--points palm\|tips\|all` | `palm` | 손당 포인트: 손바닥 중심 1개 / 손가락 끝 5개 / 둘 다 6개 |
| `--speed-ref F` | config `handSpeedRef` → `1.5` | 이 속도(화면 폭/초)에서 강도 v = 1 |
| `--min-speed F` | config `handMinSpeed` → `0.05` | 이보다 느린 손은 송신 안 함 → 손을 가만히 두면 5초 뒤 대기 화면 |
| `--emit-still` | config `handEmitStill` → 끔 | 정지한 손도 v = 0.1로 송신 (손 위치 표시 연출용) |
| `--preview` | 끔 | OpenCV 창: landmark, 송신 포인트(초록 = 움직임, 회색 = 정지), FPS |
| `--quiet` | 끔 | 1초 로그 끔 |
| `--model PATH` | `models/hand_landmarker.task` | 손 모델 경로 |
| `--no-body` | 끔 | 몸 스트림 끔 (Pose 실행 안 함, 9001 송신 안 함). Unity는 모션 무게중심으로 대체 |
| `--pose-model PATH` | `models/pose_landmarker_lite.task` | Pose 모델 경로. 파일이 없으면 경고 후 손 스트림만 실행 |
| `--exposure N\|auto` | 카메라 설정 유지 | 수동 노출 (log2 초, `-7` = 1/128초). 빠른 손의 모션 블러를 줄임. 정상 종료 시 자동 노출 복구. `auto` = 자동 노출로 설정 |
| `--duration N` | `0` | N초 후 자동 종료 (0 = 무한, 테스트용) |

### config.json에서 읽는 값 (파일은 **읽기만** 한다)

| 키 | 기본값 | 적용 시점 |
|---|---|---|
| `cameraIndex`, `cameraWidth`, `cameraHeight`, `mirror`, `udpIp`, `udpPort`, `udpBodyPort` | 0, 640, 480, true, 127.0.0.1, 9000, 9001 | 시작할 때. 바꾸면 센서 재시작 필요 (로그로 알림) |
| `handSpeedRef` | 1.5 | **실행 중 즉시** (0.5초마다 파일 변경 확인) |
| `handMinSpeed` | 0.05 | **실행 중 즉시** |
| `handEmitStill` | false | **실행 중 즉시** |

- 속도 조절: `config.json`의 `hand*` 값을 고치고 저장하면 재시작 없이 반영된다 (`[config] reloaded: ...` 로그)
- 명령줄 옵션(`--speed-ref` 등)을 주면 그 값으로 **고정**되고 config 변경은 무시된다
- 범위 밖 값(`handSpeedRef <= 0`, `handMinSpeed < 0`)이나 저장 중인 파일은 무시하고 이전 값 유지
- 주의: Unity `AppConfig`에 `hand*` 필드가 추가되기 전에는 F1 패널에서 저장하면 이 값들이 지워진다 (기획 세션에 요청함)

## 4. 동작

- 매 프레임 패킷 송신. 움직이는 손이 없어도 `count 0` 패킷 송신 (연결 유지)
- 좌표: x 0(좌)~1(우), y 0(아래)~1(위). `mirror: true`면 x를 뒤집어 거울처럼 (프레임은 안 뒤집음)
- 강도: `v = clamp01(손바닥 속도 / speedRef)`. 속도는 프레임 간 손바닥 이동 거리(화면 폭 단위) / 시간, 지터 완화용 EMA 적용
- 손 매칭: 이전 프레임 손바닥과 가장 가까운 손 (좌/우 라벨 보조). 새로 나타난 손은 첫 프레임 송신 안 함
- 끊김 방지 (hold): 움직이던 손은 멈춘 뒤에도 **0.3초 동안** 계속 송신 (강도는 최대값에서 절반까지 서서히 감소). 손을 흔들 때 방향이 바뀌는 순간 끊기지 않게 함
- 놓친 손: 빠른 동작으로 인식이 잠깐 끊겨도 0.3초 동안은 마지막 위치로 송신하고, 다시 잡히면 같은 손으로 이어감. 놓친 동안 손이 움직인 만큼 매칭 거리를 늘림 (초당 화면 폭 4배)
- 로그의 `held=N`: 지난 1초 중 hold 덕분에 송신된 프레임 수
- 카메라 열기 실패 시 2초 간격 재시도. 읽기 연속 실패 시 다시 열기

몸 스트림:

- 매 프레임 `udpBodyPort`로 송신. 패킷 형식은 같고 `seq`는 손 스트림과 독립. `count` 0 = 사람 없음, 1 = `x, y` 몸 중심, `v` 몸 이동 속도 (화면 폭/초, 1에서 포화)
- 몸 중심: 어깨(11, 12)와 엉덩이(23, 24) 중 visibility 0.5 이상인 점의 평균. 웹캠 앞에 앉아 엉덩이가 안 보여도 동작하며, 어깨가 모두 안 보이면 사람 없음
- 좌표 변환은 손과 같음 (Mirror, y 반전, clamp)
- 손+Pose 추론 시간의 이동 평균(30프레임)이 40 ms를 넘으면 Pose를 격 프레임으로 실행하고 이전 값을 다시 보냄. 30 ms 미만이면 매 프레임으로 복귀. 카메라 때문에 fps가 낮은 경우에는 격 프레임으로 내려가지 않음
- 미리보기 창: 몸 중심을 분홍색 십자로 표시

로그 (1초마다):
```
fps=24.0 hands=1 points=1 seq=50 held=3 body=1 poseSkip=0 inferMs=17.0
```

`body=1`: 사람 인식 중, `poseSkip=1`: Pose 격 프레임 실행 중, `inferMs`: 손+Pose 추론 시간 이동 평균.

## 5. 권장 조명과 카메라 조건

빠르게 흔드는 손을 놓치는 원인은 모션 블러다. 측정 카메라: Logi C270, 640×480, 2026-11-06 (기록: `Assets/05_Docs/38_세션GS_HandTracking.md`).

- **권장: `run_hand_sensor.bat --exposure -7`**. 측정 환경에서 한 손 빠른 흔들기 놓침 0% (2회), 자동 노출은 0.4~36%
- 조명: 방의 밝은 등을 켠 상태에서 측정했다. 이때 `-7`의 화면 평균 밝기는 약 40~70이고 손과 몸 인식을 확인했다. 미리보기가 그보다 어두우면 조명을 늘리거나 `-6`을 쓴다 (밝기 약 95, 놓침 1.7%)
- 어두운 방에서는 측정하지 않았다. 설치 장소마다 미리보기로 확인할 것
- C270에서 듣지 않는 설정: `MJPG` 요청(YUY2 유지), 30 fps 요청. 자동 노출은 24 fps 또는 30 fps로 동작하고, 수동 노출은 30 fps
- 인식 신뢰도를 낮추지 말 것. presence 0.1에서 놓침은 줄지만, 손이 없는데 인식해 실드 수준의 `v`가 나왔다
- **수동 노출은 카메라를 닫아도 유지된다.** 콘솔 창을 닫아 강제 종료하면 복구되지 않아 Unity 웹캠 모드에도 영향을 준다. 이때는 `run_hand_sensor.bat --exposure auto --duration 3` 실행

## 6. 테스트

```bat
.venv\Scripts\python.exe -m pytest -q tests
```

## 7. 문제 해결

| 증상 | 원인 / 조치 |
|---|---|
| `[camera] open failed` 반복 | Unity가 `webcam` 모드로 카메라 점유 중 → `sensorMode: "external"` 후 Unity 재시작. 또는 `cameraIndex` 확인 |
| `[config] WARNING sensorMode='webcam'` | 위와 같음. 경고만 하고 계속 실행 |
| `model not found` | `download_model.py` 실행 (bat은 자동 실행) |
| `[body] model not found` | Pose 모델 없음. 손 스트림만 실행 중. `download_model.py` 실행 |
| Unity가 반응 없음 | `udpIp`/`udpPort`가 Unity와 같은지, F1 패널 패킷 수신 수 증가 확인. 방화벽 |
| 빠르게 흔들면 손 표시가 끊김 | 모션 블러. `--exposure -7` (5절) |
| 영상이 계속 어두움 | 수동 노출이 남아 있음. `--exposure auto --duration 3` 실행 (5절) |
| 손을 흔들어도 반응 약함 | `handSpeedRef`를 낮춤 (예: `1.0`) |
| 손을 가만히 둬도 반응 | `handMinSpeed`를 올림 (예: `0.08`) |
| config를 고쳐도 반영 안 됨 | 명령줄 옵션으로 고정했는지 확인 (시작 로그 `fixed by CLI`). `--config` 경로 확인 |
| fps 20 미만 (예: 15) | 카메라 자체가 느리게 보내는 경우 (자동 노출 — 조명이 약하면 노출 시간이 길어져 15 fps로 떨어짐). 카메라만 단독으로 읽어도 15 fps인지 확인. 조명 확보가 해결책. 추론 자체는 손 + Pose 합쳐 CPU 약 17 ms/프레임 (2026-11-06 측정) |
| 시작 시 `W0000`/`INFO` 로그 | MediaPipe/TFLite 내부 로그. 무시해도 됨 |
