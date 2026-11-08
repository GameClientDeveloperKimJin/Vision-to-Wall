# HandSensor — 손 인식 외부 센서

웹캠 → MediaPipe HandLandmarker → 손 좌표 → UDP(기존 MotionProtocol 바이너리)로 Unity에 송신.
Unity는 `sensorMode: "external"`이면 웹캠을 열지 않고 수신만 한다. Unity 코드 변경 없음.

웹캠 차영상 센서와 달리 **움직이는 손에만** 반응한다 (몸만 움직이면 반응 없음).

같은 카메라 프레임으로 PoseLandmarker도 실행해 **몸 위치를 별도 스트림**(`udpBodyPort`, 기본 9001)으로 보낸다. 게임의 회피 판정에 쓴다. 손 스트림(9000)은 그대로다.

같은 Pose 결과에서 **관절 13개를 별도 스트림**(`udpPosePort`, 기본 9002)으로도 보낸다. 미니 캐릭터·전투 아바타용 (4절).

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

종료: 콘솔에서 `Ctrl+C` / `Ctrl+Break`, 미리보기 창에서 `q` / `Esc` / 창 닫기, `--stop-file` 파일 생성(런처). 모두 정상 종료이고 수동 노출을 자동으로 되돌린다. 콘솔 창 X 버튼도 같은 처리를 하도록 만들었으나 자동 시험 방법이 없어 미검증 (Windows Terminal 호스트). 작업 관리자·`taskkill /F` 강제 종료는 복구하지 못한다 (5절).

전시 운영(원클릭 실행, 감시, 자동 시작)은 `Tools/Launcher/README.md`.

## 3. 옵션

| 옵션 | 기본값 | 설명 |
|---|---|---|
| `--config PATH` | `Assets/StreamingAssets/config.json` | 빌드 환경: `<빌드폴더>/<앱>_Data/StreamingAssets/config.json` |
| `--points palm\|tips\|all` | `palm` | 손당 포인트: 손바닥 중심 1개 / 손가락 끝 5개 / 둘 다 6개 |
| `--speed-ref F` | config `handSpeedRef` → `1.5` | 이 속도(화면 폭/초)에서 강도 v = 1 |
| `--min-speed F` | config `handMinSpeed` → `0.05` | 이보다 느린 손은 송신 안 함 → 손을 가만히 두면 5초 뒤 대기 화면 |
| `--emit-still` | config `handEmitStill` → 끔 | 정지한 손도 v = 0.1로 송신 (손 위치 표시 연출용) |
| `--preview` | 끔 | OpenCV 창: landmark, 송신 포인트(초록 = 움직임, 회색 = 정지), FPS. Enter = 관절 녹화 시작/정지 (4절) |
| `--quiet` | 끔 | 1초 로그 끔 |
| `--model PATH` | `models/hand_landmarker.task` | 손 모델 경로 |
| `--no-body` | 끔 | 몸 스트림 끔 (Pose 실행 안 함, 9001·9002 송신 안 함). Unity는 모션 무게중심으로 대체 |
| `--pose-model PATH` | `models/pose_landmarker_lite.task` | Pose 모델 경로. 파일이 없으면 경고 후 손 스트림만 실행 |
| `--exposure N\|auto` | config `cameraExposure` → 카메라 설정 유지 | 수동 노출 (log2 초, `-7` = 1/128초). 빠른 손의 모션 블러를 줄임. 정상 종료 시 자동 노출 복구. `auto` = 자동 노출로 설정. 주면 config보다 우선 |
| `--duration N` | `0` | N초 후 자동 종료 (0 = 무한, 테스트용) |
| `--stop-file PATH` | 없음 | 이 파일이 생기면 0.25초 안에 정상 종료 (노출 복구). 시작·종료 때 지움. 런처가 사용 |
| `--status-file PATH` | 없음 | 1초마다 JSON 상태 기록: `pid`, `state`(`starting` → `camera` → `running` → `stopped`), `fps`, `frames`, `exposure`, `time`. 런처의 준비·멈춤 판정용 |

### config.json에서 읽는 값 (파일은 **읽기만** 한다)

| 키 | 기본값 | 적용 시점 |
|---|---|---|
| `cameraIndex`, `cameraWidth`, `cameraHeight`, `mirror`, `udpIp`, `udpPort`, `udpBodyPort`, `udpPosePort` | 0, 640, 480, true, 127.0.0.1, 9000, 9001, 9002 (0 = 끔) | 시작할 때. 바꾸면 센서 재시작 필요 (로그로 알림) |
| `cameraExposure` | `"-7"` (저장소 config). 키가 없거나 `""`면 카메라 설정 유지 | 카메라를 열 때. `"auto"` = 자동 노출, `"-7"` 같은 숫자 = 수동 노출 (`-13`~`0`). 잘못된 값은 경고 후 카메라 설정 유지. `--exposure`가 있으면 무시. 시작 로그 `[camera] exposure setting: -7.0 (config)` |
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

관절 스트림 (`udpPosePort`, 기본 9002, `0` = 끔). 규격 원본: `Assets/02_Scripts/Core/PoseProtocol.cs`, `Assets/05_Docs/52_세션R2_PoseStream.md`:

- 패킷 (little-endian): `uint32 magic 0x57325602 | uint32 seq | uint32 timestampMs | uint16 count | count × float32 6개`
- `count` 0 = 사람 없음, 아니면 13. 관절마다 `화면 x, 화면 y, 월드 x, 월드 y, 월드 z, visibility`. 크기 326바이트 (빈 패킷 14바이트)
- 관절 순서: 코, 왼/오 어깨, 왼/오 팔꿈치, 왼/오 손목, 왼/오 골반, 왼/오 무릎, 왼/오 발목 (MediaPipe 0, 11~16, 23~28)
- 왼쪽·오른쪽은 **플레이어 몸 기준**. 미러는 좌표 x에만 적용하고 라벨은 바꾸지 않음
- 화면 좌표: 손·몸 스트림과 같음 (0~1, 원점 왼쪽 아래, 미러, clamp)
- 월드 좌표: MediaPipe `pose_world_landmarks` (미터, 골반 중심)를 Unity 축으로 변환. x 오른쪽 (미러 적용), y 위, z 카메라에서 멀어지는 쪽
- `timestampMs`: 프레임 캡처 시각 (`time.monotonic()` ms)
- Pose가 실행된 프레임마다 1패킷. `poseSkip`으로 건너뛴 프레임은 보내지 않음. 사람이 사라지면 count 0을 1번 보내고 다시 잡힐 때까지 송신 없음
- visibility가 낮은 관절도 그대로 보냄 (판단은 수신 측). `--no-body`면 관절 스트림도 끔
- `udpPosePort`가 `udpPort`나 `udpBodyPort`와 같으면 경고 후 끔 (Unity `ConfigService`와 같은 규칙)

로그 (1초마다):
```
fps=24.0 hands=1 points=1 seq=50 held=3 body=1 poseSkip=0 inferMs=17.0 joints=13
```

`body=1`: 사람 인식 중, `poseSkip=1`: Pose 격 프레임 실행 중, `inferMs`: 손+Pose 추론 시간 이동 평균, `joints`: 마지막 관절 패킷의 관절 수 (13 또는 0).

### 관절 스트림 확인 도구

Unity 없이 9002를 확인한다. **Unity가 9002를 수신 중이면 같이 쓸 수 없다** (포트 하나에 수신자 하나).

```bat
.venv\Scripts\python.exe pose_monitor.py                 :: 수신, 코·손목·팔꿈치를 초당 4줄 출력
.venv\Scripts\python.exe pose_monitor.py --all --rate 2  :: 관절 13개 전부
.venv\Scripts\python.exe fake_pose_sender.py             :: 카메라 없이 가짜 관절 송신 (30 Hz)
.venv\Scripts\python.exe fake_pose_sender.py --pattern raise --no-cycle
.venv\Scripts\python.exe pose_from_media.py <frame_*.png 폴더 또는 영상> [--send]  :: 같은 처리로 관절 CSV 생성 (RT 합성 검증, 56번 문서)
```

- `pose_monitor.py` 출력: `R_Wrist scr(x,y) w(x,y,z) v0.99` = 오른쪽 손목의 화면 좌표, 월드 좌표, visibility. 종료 시 패킷 수, count 0 수, seq 끊김, 지연(캡처 → 수신, 같은 PC에서만 유효)
- `fake_pose_sender.py` 패턴: `wave` 오른팔 들고 흔들기 (기본), `raise` 오른팔 2초 → 왼팔 2초 번갈아 들기 (좌우 라벨 확인용), `jacks` 양팔·다리 벌렸다 모으기. 기본으로 8초 사람 있음 → count 0 → 3초 없음 반복 (`--no-cycle`로 끔)
- `fake_pose_sender.py`와 `hand_sensor.py`를 같은 포트로 동시에 보내지 말 것 (seq 충돌)

### 관절 녹화와 팔 들기 확인

`--preview` 창에서 **Enter = 녹화 시작, 다시 Enter = 정지** (창 오른쪽 위 `REC N`). 9002로 보낸 관절 값만 기록하고 카메라 영상은 저장하지 않는다.

- 저장: `recordings/pose_YYYYMMDD_HHMMSS.csv` (git 제외). 녹화 중 종료해도 저장
- 정지하면 콘솔에 판정 출력. 손목 visibility 0.5 이상이고 화면 y가 코보다 높으면 "든 상태"
  - 오른팔: 1초 이상 들었고, 그때 화면 x가 오른쪽 절반(미러), 월드 y가 내렸을 때보다 높음
  - 왼팔: 같은 조건, 화면 x 왼쪽 절반
- 다시 판정: `.venv\Scripts\python.exe pose_record.py recordings\pose_....csv`

출력 형식 (숫자는 예시):
```
[record] recording: 26.3s, 780 packets (780 with a person, 0 count 0)
[record] right arm: PASS  raised 9.8s, screen x 0.71 (right half), world y up +0.62 / down +0.05
[record] left arm: PASS  raised 9.1s, screen x 0.30 (left half), world y up +0.60 / down +0.07
[record] result: PASS
```

## 5. 권장 조명과 카메라 조건

빠르게 흔드는 손을 놓치는 원인은 모션 블러다. 측정 카메라: Logi C270, 640×480, 2026-11-06 (기록: `Assets/05_Docs/38_세션GS_HandTracking.md`).

- **권장 노출 `-7`** (config `cameraExposure` 기본값, 2026-11-08부터 옵션 없이 적용). 측정 환경에서 한 손 빠른 흔들기 놓침 0% (2회), 자동 노출은 0.4~36%
- 조명: 방의 밝은 등을 켠 상태에서 측정했다. 이때 `-7`의 화면 평균 밝기는 약 40~70이고 손과 몸 인식을 확인했다. 미리보기가 그보다 어두우면 조명을 늘리거나 `-6`을 쓴다 (밝기 약 95, 놓침 1.7%)
- 어두운 방에서는 측정하지 않았다. 설치 장소마다 미리보기로 확인할 것
- C270에서 듣지 않는 설정: `MJPG` 요청(YUY2 유지), 30 fps 요청. 자동 노출은 24 fps 또는 30 fps로 동작하고, 수동 노출은 30 fps
- 인식 신뢰도를 낮추지 말 것. presence 0.1에서 놓침은 줄지만, 손이 없는데 인식해 실드 수준의 `v`가 나왔다
- **수동 노출은 카메라를 닫아도 유지된다.** 강제 종료(작업 관리자, `taskkill /F`)하면 복구되지 않아 다른 프로그램과 Unity 웹캠 모드에도 영향을 준다 (2026-11-08 측정: 강제 종료 뒤 밝기 81·노출 -7 유지, 복구 뒤 125~128·노출 -5). 이때는 `run_hand_sensor.bat --exposure auto --duration 3` 실행. 런처의 `stop_exhibition.bat`은 강제 종료했을 때 이것을 자동으로 실행한다

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
