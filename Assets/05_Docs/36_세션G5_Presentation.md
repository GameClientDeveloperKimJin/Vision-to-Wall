# 36. 세션 G5 — Presentation

- 담당자: **G5 Presentation**
- 브랜치: `feature/game-presentation`
- 담당: 요구사항 4절 시각·청각 연출, G-FR-05 파티클 트레일 강화, G-FR-07 실드 파티클
- 수정 가능: `Assets/02_Scripts/Game/Presentation/**`, `Assets/02_Scripts/Game/Tests/Presentation/**`, `Assets/02_Scripts/Game/Dev/Presentation/**`, `Assets/02_Scripts/Render/MediaWallController.cs` (`IWallPresentation` 구현만), `Assets/03_Prefabs/Render/MediaWall.prefab`, `Assets/03_Prefabs/Game/**`, 이 문서
- 선행 조건: G4 `main` 병합 (사운드 미수령이면 슬롯만 구현)

## 1. 목표

판정 로직을 변경하지 않고 단계·전투 이벤트에 연출을 입힌다. **연출은 이벤트를 듣기만 하고 게임 상태를 바꾸지 않는다.**

## 2. 납품물

| 파일 | 역할 |
|---|---|
| `Render/MediaWallController.cs` 수정 | `IWallPresentation` 구현 (색 보간, 흡수 힘) |
| `Presentation/PhasePresenter.cs` | `PhaseChanged` → 테마, 안광, BGM, 슬로우 모션 |
| `Presentation/CombatPresenter.cs` | `CombatEventRaised` → 탄막, 실드, 패링, 폭발 이펙트와 효과음 |
| `Presentation/PlayerTrail.cs` | 플레이어 위치 네온 트레일 |
| `Presentation/AudioDirector.cs` | BGM 페이드, SFX 풀. 클립이 없으면 건너뜀 |

## 3. 단계별 연출

| 단계 | 연출 |
|---|---|
| **MediaWall** | 푸른 파티클과 물결. 보스는 눈 감김, 안광 꺼짐. 게이지 상승에 비례해 `ParticleSystemForceField` 인력으로 파티클이 보스 코어로 빨려듦 |
| **Awaken (2초)** | `Color.Lerp`로 푸른색 → Crimson/Magenta, 안광 번쩍임, 플레이어 응시, 각성음, BGM 시작 |
| **Combat** | 붉은 조준선, 탄막 트레일, 실드 시 손 주변 파티클이 둥글게 응집, 패링 시 반사 이펙트와 효과음 |
| **Result: Victory** | 슬로우 모션(`Time.timeScale`), 보스가 네온 파편으로 대폭발 |
| **Result: GameOver** | 보스가 에너지를 거두어들이고 안광이 꺼짐 |
| **복귀** | 5초에 걸쳐 푸른색으로 복귀, BGM 페이드아웃 |

### 주의
- `Time.timeScale`은 Result에서만 변경하고, 단계 종료·`OnDisable`에서 반드시 1로 복원
- `MediaWallController`는 현재 색을 시작 시 1회만 머티리얼에 적용함. 값이 바뀔 때만 다시 적용해 매 프레임 호출을 피할 것
- ForceField를 쓰려면 파티클의 External Forces 모듈을 켜야 함. `MediaWall.prefab` 수정 후 씬 참조가 유지되는지 확인
- 파티클은 `ParticleSystem.Emit` 또는 풀 사용. 프레임당 방출 상한 유지

## 4. 체크리스트

### 구현 — 담당: G5 Presentation
- [ ] `IWallPresentation` 구현, 기존 미디어 월 동작 회귀 없음
- [ ] 파티클 흡수 (ForceField, 게이지 연동)
- [ ] 각성 색 전환, 안광
- [ ] 조준선·탄막·실드·패링 이펙트
- [ ] Victory 폭발 + 슬로우 모션, GameOver 연출, 복귀
- [ ] `AudioDirector` (BGM 페이드 인·아웃, SFX 풀, 클립 누락 허용)

### 자동 검증 — 담당: G5 Presentation
- [ ] 기존·신규 EditMode 테스트 전부 통과
- [ ] 전체 순환 후 `Time.timeScale == 1`, 테마 값이 초기값으로 복귀
- [ ] 전투 중 Play 중단 후 재시작해도 `timeScale`과 색이 정상
- [ ] 전투 중 fps 60 이상, GC 0 B/frame (전환 프레임 제외)

### 사람 검증 — 담당: 사용자
- [ ] 흔드는 동안 파티클이 보스로 빨려 들어가고, 2초 동안 화면이 붉게 각성함
- [ ] 패링과 승리 폭발이 타격감 있게 느껴짐
- [ ] 결과 후 미디어 월로 자연스럽게 복귀함

## 5. 통합 인계

기획 세션 작업: Main에 Presenter 배치, `IWallPresentation`·`IGamePhaseSource`·`ICombatStateSource` 연결.

## 6. 제외 범위
판정 수치 변경(G3), HP 바·결과 문구(G6)

## 작업 로그
- (G5 Presentation이 기록)
