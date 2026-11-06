# 34. 세션 G3 — Boss Combat

- 담당자: **G3 Boss Combat**
- 브랜치: `feature/game-combat`
- 담당: G-FR-05 보스 시선, G-FR-06 공격·회피, G-FR-07 실드·패링
- 수정 가능: `Assets/02_Scripts/Game/Combat/**`, `Assets/02_Scripts/Game/Tests/Combat/**`, `Assets/02_Scripts/Game/Dev/Combat/**`, `Assets/03_Prefabs/Game/**`, 이 문서
- 선행 조건: G2 `main` 병합

## 1. 목표

임시 도형으로 게임 규칙을 완성한다. 이 세션이 끝나면 모델과 연출 없이도 승패가 나야 한다.

## 2. 납품물

| 파일 | 역할 |
|---|---|
| `Combat/CombatRules.cs` | 순수 C#. 시간 진행, 판정, HP (테스트 대상) |
| `Combat/BossCombatController.cs` | `ICombatSession`, `ICombatStateSource` 구현 |
| `Combat/BossLookAt.cs` | `Quaternion.Slerp` 시선 추적, Pan/Tilt 계산 |
| `Combat/ProjectilePool.cs` | 탄막 오브젝트 풀 (런타임 할당 없음) |
| `Combat/WallSpace.cs` | 정규화 좌표 ↔ 월드 좌표 변환 |
| `03_Prefabs/Game/BossEye.prefab` | 임시 구체. 모델은 자식 `Visual` 아래에 둠 (G4가 교체) |
| `03_Prefabs/Game/Projectile.prefab` | 임시 도형. 모델은 자식 `Visual` 아래에 둠 |
| `Dev/Combat/CombatDev.unity` | 위치·실드 키보드 입력 Mock 포함 테스트 씬 |

## 3. 전투 규칙

### 공격 주기
1. **조준**: `aimSec - aimLockSec` 동안 플레이어 위치를 따라감. `AimStarted`
2. **고정**: `aimLockSec` 동안 조준 위치 고정. `AimLocked`
3. **발사**: 고정 위치로 탄막 발사. `Fired`
4. **도착 판정** (`projectileFlightSec` 후)
5. **대기**: `attackRecoverSec` 후 1번으로

### 도착 판정

```
d = 플레이어 위치와 조준 위치의 거리 (x에 가로세로비 적용, 화면 높이 기준)
d > hitRadius                → Dodged
d <= hitRadius, 실드 활성    → Parried, 보스 HP -1, 탄막 반사
d <= hitRadius, 실드 비활성  → PlayerHit, 플레이어 HP -1
```

`hasPosition == false`이면 마지막으로 알려진 위치를 사용한다.

### 실드
- `motion >= shieldThreshold`이고 쿨다운이 끝났으면 발동. `ShieldRaised`
- `shieldDurationSec` 유지 후 해제. `ShieldLowered`, `shieldCooldownSec` 시작

### 종료

| 조건 | 결과 |
|---|---|
| 보스 HP 0 | Victory (`BossDefeated`) |
| 플레이어 HP 0 | GameOver |
| `combatDurationSec` 초과 | GameOver |
| 입력 없음 `combatAbandonSec` 지속 | GameOver |

`Abort()`는 `Finished`를 발생시키지 않고 풀과 상태만 정리한다.

### 보스 시선과 3D 공간
- 플레이어 위치를 깊이 고정 평면으로 변환해 `Quaternion.Slerp`로 응시
- `BossPanDeg`, `BossTiltDeg`는 보스 기본 정면 대비 회전각 (v2.1에서 시리얼 패킷으로 변환)
- 보스와 탄막은 Overlay 원근 카메라 시점 기준. Dev 씬에서 카메라 구성을 검증하고 설정값을 인계 사항에 적음
- 판정은 물리 충돌을 쓰지 않고 정규화 좌표로 계산

### 시간
판정은 `Time.unscaledDeltaTime`으로 진행하되, 프레임당 `GamePhaseMachine.MaxFrameDeltaSec`(0.1초)로 제한한다. 상한이 없으면 Play 진입 프레임 스파이크(3.8초 측정)로 타이머가 건너뛴다.

`Begin()`은 `GamePhaseMachine`이 호출한다. `Finished`는 전투당 한 번만 발생시키고, `Abort()` 후에는 발생시키지 않는다.

## 4. 체크리스트

### 구현 — 담당: G3 Boss Combat
- [ ] `CombatRules` 공격 주기, 판정, 실드, 종료 조건
- [ ] `BossCombatController`, `CombatEventRaised`
- [ ] `BossLookAt`, Pan/Tilt
- [ ] `ProjectilePool`, 탄막 비행과 반사 표현
- [ ] 임시 조준선 (`LineRenderer`, 붉은색)
- [ ] `BossEye.prefab`, `Projectile.prefab`
- [ ] `CombatDev.unity`

### 자동 검증 — 담당: G3 Boss Combat
- [ ] EditMode: 공격 타이밍, 회피 경계값, 가로세로비 보정, 피격, 패링, 쿨다운, 종료 조건 4종
- [ ] EditMode: 동일 프레임에 플레이어·보스 HP 동시 0이 불가능한지 확인
- [ ] MCP Play에서 끝까지 진행해 Victory와 GameOver 각 1회, 콘솔 에러 0
- [ ] GC 0 B/frame (전투 중)

### 사람 검증 — 담당: 사용자
- [ ] 보스가 움직이는 방향을 부드럽게 따라보고, 조준선이 고정된 뒤 옆으로 피하면 피해가 없음
- [ ] 제자리에 있으면 HP가 줄고, 손을 크게 흔들어 실드를 켠 채 맞으면 보스 HP가 줄어듦
- [ ] 60초 동안 **회피와 패링이 실제로 해볼 만한지** 판단 (어려우면 수치 조정 요청)

## 5. 통합 인계

기획 세션 작업: Main에 보스와 전투 컨트롤러 배치, Overlay 카메라 스택 설정, Mock을 `BossCombatController`로 교체.

## 6. 제외 범위
모델(G4), 파티클·폭발·사운드(G5), HUD(G6). 조준선과 탄막은 임시 표현까지만

## 작업 로그
- (G3 Boss Combat이 기록)
