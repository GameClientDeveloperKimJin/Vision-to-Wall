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
- [x] `CombatRules` 공격 주기, 판정, 실드, 종료 조건
- [x] `BossCombatController`, `CombatEventRaised`
- [x] `BossLookAt`, Pan/Tilt
- [x] `ProjectilePool`, 탄막 비행과 반사 표현
- [x] 임시 조준선 (`LineRenderer`, 붉은색)
- [x] `BossEye.prefab`, `Projectile.prefab`
- [x] `CombatDev.unity`

### 자동 검증 — 담당: G3 Boss Combat
- [x] EditMode: 공격 타이밍, 회피 경계값, 가로세로비 보정, 피격, 패링, 쿨다운, 종료 조건 4종
- [x] EditMode: 동일 프레임에 플레이어·보스 HP 동시 0이 불가능한지 확인
- [x] MCP Play에서 끝까지 진행해 Victory와 GameOver 각 1회, 콘솔 에러 0
- [x] GC 0 B/frame (전투 중): 호출 단위 0. 프레임 전체는 작업 로그 참고

### 사람 검증 — 담당: 사용자
- [ ] 보스가 움직이는 방향을 부드럽게 따라보고, 조준선이 고정된 뒤 옆으로 피하면 피해가 없음
- [ ] 제자리에 있으면 HP가 줄고, 손을 크게 흔들어 실드를 켠 채 맞으면 보스 HP가 줄어듦
- [ ] 60초 동안 **회피와 패링이 실제로 해볼 만한지** 판단 (어려우면 수치 조정 요청)

## 5. 통합 인계

기획 세션 작업: Main에 보스와 전투 컨트롤러 배치, Overlay 카메라 스택 설정, Mock을 `BossCombatController`로 교체.

### Main 연결 방법 (`CombatDev.unity`에서 검증한 구성)
1. `03_Prefabs/Game/BossCombatRig.prefab`을 Main에 배치 (위치 원점 그대로)
2. `MediaWall/WallCamera`
   - Culling Mask에서 `TransparentFX`(레이어 1) 제외 (Dev 씬 값 `-3`). 빠뜨리면 보스가 직교 카메라에도 납작하게 한 번 더 그려짐
   - URP Camera Stack에 `BossCombatRig/OverlayCamera` 추가
3. `AppBootstrap`: `combat.Initialize(config, playerInput)`를 먼저 호출한 뒤 `phaseMachine.Initialize(config, playerInput, combat)`. Mock 제거

| OverlayCamera 설정 | 값 | 이유 |
|---|---|---|
| Render Type | Overlay | 월 쉐이더 유지 |
| 위치 | (0, 0, -10) | WallCamera와 같음 |
| Field of View | 53.13 | z=0에서 화면 높이 10 = WallCamera(Orthographic Size 5)와 일치 |
| Near / Far | 0.1 / 30 | |
| Culling Mask | `TransparentFX`만 (`2`) | `ProjectSettings` 수정 없이 내장 레이어 사용 |
| Render Shadows | 끔 | |

- 레이어: `BossCombatController.Initialize`가 보스, 조준선, 탄막 전체를 레이어 1로 설정하므로 G4가 `Visual`을 교체해도 별도 설정이 필요 없음
- G5: FOV가 맞춰져 있어 z=0 평면에서 월드 좌표가 월과 같음. `SetAbsorb`의 `worldCenter`에 보스 `transform.position`을 그대로 넘기면 됨
- 플레이어 평면은 카메라에서 5 거리 (`playerPlaneDistance`). 보스는 (0, 2.2, 0)

### 동작 참고
- 보스는 전투 밖에서도 보임 (G5: MediaWall에서 눈 감김). 비활성화하지 않고 정면(rest)으로 돌아감
- `Abort()`는 이벤트를 내지 않음. 실드 표현은 `ShieldLowered`만 믿지 말고 `IGamePhaseSource.PhaseChanged`로도 정리할 것 (전투 정상 종료 때는 `ShieldLowered`가 나옴)
- `CombatEvent.position`: 조준·탄막·판정 이벤트는 조준 위치, 실드 이벤트는 플레이어 위치, `BossDefeated`는 보스 화면 위치
- GameOver 전용 이벤트는 없음. `Finished` / `IGamePhaseSource.Result` 사용
- `BossPanDeg`는 **보스 기준**. 화면 오른쪽의 플레이어를 보면 음수 (보스의 왼쪽). `BossTiltDeg`는 위가 양수. v2.1 시리얼에서 부호 확인 필요
- `combatAbandonSec`은 `hasPosition == false` 기준. `PlayerInputFilter`의 1초 유지가 앞에 있어 실제로는 입력이 끊기고 약 11초 후 종료
- webcam 모드에는 몸 스트림이 없어, 가만히 서 있으면 입력 없음으로 판정됨 (external 모드는 몸이 잡히면 유지)
- F1 변경은 다음 틱부터 반영. `bossMaxHp`, `playerMaxHp`만 다음 전투부터

## 6. 제외 범위
모델(G4), 파티클·폭발·사운드(G5), HUD(G6). 조준선과 탄막은 임시 표현까지만

## 작업 로그
- 2026-11-06 구현·자동 검증 완료. EditMode 174/174 (G3 추가 51건, 기존 123건 포함). 추가 납품물: `BossCombatRig.prefab`, `CombatLayers.cs`
- Play (`CombatDev.unity`, 자동 시나리오, 포커스 유지, 콘솔 에러·경고 0)
  - Victory: 전투 시간 16.00초, 패링 5, 피격 0
  - GameOver(피격): 19.50초, 패링 1, 피격 5 (첫 공격은 Victory 시나리오에서 전환하며 패링됨)
  - GameOver(시간 초과): 잔여 0, 도착 17회 (회피 15, 피격 2)
  - GameOver(입력 없음): 10.00초 후, 피격 3
- 판정 시각은 설계값과 일치 (조준 고정 +1.20, 발사 +1.50, 도착 +2.00, 주기 3.50초)
- 전투 경과는 `RemainingSec` 기준. 벽시계로는 일부 값이 더 길게 찍힘 (아래 일시정지, `execute_code` 멈춤은 프레임 상한으로 전투 시간에 반영되지 않음)
- GC (`ProfilerRecorder`, 대조군 `new object()` x600 = 600): 전투 중 `BossCombatController.Update` x600 = 0, x3000 = 0 (이 중 조준 중 1150회, 이벤트 구간 포함). EditMode `Is.Not.AllocatingGCMemory`: `CombatRules` 1000틱(이벤트 포함), `BossLookAt` 100틱 통과
- 프레임 전체 GC는 **판단 불가**: `get_counters`가 컨트롤러 활성·비활성 모두 17265 B / 337회로 동일. 에디터·MCP 할당으로 보이나 값이 전혀 변하지 않아 대조로 쓸 수 없음. 빌드 프로파일 미측정
- 첫 Victory 실행 중 에디터가 일시정지 상태가 됨 (`isPaused = true`, 약 42초). 원인 미확정. `manage_camera screenshot` 약 2초 뒤였고, 스크린샷을 찍지 않은 나머지 실행 3회에서는 재현되지 않음. 판정에는 영향 없음
- Overlay 합성은 Play 스크린샷으로 확인 (월 위에 보스 1개, 이중 렌더 없음). Main 씬에서는 미검증
- 수치 근거 (기본값): 60초에 도착 17회. 이기려면 5회 패링 필요. 고정~도착 0.8초. 16:9에서 회피에 필요한 이동은 화면 폭의 8.4%. 계속 흔들기만 하면 실드 주기 3.0초와 공격 주기 3.5초가 어긋나 패배 (테스트 `Balance_WavingNonstopAtDefaults_DoesNotWin`)
