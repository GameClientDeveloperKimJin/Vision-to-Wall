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
- [ ] `PlayerInputTracker`: 관절 → `guardPose` (판정, 평활, 짧은 깜빡임 무시)
- [ ] `CombatRules`: 자세 시작 시 실드 발동, 기존 흔들기 실드 유지
- [ ] Dev 씬 확인 (`fake_pose_sender.py --pattern raise`, 실제 센서)

### 자동 검증 — 담당: RS Shield Pose
- [ ] EditMode: 자세 시작에만 발동, 들고 있으면 재발동 없음, 쿨다운, visibility 낮으면 무시, 관절 없으면 기존 동작과 동일
- [ ] 기존 EditMode 전체, pytest 전체 통과
- [ ] 프레임 할당 0

### 사람 검증 — 담당: 사용자
- [ ] 조준선이 고정된 뒤 팔을 들면 실드가 켜지고 패링됨
- [ ] 팔을 계속 들고 있으면 실드가 계속 켜지지 않음
- [ ] 빠르게 흔들기 실드도 여전히 됨

## 작업 로그
- (RS Shield Pose가 기록)
