# 53. 세션 R3 — Pose Character

- 담당자: **R3 Pose Character**
- 브랜치: `feature/roadmap-pose-character`
- 담당: 관절 스트림(9002) 수신, 화면 우측 상단 미니 캐릭터
- 수정 가능: `Assets/02_Scripts/Network/Pose/**`, `Assets/02_Scripts/UI/MiniCharacter/**`, 각 모듈의 `Tests/`·`Dev/` 하위 같은 이름 폴더, `Assets/03_Prefabs/UI/MiniCharacter.prefab`, 이 문서
- 선행 조건: R2 `main` 병합
- 사용자 결정: **네온 관절 인형** (관절은 빛나는 점, 뼈는 선)
- 아트 기준: `Assets/04_Docs/eye_of_the_future_art_reference.png` (시안 네온 홀로그램)

## 1. 목표

플레이어 영상 대신 작은 캐릭터로 플레이어의 실제 동작을 보여준다. 관람객이 "내 몸이 인식되고 있다"를 바로 알 수 있게 한다.

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
- 표시 단계: 미디어 월과 전투 모두 표시. 전투 HUD(보스 HP 상단 중앙, 플레이어 HP 우측 하단, 콤보 우측 하단)와 겹치지 않을 것
- 그리기: UI Image 점과 회전한 Image 선, 또는 하나의 Mesh. 프레임 할당 0
- 수신기는 `AppConfig.udpPosePort`가 0이면 열지 않음

## 4. 체크리스트

### 구현 — 담당: R3 Pose Character
- [ ] `UdpPoseReceiver` (magic, 크기 검사, count 0, seq 역행 버림, 통계)
- [ ] `PoseSmoother`
- [ ] `MiniCharacterView`, 프리팹, 페이드
- [ ] Dev 씬: `fake_sender.py` 관절 패턴과 실제 손 센서 모두로 확인

### 자동 검증 — 담당: R3 Pose Character
- [ ] EditMode: 패킷 파싱(정상, 잘린 패킷, 잘못된 magic, count 0), 평활, visibility 처리
- [ ] 기존 EditMode 전체 통과
- [ ] 수신·표시 프레임 할당 0
- [ ] 1920×1080 스크린샷: 전투 HUD와 겹침 없음

### 사람 검증 — 담당: 사용자
- [ ] 팔을 들고, 흔들고, 쪼그려 앉으면 캐릭터가 같은 동작을 함 (좌우 방향 맞음)
- [ ] 화면 밖으로 나가면 캐릭터가 사라지고, 돌아오면 나타남
- [ ] 크기와 위치가 전투를 방해하지 않음

## 5. 통합 인계

기획 세션 작업: Main에 수신기(`NetworkPose`)와 `MiniCharacter` 배치, `AppBootstrap`에서 external일 때만 수신기 초기화, F1 패널에 관절 스트림 수신·버림 수 표시 연결.

## 6. 제외 범위

전투 아바타 포즈(R4), 3D 캐릭터 모델, 손가락

## 작업 로그
- (R3 Pose Character가 기록)
