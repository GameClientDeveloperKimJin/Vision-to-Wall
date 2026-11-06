# 33. 세션 G2 — Game Flow

- 담당자: **G2 Game Flow**
- 브랜치: `feature/game-flow`
- 담당: 요구사항 4절 Phase 1~4 상태 전환
- 수정 가능: `Assets/02_Scripts/Game/Flow/**`, `Assets/02_Scripts/Game/Tests/Flow/**`, `Assets/02_Scripts/Game/Dev/Flow/**`, 이 문서
- 선행 조건: G1 `main` 병합

## 1. 목표

단일 씬에서 미디어 월 → 각성 → 전투 → 결과 → 미디어 월이 끊김 없이 순환하게 한다. 이 세션은 전환 로직만 만들고, 전투는 Mock, 연출은 G5가 맡는다.

## 2. 납품물

| 파일 | 역할 |
|---|---|
| `Flow/GamePhaseMachine.cs` | MonoBehaviour. `IGamePhaseSource` 구현 |
| `Flow/AwakenGauge.cs` | 순수 C#. 충전, 유예, 감쇠 (테스트 대상) |
| `Flow/ExhibitionPhaseGate.cs` | `IExhibitionStateSource` 데코레이터 |
| `Flow/MockCombatSession.cs` | G3 전까지 쓰는 `ICombatSession` 구현 |
| `Dev/Flow/GameFlowDev.unity` | 단계, 게이지 표시 테스트 씬 |

공개 API:

```csharp
public void Initialize(AppConfig config, IPlayerInputSource input, ICombatSession combat);
```

## 3. 상태 전환

| 현재 | 조건 | 다음 |
|---|---|---|
| MediaWall | 각성 게이지 1 도달 | Awaken |
| Awaken | `awakenTransitionSec` 경과 | Combat (`combat.Begin()`) |
| Combat | `combat.Finished(result)` | Result |
| Result | `resultDurationSec` 경과 | MediaWall (게이지 0, `rearmCooldownSec` 시작) |

### 각성 게이지
- `motion >= awakenMinMotion`이면 `dt / awakenChargeSec`만큼 충전
- 조건이 끊겨도 `awakenGraceSec` 동안 유지, 이후 충전 속도와 같은 속도로 감쇠
- 쿨다운 중에는 충전하지 않음
- 시간은 `Time.unscaledDeltaTime`

### ExhibitionPhaseGate
현재 `AttractModeView`와 `MediaWallController`는 `IExhibitionStateSource`를 본다. 그대로 두면 전투 중 5초 입력이 없을 때 안내 문구가 나타난다.

- MediaWall 단계: 내부 `ExhibitionStateMachine` 값을 그대로 전달
- 그 외 단계: `Active`로 고정
- 값이 실제로 바뀔 때만 `StateChanged` 발생
- Render 코드는 수정하지 않는다. `wall.Initialize(receiver, gate)`로 바꿔 끼운다

### 예외 처리
- 비활성화·종료 시 `combat.Abort()` 호출
- `Finished`가 Combat 단계 밖에서 오면 무시

## 4. 체크리스트

### 구현 — 담당: G2 Game Flow
- [ ] `AwakenGauge`
- [ ] `GamePhaseMachine`, `PhaseChanged`, `PhaseElapsedSec`
- [ ] `ExhibitionPhaseGate`
- [ ] `MockCombatSession` (10초 후 Victory, 설정으로 GameOver 가능)
- [ ] `GameFlowDev.unity`

### 자동 검증 — 담당: G2 Game Flow
- [ ] EditMode: 3초 충전, 유예 안 끊김, 유예 후 감쇠, 쿨다운 충전 차단
- [ ] EditMode: 단계 순환, 잘못된 시점의 `Finished` 무시, 게이트 값·이벤트
- [ ] MCP Play에서 `fake_sender.py`로 전체 순환 1회 + 콘솔 에러 0
- [ ] GC 0 B/frame (전환 프레임 제외)

### 사람 검증 — 담당: 사용자
- [ ] 약 3초 계속 손을 흔들면 각성 단계로 넘어가고, 도중에 멈추면 게이지가 줄어듦
- [ ] 결과 단계가 끝나면 미디어 월로 돌아오고, 5초 후 다시 충전됨
- [ ] 전투 단계에서 가만히 있어도 "손을 흔들어 보세요"가 나타나지 않음

## 5. 통합 인계

기획 세션 작업: Main에 `GamePhaseMachine` 배치, `wall`/`panel`에 게이트 전달, G3 전까지 Mock 연결.

## 6. 제외 범위
색 전환, 파티클 흡수, 사운드(G5), HP 바(G6), 전투 판정(G3)

## 작업 로그
- (G2 Game Flow가 기록)
