# 53. 세션 R3 — Pose Character

- 담당자: **R3 Pose Character**
- 브랜치: `feature/roadmap-pose-character`
- 담당: 관절 스트림(9002) 수신, 화면 우측 상단 미니 캐릭터
- 수정 가능: `Assets/02_Scripts/Network/Pose/**`, `Assets/02_Scripts/UI/MiniCharacter/**`, 각 모듈의 `Tests/`·`Dev/` 하위 같은 이름 폴더, `Assets/03_Prefabs/UI/MiniCharacter.prefab`, 이 문서
- 선행 조건: R2 `main` 병합
- 사용자 결정: **네온 관절 인형** (관절은 빛나는 점, 뼈는 선), **단계별 표시** (미디어 월에서만, 2026-11-08)
- 아트 기준: `Assets/04_Docs/eye_of_the_future_art_reference.png` (시안 네온 홀로그램)

## 1. 목표

플레이어 영상 대신 작은 캐릭터로 플레이어의 실제 동작을 보여준다. 관람객이 "내 몸이 인식되고 있다"를 바로 알 수 있게 한다.

역할 분담: 미니 캐릭터는 **대기 중 인식 피드백**(체험 유도), 전투 아바타(R4)는 **전투 중 내 몸**. 전투 중에는 같은 정보를 두 번 보여주지 않도록 미니 캐릭터를 숨긴다.

## 2. 납품물

| 파일 | 역할 |
|---|---|
| `Network/Pose/UdpPoseReceiver.cs` | `IPoseSource` 구현. 기존 `UdpMotionReceiver`와 같은 방식 (수신 스레드, 고정 버퍼, 메인 스레드 반영, 수신·버림 통계 `INetworkStats`) |
| `UI/MiniCharacter/MiniCharacterView.cs` | 화면 좌표 관절 13개 → 점 13개, 뼈 12개. 머리는 코 기준 원 |
| `UI/MiniCharacter/PoseSmoother.cs` | 관절별 지수 평활, visibility 낮은 관절 처리. 순수 로직, 테스트 대상 |
| `03_Prefabs/UI/MiniCharacter.prefab` | 우측 상단 패널 (VARCO 콤보 틀처럼 홀로그램 받침 선택) |

공개 API:

```csharp
// UdpPoseReceiver
public void Initialize(AppConfig config, int port);
// MiniCharacterView
public void Initialize(IPoseSource pose, IGamePhaseSource phase);
```

## 3. 처리 설계

- 뼈: 어깨-어깨, 어깨-팔꿈치-손목(좌우), 어깨-골반(좌우), 골반-골반, 골반-무릎-발목(좌우)
- 몸 크기를 패널에 맞춤: 어깨·골반 중심과 키로 정규화해 플레이어가 멀리 있어도 같은 크기
- visibility 0.5 미만 관절은 마지막 값을 0.3초 유지 후 흐리게, 그 뼈도 흐리게
- count 0 또는 0.5초 이상 미수신이면 캐릭터를 페이드아웃하고 "인식 대기" 표시 없이 빈 받침만
- webcam 모드(관절 스트림 없음)에서는 패널 전체 숨김
- 표시 단계 (사용자 결정):

| 단계 | 미니 캐릭터 |
|---|---|
| MediaWall | 표시 |
| Awaken | `awakenTransitionSec` 동안 페이드아웃 |
| Combat, Result | 숨김 (관절 수신과 평활은 계속해 다시 나타날 때 튀지 않게) |
| Result 후 MediaWall 복귀 | 페이드인 |

- 숨김은 `CanvasGroup` 알파. 숨김 상태에서 그리기 갱신을 멈춰 비용 0
- 미디어 월 단계의 안내 문구("손을 흔들어 보세요", 화면 중앙)와 겹치지 않을 것
- 그리기: UI Image 점과 회전한 Image 선, 또는 하나의 Mesh. 프레임 할당 0
- 수신기는 `AppConfig.udpPosePort`가 0이면 열지 않음

## 4. 체크리스트

### 구현 — 담당: R3 Pose Character
- [x] `UdpPoseReceiver` (magic, 크기 검사, count 0, seq 역행 버림, 통계)
- [x] `PoseSmoother`
- [x] `MiniCharacterView`, 프리팹, 페이드
- [x] Dev 씬: `fake_pose_sender.py` 관절 패턴(wave, raise, jacks)으로 확인. 실제 손 센서는 사람이 있어야 송신되므로 사람 검증에서 확인

### 자동 검증 — 담당: R3 Pose Character
- [x] EditMode: 패킷 파싱(정상, 잘린 패킷, 잘못된 magic, count 0), 평활, visibility 처리
- [x] 기존 EditMode 전체 통과 (295/295, 기존 264 + R3 31)
- [x] 수신·표시 프레임 할당 0
- [x] 1920×1080 스크린샷: 미디어 월 안내 문구와 겹침 없음
- [x] 단계 전환 시 알파: MediaWall 1, Awaken 중 감소, Combat·Result 0, 복귀 후 1

### 사람 검증 — 담당: 사용자
- [ ] 팔을 들고, 흔들고, 쪼그려 앉으면 캐릭터가 같은 동작을 함 (좌우 방향 맞음)
- [ ] 화면 밖으로 나가면 캐릭터가 사라지고, 돌아오면 나타남
- [ ] 대기 화면에서 캐릭터가 눈에 띄고, 각성하면 자연스럽게 사라졌다가 복귀 후 다시 나타남

## 5. 통합 인계

기획 세션 작업: Main에 수신기(`NetworkPose`)와 `MiniCharacter` 배치, `AppBootstrap`에서 external일 때만 수신기 초기화, F1 패널에 관절 스트림 수신·버림 수 표시 연결.

## 6. 제외 범위

전투 아바타 포즈(R4), 3D 캐릭터 모델, 손가락

## 작업 로그

### 2026-11-08 R3 Pose Character — 구현과 자동 검증

파일:

| 파일 | 내용 |
|---|---|
| `Network/Pose/PosePacketSerializer.cs` | 9002 패킷 읽기·쓰기. count는 0 또는 13만 허용, 길이 정확히 일치해야 함. count 0이면 `HasPerson` false, 관절 0 |
| `Network/Pose/UdpPoseReceiver.cs` | `IPoseSource`, `INetworkStats`. 포트 0이면 소켓을 열지 않음. seq 규칙은 `UdpMotionReceiver.IsSeqAccepted` 재사용. pool(8) 부족 시 **가장 오래된 대기 프레임을 버림** (최신 자세 우선) |
| `UI/MiniCharacter/PoseSmoother.cs` | 순수 로직. 몸 단위 정규화(몸통 길이 = 1, 카메라 종횡비 보정), 관절 평활 τ 0.06초, 중심 τ 0.5초, 크기 τ 1초. 발목이 보이면 중심 높이를 발목에 고정(쪼그려 앉으면 몸이 내려감). visibility 0.5 미만: 0.3초 유지 → 0.2로 흐림. 0.5초 미수신 또는 count 0 → 0.3초 페이드아웃, 다음 등장은 날아오지 않고 바로 자리 잡음 |
| `UI/MiniCharacter/MiniCharacterFade.cs` | 단계별 알파 (표시 단계 표 그대로). MediaWall 페이드인 0.6초 |
| `UI/MiniCharacter/NeonFigureGraphic.cs` | UI 메시 하나(draw call 1). 뼈 12개 선, 관절 13개 점, 머리는 코 중심 링. 바깥 시안 글로우 + 안쪽 밝은 코어 2겹. 글로우 텍스처는 실행 중 1회 생성 |
| `UI/MiniCharacter/MiniCharacterView.cs` | `Initialize(pose, phase)`와 `Initialize(pose, phase, config)`. 알파 0이면 Canvas 꺼서 그리기 비용 0, 평활은 계속 |
| `03_Prefabs/UI/MiniCharacter.prefab` | Canvas(Overlay, sortingOrder 40), 우측 상단 240×300 패널, 오프셋 (-36, -36), 받침 `09_UI/ComboBase.png` |
| `UI/Dev/MiniCharacter/` | Dev 씬 `MiniCharacterDev.unity`, 부트스트랩, 빌더(`VisionToWall/UI/Build MiniCharacter Prefab`, `.../Build MiniCharacterDev Scene`) |
| Tests | `Network/Tests/EditMode/Pose/` 13개 (파싱 9, 실제 소켓 루프백 4), `UI/Tests/EditMode/MiniCharacter/` 18개 |

검증 수치:
- EditMode **295/295** 통과
- 프레임 할당 (ProfilerRecorder `GC Allocation In Frame Count`, 600회 호출, 고정 오버헤드 2): `TryRead` 2, 수신 dispatch(`CopyFrom` + 이벤트 + `Push`) 2, `MiniCharacterView.Update` 0, 메시 재구성 0 → **모두 0**. 기준 `new object()` ×600 = 600
- 단계 알파 (Dev 씬, 실측): MediaWall 1.00 → Awaken 0.5초마다 0.75 / 0.50 / 0.25 / 0.00 (2.0초, `awakenTransitionSec` 2) → Combat 0 → Result 0 → MediaWall 0.3초 0.50, 0.6초 1.00
- 인원 없음: `fake_pose_sender.py` 기본 주기(8초 있음, 3초 없음)에서 count 0 수신 후 figure 0, 재등장 0.5초 안에 1
- 겹침 (Main, 1920×1080, 안내 문구 강제 표시): 문구 화면 사각형 (583,498)–(1344,582), 패널 (1644,744)–(1884,1044). 가로 300 px 떨어짐
- 수신 drop: 시작 직후 Play 진입·`execute_code` 정지로 pool drop 72 (Dev 씬 1회차). 이후 구간과 Main 구간 drop 0
- 스크린샷: `Temp/r3_main_1080_text.png` (Main + 안내 문구), `Temp/r3_dev_wave.png`, `Temp/r3_dev_jacks_a.png`, `Temp/r3_dev_jacks_b.png` (Dev, 첫 두 장은 글로우 버그 수정 전)

발견과 처리:
- `Mathf.SmoothStep(a, b, t)`는 GLSL smoothstep이 아니라 a→b 보간이라 글로우 텍스처 가장자리 알파가 0.27로 남아 점이 네모로 보였음. `SmoothStep01`로 교체, 테스트 추가
- 머리 링을 짧은 선 20개로 그리면 겹친 끝이 눈금처럼 보임 → 정점을 공유하는 고리 띠로 교체

통합 인계 (기획 세션):
1. Main 배치: 빈 오브젝트 `NetworkPose`에 `UdpPoseReceiver`, 프리팹 `03_Prefabs/UI/MiniCharacter.prefab` 인스턴스
2. `AppBootstrap` 순서: `ConfigService.Load()` 뒤, `config.UseExternalSensor && config.udpPosePort != 0`일 때만 `receiver.Initialize(config)` (포트는 `udpPosePort`). 그다음 `GamePhaseMachine`이 준비된 뒤 `miniCharacter.Initialize(receiver, gamePhaseMachine, config)`. webcam 모드이거나 포트 0이면 `miniCharacter.Initialize(null, gamePhaseMachine, config)` → 패널 계속 숨김. 수신기를 R4 아바타와 공유할 것 (포트 하나에 수신자 하나)
3. F1 통계: `UdpPoseReceiver`는 `INetworkStats` (`PacketsReceived`, `PacketsDropped`, `LastSeq`, `LastPacketTime`). 추가로 `EmptyPacketsReceived`(count 0), `DroppedPoolExhausted`, `DroppedInvalid`, `DroppedSeq`, `DroppedSocket`, `IsRunning`
4. 프리팹을 빌더로 재생성해도 GUID는 유지되지만 씬 인스턴스의 오버라이드는 확인할 것
5. 9002 패킷은 사람이 처음 잡히기 전에는 오지 않음 → 그 동안 받침만 보임 (정상)

### 2026-11-08 사람 검증 1차 — 인식 안 됨 → 월드 좌표로 전환

- 증상: 실제 손 센서로 시험하자 캐릭터가 동작을 따라 하지 않음 (사용자: "인식이 아예 안되네")
- 원인: 앉아서 시험하면 골반·무릎·발목이 카메라 밖. 화면 좌표는 0~1 clamp라 가장자리(y 0)에 붙고 visibility 0.1. 평활기가 **화면 좌표** 기준으로 어깨+골반 4개가 모두 보여야 캐릭터를 세우므로 `settled=False`, 잠깐 세워져도 가장자리 값으로 크기·중심이 틀어짐 (몸 단위 x 10.6 등)
- 수정: `PoseSmoother`가 **월드 좌표** (미터, 골반 중심, 미러 적용, z 무시)를 사용. MediaPipe가 화면 밖 관절도 추정하므로 상체만 보여도 비율이 맞는 캐릭터가 나옴. 세우는 조건은 양 어깨 visibility 0.5 이상 + 몸통 0.1~1 m. 보이지 않는 관절은 0.3초 유지 후 흐려지고 추정값을 천천히(τ 0.3초) 따라감. 카메라 종횡비 보정은 필요 없어져 삭제
- 3절 "화면 좌표 관절 13개 → 점 13개"에서 바뀐 부분. 미러 방향은 같음 (실측: 왼어깨 화면 x 0.24, 월드 x -0.11)
- 확인: EditMode 296/296, 실제 센서 상체만 보이는 상태에서 `settled=True`, 머리·어깨·골반 위치 정상 (`Temp/r3_live_world.png`)
- 앉은 자세는 정면 투영상 다리가 접혀 짧게 보임 (정상). 전시 상황처럼 서서 전신이 보이면 발이 받침에 붙음

### 2026-11-08 사람 없이 실제 포즈로 시험 (녹화 재생)

사용자 요청: 직접 포즈를 취하지 않고 MediaPipe PoseLandmarker 결과로 시험.
- `UI/MiniCharacter/PoseRecording.cs`: 손 센서 녹화 CSV(`--preview` Enter, R2 형식) 파서
- 테스트 데이터 `UI/Tests/EditMode/MiniCharacter/Data/pose_arms_lr.csv`: R2 사람 검증 녹화(관절 값만, 영상 없음). 19.2초, 568패킷. 앉은 자세로 오른팔 약 5~10초, 왼팔 약 11~13.5초
- `PoseReplayTests` (EditMode): 녹화를 60 fps로 재생해 판정. 캐릭터 표시 90% 이상, 오른팔 구간(6.5~9.5초)·왼팔 구간(11.6~12.5초) 각각 90% 이상의 프레임에서 손목이 어깨보다 0.5 몸통 위, 미러 방향 쪽(+x 오른쪽), 반대 손목보다 높음, 밝게 표시 → 통과
- `UI/Dev/MiniCharacter/PoseReplaySender.cs`: Dev 씬 `PoseReplay` 오브젝트(기본 꺼짐)를 켜면 녹화를 원래 속도로 9002에 재생. **손 센서와 동시 사용 금지** (seq가 큰 센서 쪽만 받아들여짐. 실제로 겪음: seq drop 1775)
- 화면 확인: `Temp/r3_replay_right.png`(8초, 오른팔 화면 오른쪽), `Temp/r3_replay_left.png`(12초, 왼팔 화면 왼쪽). 이 Play에서만 재생과 수신을 9102로 바꿔 찍음
- EditMode 299/299
- 남은 일: 쪼그려 앉기·벌려 뛰기·화면 밖 나가기 같은 동작은 녹화가 없음 → 아래 "합성 영상 자동 검증"으로 진행.

### 2026-11-08 사용자 결정: 사람 포즈 검증을 합성 영상 자동 검증으로 대체 (방식 A)

플레이어 약속 (검증 기준, 사용자 확인 "의도 맞음"):
1. 서면 바로 나타난다
2. 같은 쪽으로 따라 한다 (거울 방향)
3. 늦지 않다 (동작 시작 → 인형 반응 0.2~0.3초 이내 목표, 아직 측정 안 함)
4. 떨리거나 튀지 않는다
5. 떠나면 사라지고, 몸 일부만 보여도 무너지지 않는다
6. 전투를 방해하지 않는다 (각성부터 사라짐, 자동 확인 완료)

방식: Unity 휴머노이드 + 동작 클립 → 640×480 정면 영상 + 정답 관절 CSV → PoseLandmarker(손 센서와 같은 처리) → 검출 CSV → 정답 비교 + `PoseSmoother` 재생 비교. 결과는 클립별 PASS 표(`Temp/pose_check/report.md`)와 비교 이미지. "눈에 띄는가"는 비교 이미지로 사용자가 판단. 모델 출처·폴더·소유권·병합 시점은 기획 세션 결정 대기.
- 영상 → CSV 도구는 `Tools/HandSensor`(R3 범위 밖)에 들어가므로 기획 세션 결정 필요

### 2026-11-08 기획 세션 회신 (사용자 확정)

- 합성 영상 자동 검증은 R3에서 제외. R4 직전 별도 세션에서 설계 (R4 3D 뼈 회전 검증용), 모델 출처도 그때 결정. R3에서 만든 합성 검증 파일 없음 (`pose_from_media.py`, 테스트 휴머노이드, 렌더러 모두 미작성)
- 위 "플레이어 약속" 6개는 R4 합성 검증 세션의 기준으로 사용
- R3는 짧은 사람 검증 후 병합

지연·떨림 (최종 통합 항목으로 넘김):

| 항목 | 수치 | 상태 |
|---|---|---|
| 캡처 → Unity 수신 | 평균 26 ms, 최대 47 ms (R2 `pose_monitor.py` 실측, 52번 문서) | 측정 |
| Unity 평활 | 갑작스러운 자세 변화의 80%를 0.1초 안에 따라감 (`StepResponse_Reaches80PercentWithin100ms`) | 측정 |
| 합계 (카메라 노출·화면 출력 제외) | 약 0.13초 → 목표 0.2~0.3초 이내 | 부분 측정 |
| 동작 → 화면 전체 지연 | 카메라 노출, 렌더 프레임 포함 | 미측정 |
| 떨림 (실제 녹화, 앉은 자세에서 어깨·골반·코) | 원본 패킷당 0.032 몸통 → 평활 후 0.010 (약 1/3). 패널에서 약 2.1 px → 0.7 px, 한 프레임 최대 7 px (팔 들 때 몸 움직임 포함) | 측정 |
