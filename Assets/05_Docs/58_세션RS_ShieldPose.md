# 58. 세션 RS — Shield Pose

- 담당자: **RS Shield Pose**
- 브랜치: `feature/roadmap-shield-pose`
- 담당: 팔을 들면 실드가 켜지는 규칙 추가 (빠르게 흔들기 실드는 유지)
- 수정 가능: `Assets/02_Scripts/Game/Input/**`, `Assets/02_Scripts/Game/Combat/CombatRules.cs`, 각 `Tests/`·`Dev/` 하위 Input·Combat 폴더, 이 문서
- 선행 조건: R4b `main` 병합, 기획 세션 선행 작업
- 사용자 결정 (2026-11-08): **A안**. R4 사람 검증 중 "레이저 타이밍에 맞게 팔을 들었는데 패링이 안 된다" 보고. 아바타가 팔 동작을 따라 하므로 팔을 들어 막는 것이 자연스러운 행동이 됨 (레퍼런스 이미지도 팔을 뻗어 막는 모습)

## 1. 규칙

| 항목 | 내용 |
|---|---|
| 실드 조건 | 기존: 움직임 강도 ≥ `shieldThreshold`. **추가: 팔 들기 자세가 새로 시작됨** (어느 쪽 팔이든) |
| 팔 들기 자세 | 손목이 같은 쪽 어깨보다 위 (화면 y 기준, 2D. RT에서 2D 정확도 1~9° 확인), 손목 visibility 0.5 이상 |
| 발동 시점 | 자세가 **시작되는 순간**에만 발동 (들고 있기만 하면 계속 켜지지 않게). 내렸다 다시 들어야 다음 발동 |
| 지속·쿨다운 | 기존 `shieldDurationSec`, `shieldCooldownSec` 그대로 |
| 모드 | 관절 스트림이 있을 때만 (external). webcam 모드는 지금처럼 흔들기만 |

## 2. 기획 세션 선행 작업

- `Core/GameContracts.cs` `PlayerSample`에 `bool guardPose` 추가 (팔 들기 자세 여부)
- config `shieldPoseEnabled` (기본 true)
- `PlayerInputTracker`가 `IPoseSource`를 받도록 `Initialize`에 선택 인자 추가하는 것은 RS가 구현, 호출은 기획 세션

## 3. 체크리스트

### 구현 — 담당: RS Shield Pose
- [x] `PlayerInputTracker`: 관절 → `guardPose` (판정, 평활, 짧은 깜빡임 무시)
- [x] `CombatRules`: 자세 시작 시 실드 발동, 기존 흔들기 실드 유지
- [x] Dev 씬 확인 (`fake_pose_sender.py --pattern raise`). 실제 센서는 사람 검증에서

### 자동 검증 — 담당: RS Shield Pose
- [x] EditMode: 자세 시작에만 발동, 들고 있으면 재발동 없음, 쿨다운, visibility 낮으면 무시, 관절 없으면 기존 동작과 동일
- [x] 기존 EditMode 전체, pytest 전체 통과
- [x] 프레임 할당 0

### 사람 검증 — 담당: 사용자
- [ ] 조준선이 고정된 뒤 팔을 들면 실드가 켜지고 패링됨
- [ ] 팔을 계속 들고 있으면 실드가 계속 켜지지 않음
- [ ] 빠르게 흔들기 실드도 여전히 됨

## 작업 로그
### 2026-11-08 RS Shield Pose — 구현

**판정 (`PlayerInputFilter.AddPoseFrame`)**

| 항목 | 값 | 근거 |
|---|---|---|
| 팔 들림 | 손목 y > 같은 쪽 어깨 y (화면, 원점 왼쪽 아래), 손목 visibility ≥ 0.5 | 1절 규칙 그대로. 어깨 visibility는 보지 않음 (설치 요건상 가슴은 항상 화면 안) |
| 히스테리시스 | 들린 팔은 손목이 어깨보다 0.03 아래로 내려가야 내려감 | 어깨선 근처 떨림으로 켜졌다 꺼졌다 하며 재발동되는 것 방지. 평활(위치 필터) 대신 이걸로: 평활은 반응을 늦춰 패링 타이밍을 깎음 |
| 켜짐 | 들린 프레임 2개 연속 (30 Hz에서 약 33 ms) | 한 프레임짜리 오검출로 실드가 낭비되는 것 방지. 지연은 체감 불가 수준 |
| 꺼짐 | 마지막 들린 프레임 후 0.15초 | 짧은 visibility 깜빡임·관절 튐(1~4프레임)에 꺼졌다 다시 켜지며 재발동되는 것 방지 |
| 즉시 꺼짐 | count 0 패킷(사람 없음), 스트림 끊김은 0.15초 뒤 | 끊긴 센서는 count 0을 안 보내므로 Tick에서 시간으로 만료 |
| 양팔 | 어느 쪽이든 들리면 `guardPose` true (OR) | 계약이 bool 하나. 한쪽을 든 채 다른 쪽으로 바꿔 들면 계속 든 것으로 봄 → 재발동 없음 (팔 교대로 실드 연타 방지) |

**발동 (`CombatRules.TickShield`)**: `guardPose`가 false→true가 되는 틱이 "자세 시작". 흔들기(`motion ≥ shieldThreshold`)와 OR. 전투 시작(`Begin`) 때 이미 들고 있던 팔은 시작으로 치지 않음. `shieldPoseEnabled`는 매 틱 읽음 (F1 즉시 반영), 꺼진 동안 든 팔은 켜도 발동하지 않음. 지속·쿨다운은 기존 값 그대로.

**결정: 쿨다운 중 팔 들기 → 팔을 들고 있는 동안 기억했다가 쿨다운이 끝나는 순간 발동. 내리면 버림.**
- 근거 1 (보고된 불만 그대로): "레이저 타이밍에 맞게 팔을 들었는데 패링이 안 된다". 버리면 사용자는 제대로 들었는데 아무 일도 안 일어나고, 들고만 있어서는 재발동도 안 되니 내렸다 다시 들어야 함 — 패링 타이밍에 그 동작은 불가능.
- 근거 2 (타이밍 계산): 기본값 조준 1.5초(고정 0.3초) + 비행 0.5초 + 회복 1.5초 = 공격 주기 3.5초, 실드 1.5초 + 쿨다운 1.5초 = 3초. 고정 순간(도착 0.8초 전)에 팔을 들었는데 쿨다운이 0.8초 안에 끝나면, 늦게 켜진 실드도 1.5초 동안 유지되므로 도착을 덮음 → 패링 성공. 쿨다운이 더 남았으면 어차피 이번 레이저는 막을 수 없으니 기억해도 손해 없음.
- 짧은 고정 버퍼(예: 0.3초) 대신 "들고 있는 동안": 사람은 레이저를 기다리며 팔을 들고 있음. 0.3초 버퍼는 쿨다운 끝 직전에 든 경우만 구함. 들기 한 번에 실드 한 번은 그대로라 "들고 있으면 계속 켜짐"은 생기지 않음.
- 실드가 이미 켜진 동안 든 팔은 그 실드로 소모 (기억 안 함). 이미 막고 있는데 들고 있다가 3초 뒤 다음 레이저 때 저절로 켜지면 "들고만 있어도 켜짐"과 같아짐.
- 짧게 들었다 쿨다운 중에 내린 경우는 버림. 의도 없는 팔 움직임이 나중에 실드를 쓰는 것 방지.

**webcam (관절 없음)**: `IPoseSource` 없으면 `guardPose`는 항상 false → `CombatRules`는 기존과 같은 경로 (`Guard_NoPoseStream_StaysFalse`, 기존 흔들기 테스트 전부 그대로 통과).

**시험**
- EditMode 386/386 통과 (새 21개: 필터 11, 전투 10). 할당 0: `Guard_AddAndTick_AllocateNothing`, `Rules_FullCycles_AllocateNothing`(guardPose 토글 추가)
- pytest 111 통과 (Tools/HandSensor, 변경 없음)
- CombatDev Play + `fake_pose_sender.py --pattern raise --on 4 --off 2`: 사람 등장마다 guard True → 1프레임 안에 실드, 4초 들고 있어도 실드 1회 (1.5초 뒤 꺼지고 재발동 없음), 실드가 도착과 겹친 회차 Parried. 오른팔→왼팔 교대(프레임 하나에 바뀜)는 계속 든 것으로 처리됨 확인
- 참고: 시험 중 사용자 쪽 실제 센서(`hand_sensor.py --exposure -7 --preview`)가 9002를 쓰고 있어서 가짜 송신은 9012로 보내고 Dev 수신기를 그 포트로 바꿔 확인 (센서는 건드리지 않음)

**Dev 씬**: `CombatDev`·`PlayerInputDev`에 `UdpPoseReceiver`(udpPosePort) 추가, 오버레이에 `guard` 표시. CombatDev 키보드: **G 누르는 동안 팔 들기**. 둘 다 `forceExternal`이라 config 변경 없이 실제 센서로 사람 검증 가능.

**통합 인계 (기획 세션)**: `PlayerInputTracker.Initialize(config, motion, bodyOrNull, poseOrNull)` 오버로드 추가, 기존 3인자는 그대로 (null 전달과 같음). `AppBootstrap` 수정안: 
```csharp
bool pose = external && config.udpPosePort != 0;
if (pose) poseReceiver.Initialize(config);
playerInput.Initialize(config, receiver, external ? bodyReceiver : null, pose ? poseReceiver : null);
```
(지금은 `pose` 계산이 `playerInput.Initialize` 뒤에 있으므로 두 줄을 앞으로 옮겨야 함. 수신기 하나를 아바타·미니캐릭터와 공유, 구독자 추가만)

**사람 검증 (CombatDev 씬, 센서 `run_hand_sensor.bat --exposure -7 --preview`, 서서 가슴·양 팔꿈치가 화면 안)**
1. 조준선이 굵어진(고정) 뒤 한쪽 팔을 머리 위로 → 실드 켜짐(파란 큰 마커) → 레이저 도착 때 parried 증가
2. 팔을 든 채 5초 유지 → 실드 1번 켜지고 1.5초 뒤 꺼진 다음 다시 안 켜짐 → 내렸다 다시 들면 (쿨다운 뒤) 다시 켜짐
3. 팔을 내린 채 손을 빠르게 흔들기 → 실드 켜짐 (기존 흔들기 실드 유지)
