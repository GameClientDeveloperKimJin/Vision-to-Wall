# Launcher — 전시 무인 운영 스크립트

손 센서(`Tools/HandSensor`)와 게임 빌드를 함께 켜고, 둘 중 하나가 죽으면 다시 켜고, 깔끔하게 끈다.
Windows PowerShell 5.1 기본 기능만 쓴다 (추가 설치 없음). 시스템 설정(레지스트리, 방화벽, 작업 스케줄러)은 바꾸지 않는다.

## 1. 준비

1. 손 센서 설치 (`Tools/HandSensor/README.md` 1절: `.venv`, 모델)
2. 게임 빌드가 `Builds/Windows/Release/VisionToWall.exe`에 있어야 함. 다른 위치면 `launcher.json`의 `gameExe` 수정
3. 빌드의 `VisionToWall_Data/StreamingAssets/config.json`이 운영 설정이다. 런처는 이 파일을 손 센서에도 `--config`로 넘긴다 (F1에서 저장한 값이 게임과 센서에 같이 적용)
   - `sensorMode`가 `external`이 아니면 손 센서를 켜지 않는다 (게임이 웹캠을 직접 연다)
   - 카메라 노출은 `cameraExposure` (기본 `"-7"`). 손 센서가 시작할 때 적용하고 정상 종료 때 자동 노출로 되돌린다

> **주의: 빌드 폴더에는 `secrets.json`(기상청 API 키)이 들어 있다.** Unity가 `StreamingAssets`를 그대로 복사하기 때문이다. 빌드 폴더를 압축·공유·업로드하면 키가 함께 나간다. 공유할 때는 `VisionToWall_Data/StreamingAssets/secrets.json`을 지우거나 `secrets.example.json` 내용으로 바꿀 것 (날씨는 기본 테마로 동작).

## 2. 사용법

| 파일 | 하는 일 |
|---|---|
| `start_exhibition.bat` | 감시 프로그램을 숨김 창으로 띄우고 바로 끝남. 감시 프로그램이 손 센서 실행 → 카메라가 열리고 첫 프레임 처리(준비) → 게임 실행 |
| `stop_exhibition.bat` | 감시 종료 → 손 센서 정상 종료(노출 자동 복구) → 게임 종료. 감시 없이 손으로 켠 센서·게임도 끔 |
| `status_exhibition.bat` | 감시·센서·게임 상태, 재시작 횟수, 최근 로그 10줄 |
| `install_autostart.bat` | Windows 로그인 때 `start_exhibition.bat` 실행 (현재 사용자 시작프로그램 폴더에 바로가기). **직접 실행할 것** |
| `uninstall_autostart.bat` | 위 바로가기 삭제 |

- 중복 실행 없음: 감시 프로그램은 하나만 뜬다(이름 있는 mutex). 손 센서(`hand_sensor.py`)나 게임이 이미 떠 있으면 새로 띄우지 않고 그 프로세스를 감시한다. 게임은 Player 설정 `forceSingleInstance`로도 하나만 뜬다
- 자동 시작 지연: `install_autostart.bat -DelaySec 30`처럼 로그인 뒤 기다릴 시간 지정 (기본 20초, 카메라·네트워크 준비 대기)
- 전시 중 F1 패널을 열면 커서가 보이고, 닫으면 숨는다

## 3. 감시 규칙

| 상황 | 동작 |
|---|---|
| 손 센서 프로세스 종료 | 바로 다시 실행. 게임은 그대로 (게임이 수신을 이어감, 로드맵 통합 1-3) |
| 손 센서 멈춤 (상태 파일 갱신이 `sensorHangSec` 15초 이상 없음, 실행 중 상태일 때만) | 강제 종료 후 다시 실행 |
| 게임 종료 (크래시 포함) | 다시 실행 |
| 게임 응답 없음 `gameHangSec` 30초 이상 | 강제 종료 후 다시 실행 |
| 5분 안에 5번 재시작 | 60초 쉬었다가 다시 시도 (로그에 `pausing`) |
| 손 센서가 준비 시간(`sensorReadyTimeoutSec` 40초) 안에 준비 안 됨 (카메라 없음 등) | 게임은 그대로 실행 (화면은 대기 상태). 센서는 카메라를 2초마다 다시 찾음 |

손 센서 정상 종료 방법: 감시가 `run/sensor.stop` 파일을 만들면 센서가 0.25초 안에 알아채고 정상 종료한다 (미리보기 `q`와 같음). 10초 안에 안 꺼지면 콘솔 닫기 → 강제 종료 순서이고, 강제 종료했고 노출이 수동이면 `hand_sensor.py --exposure auto --duration 3`을 한 번 실행해 노출을 되돌린다.

## 4. 로그와 파일

| 경로 | 내용 |
|---|---|
| `logs/launcher_YYYYMMDD.log` | 시작·종료·재시작 시각과 이유, 종료 코드, 재시작 횟수 |
| `logs/sensor_YYYYMMDD_HHMMSS.out.log` / `.err.log` | 손 센서 출력 (실행마다 새 파일). `.err.log`는 MediaPipe 내부 로그 |
| `logs/game_YYYYMMDD_HHMMSS.log` | 게임 Player 로그 (`-logFile`) |
| `run/` | 실행 중 상태: `state.json`(pid, 재시작 수), `sensor.status.json`(센서 상태), 정지 요청 파일 |

`logs/`, `run/`은 git 제외. `logKeepDays`(14일)보다 오래된 로그는 감시 시작 때 지운다.

## 5. 설정 (`launcher.json`)

| 키 | 기본값 | 설명 |
|---|---|---|
| `gameExe` | `../../Builds/Windows/Release/VisionToWall.exe` | 이 폴더 기준 상대 경로 또는 절대 경로 |
| `gameArgs` | `[]` | 게임 실행 인수 추가. 예: `["-perfLog", "D:/perf.csv"]` (빌드 측정 로그) |
| `sensorArgs` | `[]` | 손 센서 인수 추가. 예: `["--exposure", "-6"]` (config보다 우선) |
| `sensorReadyTimeoutSec` | 40 | 센서 준비를 기다리는 최대 시간 |
| `sensorHangSec` / `gameHangSec` | 15 / 30 | 멈춤 판정 시간 |
| `checkIntervalSec` | 2 | 감시 주기 |
| `startDelaySec` | 0 | 시작 전 대기 (`-DelaySec`가 우선) |
| `crashLoopCount` / `crashLoopWindowSec` / `crashLoopPauseSec` | 5 / 300 / 60 | 연속 재시작 제한 |
| `stopSensorWaitSec` / `stopGameWaitSec` | 10 / 10 | 정상 종료 대기 시간 |

## 6. 문제 해결

| 증상 | 조치 |
|---|---|
| `start: game build not found` | 빌드 경로 확인 (`launcher.json` `gameExe`) |
| 게임은 뜨는데 손 반응 없음 | `status_exhibition.bat`에서 센서 상태 확인. `sensor_*.out.log`에 `[camera] open failed` 반복이면 카메라 연결·점유 확인 |
| 영상이 계속 어두움 (다른 프로그램 포함) | 수동 노출이 남음 (센서가 강제 종료된 경우). `Tools\HandSensor\run_hand_sensor.bat --exposure auto --duration 3` |
| 재시작이 반복됨 | `launcher_*.log`의 종료 코드와 `game_*.log` / `sensor_*.err.log` 마지막 줄 확인 |
| 스크립트 실행이 막힘 | bat이 `-ExecutionPolicy Bypass`로 이 프로세스에만 허용한다. 시스템 정책은 바꾸지 않음 |
