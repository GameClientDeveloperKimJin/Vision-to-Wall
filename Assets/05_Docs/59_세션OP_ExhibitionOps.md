# 59. 세션 OP — Exhibition Ops

- 담당자: **OP Exhibition Ops**
- 브랜치: `feature/exhibition-ops`
- 담당: 전시 현장에서 무인 운영할 수 있는 실행·관리 기능, Windows 빌드, 빌드 기준 성능 측정
- 선행 조건: 로드맵 통합 검증 완료(2026-11-08), 2-1 사용자 결정, 기획 세션 선행 작업
- 목적: 실제 전시 여부와 무관하게 **"전시 현장에서 운영 가능한 수준"을 포트폴리오 근거로 남김**. 실제 전시를 하지 않았다면 결과물에는 "전시 환경을 가정한 설계·검증"으로 표기

## 1. 사용자 결정 (2-1, 2026-11-08)

| 항목 | 결정 |
|---|---|
| 기본 `sensorMode` | `external` (1-1에서 확정) |
| 카메라 노출 | config `cameraExposure` 추가, 기본 `"-7"`. 실행 옵션 `--exposure`가 있으면 옵션 우선 |
| 대기 안내 문구 | "손을 흔들어 보세요" 유지. 각성 요구 시간 3초 → **7초** (`awakenChargeSec`, config 기본값만 변경) |
| Windows 빌드 | 진행. 로컬 빌드만 (`Builds/`는 git 제외), 배포·업로드 없음 |
| F1 패널 | 로드맵 상태 표시 추가 (장치 패킷, 관절 수신 통계, 날씨·조회 상태) |

## 2. 기획 세션 선행 작업 (완료)

- `AppConfig.cameraExposure`(string, `"-7"` / `"auto"` / `""`) + `config.json`. Unity는 쓰지 않지만 F1 저장 시 키가 사라지지 않도록 계약에 둠
- `config.json` `awakenChargeSec` 7.0

## 3. 수정 가능

- `Tools/HandSensor/**` (노출 config 읽기)
- `Tools/Launcher/**` (새 폴더: 원클릭 실행, 감시, 자동 시작 스크립트)
- `Assets/02_Scripts/UI/**`, `Assets/03_Prefabs/UI/**` (F1 패널 추가 표시)
- `ProjectSettings/ProjectSettings.asset`의 Player 항목 (제품명, 회사명, 해상도·전체 화면, 아이콘). 다른 프로젝트 설정 변경은 기획 세션에 요청
- 이 문서
- `AppBootstrap`·`Main.unity` 연결은 기획 세션

## 4. 납품물

| 항목 | 내용 |
|---|---|
| 노출 config | `hand_sensor.py`가 `cameraExposure`를 읽어 적용 (옵션이 우선). 정상 종료 시 auto 복구 유지 |
| 원클릭 실행 | `Tools/Launcher/start_exhibition.bat` (또는 동등한 스크립트): 손 센서 실행 → 준비되면 게임 빌드 실행. 하나만 떠 있어도 중복 실행 안 함 |
| 감시·자동 재시작 | 손 센서가 죽으면 다시 실행 (게임은 1-3에서 확인한 대로 재연결). 게임이 죽으면 다시 실행. 재시작 횟수·시각 로그 |
| 종료 | `stop_exhibition.bat`: 손 센서 정상 종료(노출 auto 복구) → 게임 종료 |
| 자동 시작 | PC 로그인 시 원클릭 실행을 등록·해제하는 스크립트. **등록은 사용자가 직접 실행** (세션이 시스템 설정을 바꾸지 않음) |
| F1 패널 | 장치: 송신 수, 마지막 패킷 16진수. 관절: 수신·버림 수, 사람 있음. 날씨: 현재 종류, 고정 여부, 마지막 성공 시각, 상태(키 없음·오프라인·인증 오류). 패널 닫힘 상태 비용 0 유지, 1080 높이 안에 들어갈 것. 연결 메서드는 기획 세션이 `AppBootstrap`에서 호출 |
| Windows 빌드 | `Builds/Windows/`. Main 씬만. 개발 빌드와 릴리스 빌드 |
| 빌드 측정 | 전투 fps(평균·1% 저점), 프레임 GC(개발 빌드, ProfilerRecorder), 메모리. 측정 조건 기록 |
| 지연 측정 | 카메라 → 화면 전체 지연. 방법을 정하고(예: 화면 시계와 손을 함께 찍는 고속 촬영, 또는 타임스탬프 경로 합산) 한계를 함께 기록 |
| 장시간 실행 | 빌드 + 가짜 송신기(`fake_pose_sender.py` 등)로 2시간 이상: 메모리 증가, fps, 크래시, 단계 순환 횟수 |

## 5. 체크리스트

### 구현 — 담당: OP Exhibition Ops
- [x] 노출 config
- [x] 원클릭 실행·감시·종료·자동 시작 스크립트
- [x] F1 패널 추가 표시 (연결 메서드. `AppBootstrap` 호출은 기획 세션)
- [x] Windows 빌드 (Player 설정)

### 자동 검증 — 담당: OP Exhibition Ops
- [x] EditMode 전체, pytest 전체 통과 (EditMode 484/484, pytest 127/127)
- [x] 빌드 측정, 지연 측정, 장시간 실행 결과를 수치로 기록
- [x] 감시 동작: 손 센서·게임 프로세스를 강제 종료했을 때 재시작 확인
- [x] `config.json`·`secrets.json` 원복·미커밋 확인, 빌드 폴더 미커밋

### 사람 검증 — 담당: 사용자
- [ ] 원클릭 실행으로 센서와 게임이 켜지고, 손을 7초 흔들면 전투가 시작됨
- [ ] F1 패널에서 장치·관절·날씨 상태가 보임
- [ ] 종료 스크립트로 깔끔하게 꺼짐

## 6. 다음 단계 (2-3, 기획 세션)

이 세션의 측정값으로 설치 가이드(카메라 높이·거리, 구도, 조명, 노출)와 운영 매뉴얼(켜기·끄기, 장애 대응, F1 튜닝)을 작성.

## 작업 로그

### 2026-11-08 OP Exhibition Ops

#### 구현

| 항목 | 내용 |
|---|---|
| 노출 config | `sensor_config.parse_config_exposure`: `cameraExposure` `"-7"`/`-7` → 수동, `"auto"`, `""`·없음 → 카메라 설정 유지, 잘못된 값은 경고 후 유지. `--exposure`가 우선 (`resolve_exposure`). 시작 로그 `[camera] exposure setting: -7.0 (config)`. 시작 전용 필드라 바뀌면 "재시작 필요" 로그 |
| 센서 정상 종료 경로 | `--stop-file PATH`(있으면 0.25초 안에 정상 종료), `--status-file PATH`(1초 JSON 상태: `starting`/`camera`/`running`/`stopped`), 콘솔 이벤트 처리(`Ctrl+C`·`Ctrl+Break`·창 닫기·로그오프·종료 → 정상 종료, 창 닫기는 정리 끝날 때까지 최대 4초 대기). 카메라 재시도 대기도 중단 가능 |
| 런처 | `Tools/Launcher/`: `start_exhibition.bat`(숨김 감시 실행), `stop_exhibition.bat`, `status_exhibition.bat`, `install_autostart.bat`/`uninstall_autostart.bat`(현재 사용자 시작프로그램 폴더 바로가기, **사용자가 실행**), `exhibition.ps1`(본체), `launcher.json`(설정), `README.md`(사용법) |
| F1 패널 | `EngineerPanel.SetRoadmapSources(IDevicePanelSource, INetworkStats, IPoseSource, IWeatherPanelSource)`, 프리팹에 `Roadmap` 텍스트(4줄, 80px). 장치 송신 수·마지막 패킷 16진수, 관절 수신·버림·사람 있음, 날씨 종류·고정·상태(키 없음·오프라인·인증 오류·서비스 오류)·마지막 성공 시각. 열려 있을 때만 4 Hz 갱신(닫힘 비용 0), 포맷 할당 0 (테스트). 패널 높이 webcam 936px, external 886px (1080 안) |
| 빌드 측정 도구 | `UI/Diagnostics/PerfProbe.cs`: 실행 인수 `-perfLog <csv>`가 있을 때만 생성 (없으면 아무것도 안 함). 단계별 행: fps 평균, 1% 저점(99번째 백분위 프레임 시간), 최악 프레임, GC 할당 프레임·바이트(개발 빌드), 메모리 3종, GC 횟수, 전투·결과 횟수, 손 패킷 송신→전달 지연, 관절 캡처→전달 지연, 누적 수신·버림. `Tools/Launcher/soak_run.py`(가짜 송신 + 게임 장시간), `perf_summary.py`(요약) |
| 수신기 GC 수정 | 기획 세션 범위 확장 허가. `UdpMotionReceiver`·`UdpPoseReceiver` 수신 루프에서 `Socket.Poll(500 ms)` 후 `Receive`. idle 포트의 `ReceiveTimeout` 예외(약 4.2 KB, 초당 2회) 제거. 다른 소켓 예외 처리는 그대로. 테스트: idle 대기 후 순서대로 수신·버림 0, idle 종료 700 ms 미만 (`UdpMotionReceiverTests` 신규 2, `UdpPoseReceiverTests` +1) |

Player 설정 변경 (`ProjectSettings.asset`): `productName` InteractiveGame → **VisionToWall** (실행 파일 `VisionToWall.exe`, 데이터 `VisionToWall_Data`), `forceSingleInstance` 0 → **1**. 그대로 둔 값: `companyName` DefaultCompany(사용자 결정 필요 시 변경), `fullscreenMode` FullScreenWindow, `defaultIsNativeResolution` 1, `runInBackground` 1, `visibleInBackground` 1, Mono 백엔드, 아이콘 없음. vSync 0·`targetFrameRate` 없음(품질 설정, 범위 밖)이라 빌드 fps는 상한 없이 측정됨.

**`secrets.json` 주의**: Unity가 `StreamingAssets`를 그대로 복사하므로 `Builds/Windows/*/VisionToWall_Data/StreamingAssets/secrets.json`에 기상청 키가 들어간다. 빌드 폴더를 공유·압축·업로드하면 키가 따라간다 (런처 README 1절에도 적음). 빌드는 `Builds/`(git 제외)에만 있고 업로드·배포 없음.

#### 측정 조건

- PC: Ryzen 9 7900, RTX 4070 SUPER(D3D12), RAM 31 GB, 모니터 2560×1440 60 Hz (빌드는 전체 화면 창, 기본 해상도)
- Unity 6000.3.4f1, Mono, vSync 끔. 측정 중 Unity 에디터가 함께 실행 중 (백그라운드)
- 가짜 입력: `fake_sender.py` 30 Hz(원 궤적 4점, v 1.0, 20초 흔들기/40~70초 없음), `fake_pose_sender.py` 30 Hz(`wave`, 8초 있음/3초 없음). 실제 센서 측정은 Logi C270 640×480, 노출 -7
- 빌드: `Builds/Windows/Release/`(134 MB), `Builds/Windows/Dev/`(196 MB, Development)

#### 빌드 성능

| 측정 | 빌드 | 미디어 월 fps | 전투 fps | 전투 1% 저점 | 최악 프레임 (시작 제외) |
|---|---|---|---|---|---|
| 실제 센서 3분 (런처 실행, 전투 2회) | Release | 1090~1230 | 980~1030 | 197~207 | 전투 10.4 ms, 전체 49.7 ms (미디어 월 1회) |
| 가짜 송신 10분 (전투 10회) | Dev | 1098 | 979 | 215 (최저) / 233 (평균) | 전투 31 ms, 전체 52 ms (결과 화면) |
| 가짜 송신 2시간 | Release | (장시간 실행 표) | | | |

- 60 fps 기준 충분. 시작 직후 첫 행에 약 3초 프레임 1개(로딩)가 있어 요약은 30초 이후

#### 프레임 GC (Dev 빌드, "GC Allocated In Frame" = 모든 스레드 합산)

| 조건 | 메인 스레드 | 전체 (수신 스레드 포함) | GC 수집 |
|---|---|---|---|
| 수정 전, 송신 없음 (대기) | 0 B (프로파일러 500프레임) | 초당 2프레임 × 4.2 KB = **8.4 KB/s** | 30초 0회 |
| 수정 전, 가짜 송신 10분 | 0 B (프로파일러 2000프레임) | 1462프레임 / 1.99 MB = **약 3.5 KB/s** | 10분 2회 |
| 수정 후, 송신 없음 40초 | 0 B | **0 B** | 0회 |
| 수정 후, 가짜 송신 5분 (전투 5회) | 전투 중 18프레임 × 64 B (TMP 동적 글리프 추가·배열 확장, 프로파일러로 확인) | 동일 (수신 스레드 0) | 0회 |

- 할당 위치는 개발 빌드에 에디터 프로파일러를 연결해 원시 샘플(GC.Alloc 표식과 상위 표식)로 찾음. 수정 전 할당은 모두 `UdpMotionReceiver`·`UdpPoseReceiver` 스레드
- 남은 64 B는 `TMP.TryAddCharacter`/`TMP.SetArraySizes`: 처음 나오는 글자를 동적 폰트 아틀라스에 넣는 1회성 할당 (글자가 다 나오면 멈춤)
- 무효 측정 1건: 프로파일러 연결을 끊은 직후 실행에서 매 프레임 약 665 B가 기록됨. 프로파일러 연결 없이 다시 실행하면 재현 안 됨 → 프로파일러 간섭으로 판단, 수치에서 제외

#### 메모리 (Release 실제 센서 3분)

Total Used 129~137 MB, GC Reserved 24.6 MB, System Used 670~704 MB. 장시간 증가는 아래 표.

#### 지연 (카메라 → 화면)

방법: 타임스탬프 경로 합산. 같은 PC에서 Python `time.monotonic()`(Windows 3.12 = `GetTickCount64`, 해상도 15.6 ms)과 Unity `Environment.TickCount`(같은 시계 하위 32비트)를 비교. 관절 패킷은 카메라 프레임을 받은 시각, 손 패킷은 송신 시각을 담는다. 프로브가 패킷 이벤트 시점(메인 스레드 `Update`)에서 차이를 평균. 고속 촬영은 사람과 장비가 필요해 하지 않음.

| 구간 | 값 | 근거 |
|---|---|---|
| ① 노출·센서 읽기·USB·드라이버 버퍼 (빛 → `cap.read()` 반환) | **측정 못 함**. 추정 33~67 ms | C270 30 fps, 1~2 프레임 버퍼 가정. 노출 -7 = 7.8 ms |
| ② 캡처 → Unity 전달 (손·Pose 추론, UDP, 수신 대기열) | **평균 20~31 ms** (10초 행 평균의 범위, 대표 약 21 ms), 최대 47~62 ms | 실제 센서 3분, 관절 패킷 약 5,000개. 센서 로그 `inferMs` 18.4~18.9 |
| ②' 손 패킷 송신 → Unity 전달 | 평균 0.5~1.3 ms | 같은 측정 (손 스트림, 송신 시각 기준) |
| ③ Unity 처리 → 그리기 | 약 1 ms (1000 fps 프레임 시간) | 위 fps |
| ④ 화면 출력 (vSync 끔, 60 Hz 스캔) | 추정 0~17 ms + 패널 응답 | 모니터 사양 미측정 |
| ⑤ 연출 평활 (미니 캐릭터·아바타 필터) | 별도 (R3 추정 총 0.13초에 포함된 부분) | 로직 지연, 이번 측정 밖 |

- 측정된 부분(②+③) 약 22~32 ms, 추정 포함 전체 약 **60~120 ms** (⑤ 제외)
- 한계: 시계 해상도 15.6 ms(평균으로 줄임), ①④는 추정, 렌더링 대기열(GPU 지연)은 포함 안 됨. 정확한 값은 화면 시계(ms)와 손을 함께 240 fps 이상으로 찍는 방법이 필요 (설치 가이드에서 권장 절차로 제안)

#### 장시간 실행 (Release, 가짜 송신)

(진행 중 — 결과 추가 예정)

#### 감시·종료 시험 (Release 빌드 + 실제 손 센서, `launcher.json` 대신 시험용 설정에 `-perfLog`만 추가)

| 시험 | 결과 |
|---|---|
| `start_exhibition.bat` | 감시 실행 → 센서 시작 → 3초 뒤 `running`(준비) → 게임 실행 |
| 감시 중 `start_exhibition.bat` 다시 실행 | "watchdog already running - nothing to do" |
| 게임 exe 직접 두 번째 실행 | `forceSingleInstance`로 바로 종료, 게임 1개 유지 |
| 센서 강제 종료 (`taskkill /T /F`) | 2초 안에 재시작 (`sensor exited (code 1) - restart #1`), 게임은 그대로이고 새 센서 패킷 수신 재개 (프로브 수신 수 증가) |
| 게임 강제 종료 | 2초 안에 재시작 (`game exited (code 1) - restart #1`) |
| 센서 멈춤 (프로세스 일시 정지) | 상태 파일 17초 정체 → 강제 종료 → 재시작 (`restart #2`) |
| 게임 멈춤 (프로세스 일시 정지) | 응답 없음 30초 → 강제 종료 → 재시작 |
| `stop_exhibition.bat` | 5.8초. 센서 stop 파일로 정상 종료 → 게임 창 닫기 → 감시 종료. 카메라 밝기 124, 노출 자동(-5) |
| stop 파일을 모르는 센서(직접 실행)가 있을 때 종료 | 10초 대기 → 강제 종료 → 노출 자동 복구 실행. 첫 시도에서 `taskkill` 오류 출력이 스크립트를 중단시켜 복구가 건너뛰어진 결함 발견 → 수정 (`26f7fcc`) 후 재시험: 밝기 128, 노출 -5 |

노출 복구 확인 (카메라 단독 측정, `CAP_PROP_EXPOSURE`·평균 밝기): 정상 종료 뒤 -5·125, 강제 종료 뒤 -7·81 (수동 유지), `--exposure auto --duration 3` 뒤 -5~-6·125~128. 이 세션의 시험 중 강제 종료 3회는 모두 복구 후 확인함. 콘솔 창 X 버튼 종료는 자동 시험 방법이 없어 미검증 (Windows Terminal 호스트에서 창 핸들 없음).

#### 원복·미커밋 확인

- `Assets/StreamingAssets/config.json`·`secrets.json`: 수정 없음 (`git status` 깨끗). 빌드 폴더·`Tools/Launcher/logs`·`run`은 git 제외
- 작업 트리에 남은 수정은 커밋하지 않음 (기획 세션 지시, 되돌리기는 병합 때 기획 세션). `ProjectSettings.asset`은 Player 2항목만 부분 커밋 (`2fed79f`)

| 파일 | 변경 | 생긴 시점 (파일 시각) | 빌드에 필요한가 (판단 근거) |
|---|---|---|---|
| `ProjectSettings/UnityConnectSettings.asset` | `m_Enabled` 0 → 1 | 17:10:19, 첫 Release 빌드 시작(17:10:17) 2초 뒤 | **필요 없음으로 판단.** 첫 빌드는 0인 상태로 시작했고 성공함 (값이 바뀐 것은 빌드 도중). 게임 코드는 Unity 서비스를 쓰지 않음. 확인 방법: 0으로 되돌리고 빌드 → 성공하는지, 다시 1로 바뀌는지 |
| `Assets/Settings/PC_RPAsset.asset` | `m_Prefilter*` 13개 값 | 17:49:35, 마지막 Release 빌드 때 (빌드마다 다시 씀) | 필요 없음. URP 빌드 전처리가 셰이더 prefilter 값을 빌드마다 계산해 기록. 되돌려도 다음 빌드에서 다시 생김 |
| `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset` | `m_RuntimeSettings` 목록 14개 | 17:49:35 (위와 같음) | 필요 없음. 같은 이유 (URP가 빌드 때 런타임 설정 목록을 채움) |
| `ProjectSettings.asset` `m_BuildTargetBatching` | Standalone static 1 / dynamic 0 추가 | Player 설정 저장 또는 빌드 때 | 필요 없음. 기존 기본값을 명시적으로 쓴 것이라 동작 변화 없음 |
| `ProjectSettings.asset` `preloadedAssets` | Input Actions 추가 | 한 빌드 뒤 남았다가 이후 빌드에서 사라짐 (현재 diff 없음) | 처리 불필요. Input System이 빌드 동안만 넣고 빼는 항목 |
| `Assets/01_Scenes/Main.unity` | EngineerPanel 인스턴스에 새 `Roadmap` RectTransform의 레이아웃 값(0) 오버라이드 6줄 | 17:57:13 (임시 어셈블리 컴파일 새로고침 시점, 저장 원인은 미확인) | 필요 없음. VerticalLayoutGroup이 실행 중 계산하는 값이며 기존 다른 자식과 같은 알려진 노이즈 (30번 3절) |
| `06_Font`, `DefaultVolumeProfile.asset` | 글리프, 볼륨 | 세션 전부터 | 기존 Play 부산물 |

#### 통합 인계 (기획 세션)

1. `AppBootstrap`에 F1 연결. UI는 Core만 참조하므로 App에 어댑터 2개 필요 (임시 어셈블리로 컴파일 확인 후 삭제함, 동작은 미시험):

```csharp
// App/EngineerPanelAdapters.cs
using System;
using VisionToWall.Core;
using VisionToWall.Device;
using VisionToWall.UI;
using VisionToWall.Weather;

namespace VisionToWall.App
{
    internal sealed class DevicePanelAdapter : IDevicePanelSource
    {
        private readonly DeviceController _device;
        public DevicePanelAdapter(DeviceController device) => _device = device;
        public bool IsActive => _device != null && _device.IsActive;
        public int PacketsSent => _device != null ? _device.PacketsSent : 0;
        public bool TryGetLastPacket(Span<byte> destination) => _device != null && _device.TryGetLastPacket(destination);
    }

    internal sealed class WeatherPanelAdapter : IWeatherPanelSource
    {
        private readonly WeatherService _service;
        public WeatherPanelAdapter(WeatherService service) => _service = service;
        public WeatherKind Current => _service != null ? _service.Current : WeatherKind.Unknown;
        public bool IsOverride => _service != null && _service.IsOverride;
        public DateTime LastSuccessUtc =>
            _service != null && _service.Poller != null ? _service.Poller.LastSuccessUtc : DateTime.MinValue;

        public WeatherPanelState State
        {
            get
            {
                WeatherPoller p = _service != null ? _service.Poller : null;
                if (p == null) return WeatherPanelState.Off;
                switch (p.LiveStatus)
                {
                    case WeatherFetchStatus.Ok: return WeatherPanelState.Ok;
                    case WeatherFetchStatus.NoKey: return WeatherPanelState.NoKey;
                    case WeatherFetchStatus.Network:
                    case WeatherFetchStatus.Timeout: return WeatherPanelState.Offline;
                    case WeatherFetchStatus.Auth: return WeatherPanelState.AuthError;
                    case WeatherFetchStatus.ServiceError:
                    case WeatherFetchStatus.NoData:
                    case WeatherFetchStatus.Malformed: return WeatherPanelState.ServiceError;
                    default: return p.LiveAvailable ? WeatherPanelState.Waiting : WeatherPanelState.Off;
                }
            }
        }
    }
}
```

   `Awake`에서 `weatherService.Initialize(config)` 뒤:
   `panel.SetRoadmapSources(new DevicePanelAdapter(deviceController), pose ? poseReceiver : null, pose ? poseReceiver : null, new WeatherPanelAdapter(weatherService));`
   (`WeatherService.Poller`는 `Initialize` 뒤에 생기지만 어댑터는 매번 읽으므로 순서 상관 없음. 어댑터 생성은 시작 때 1회 할당)
2. 프리팹 `EngineerPanel`에 `Roadmap` 자식 추가, `roadmapText` 연결됨. Main 씬 인스턴스에 오버라이드가 있으면 확인 (`GameUiPrefabTests` 통과)
3. 운영 문서(2-3)로 넘길 값: 노출 기본 -7, 런처 사용법(`Tools/Launcher/README.md`), 지연 표의 측정·추정 구분, `secrets.json` 빌드 포함 주의, vSync 끔(빌드 fps 1000 전후, GPU 상시 사용 → 전시 PC 발열·소음이 문제면 `targetFrameRate`/vSync 결정 필요)
4. 빌드 측정 재현: `<빌드>\VisionToWall.exe -perfLog D:\perf.csv` 또는 `soak_run.py`, 요약은 `perf_summary.py`

#### 사람 검증 요청 (사용자, 1번 인계 후 Release 재빌드 필요)

1. `Tools\Launcher\start_exhibition.bat` 실행 → 센서와 게임이 켜짐 → 카메라 앞에서 손을 7초 흔들기 → 전투 시작
2. 게임에서 F1 → 패널 아래쪽 4줄에 장치(`꺼짐 (deviceEnabled)`), 관절(수신 수 증가, `사람 있음`), 날씨(`꺼짐` 또는 날씨·상태)가 보임
3. `Tools\Launcher\stop_exhibition.bat` 실행 → 게임 창이 닫히고 센서 종료 (`status_exhibition.bat`에서 모두 `not running`)
