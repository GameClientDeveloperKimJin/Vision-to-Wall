# 36. 세션 G5 — Presentation

- 담당자: **G5 Presentation**
- 브랜치: `feature/game-presentation`
- 담당: 요구사항 4절 시각·청각 연출, G-FR-05 파티클 트레일 강화, G-FR-07 실드 파티클
- 아트 기준: `Assets/04_Docs/eye_of_the_future_art_reference.png`
- 수정 가능: `Assets/02_Scripts/Game/Presentation/**`, `Assets/02_Scripts/Game/Tests/Presentation/**`, `Assets/02_Scripts/Game/Dev/Presentation/**`, `Assets/02_Scripts/Render/MediaWallController.cs` (`IWallPresentation` 구현만), `Assets/03_Prefabs/Render/MediaWall.prefab`, `Assets/03_Prefabs/Game/**`, 이 문서
- 선행 조건: 없음. G4와 교대로 진행 (아래 "G4와 교대" 참고). 사운드 미수령이면 슬롯만 구현

## 1. 목표

판정 로직을 변경하지 않고 단계·전투 이벤트에 연출을 입힌다. **연출은 이벤트를 듣기만 하고 게임 상태를 바꾸지 않는다.**

### 레퍼런스 요소

| 요소 | 구현 방향 |
|---|---|
| 보스 | G4 금속 링 외피 + 정면 홍채 쉐이더 (붉은 동공 테두리, 파란 홍채, 검은 동공). 발광 강도와 눈 감김을 값으로 제어 |
| 플레이어 | G4 아바타의 정점을 시안색 점으로 렌더링, 화면 왼쪽 앞 뒷모습. `IPlayerInputSource` 위치로 이동. 팔다리는 실제 동작을 따르지 않음 |
| 레이저 | 눈에서 뻗는 가늘고 밝은 선. 실제 공격은 붉은색만 사용 |
| 배경 광선 | 시안·보라·붉은 선이 소실점에서 퍼짐. 어둡게, 플레이어를 겨냥하지 않음 |
| 보호막 | 푸른 구체, 잎맥 같은 발광 무늬, 패링 지점에 불꽃. 손 주변 파티클이 모이면서 나타남 |
| 배경 | 검은 공간, 원근 격자 바닥·천장, 양옆 데이터 기둥과 파형. 전투 단계에서만 표시 |
| 양옆 화염 | 선택 사항. 시간이 남거나 fps 여유가 있을 때만 |

### 레퍼런스 적용 시 주의
- **가독성 우선**: 레퍼런스는 광선이 많다. 플레이어가 피해야 할 조준선과 공격이 장식 광선에 묻히면 안 된다
- **판정과 그림 일치**: 아바타의 몸통 폭이 `hitRadius`(0.15)와 대략 맞아야 한다. 레퍼런스처럼 크게 그리면 몸에 맞았는데 회피로 판정되어 보일 수 있다. 판정 값은 바꾸지 않고 크기와 기준점으로 맞춘다
- **보호막 위치**: 현재 입력은 손별 좌표가 아니라 플레이어 위치 하나를 쓴다. 구체 중심을 판정에 쓰는 위치와 일치시킬 것
- **블룸**: 레퍼런스의 발광은 블룸이 필수인데 현재 설정 여부를 먼저 확인. 전역 Volume이나 URP Asset 변경이 필요하면 범위 밖이므로 기획 세션에 요청. fps 60 유지 확인
- 미디어 월 단계는 기존 모습 유지. 레퍼런스는 전투 화면 기준임

### G4와 교대

작업 폴더와 Unity가 하나뿐이다. **폴더 사용 권한은 기획 세션이 지정하며, 지시 없이 브랜치를 바꾸지 않는다.**

| 단계 | G5 | G4 |
|---|---|---|
| 1 | 모델 없이 작업: 쉐이더, 레이저, 보호막, 배경, `Projectile.prefab` 교체, `IWallPresentation`, `AudioDirector` | VARCO 승인과 생성. 다운로드는 프로젝트 밖 임시 폴더 |
| 2 | 커밋 후 폴더 반납 | 임포트, `BossEye.prefab` `Visual` 교체, 사람 검증 |
| 3 | `main` 병합 후 홍채를 `BossEye.prefab`에 부착, 아바타를 실제 모델로 교체 | 완료 |

- **`BossEye.prefab`은 3단계 전까지 수정 금지.** 홍채는 별도 프리팹으로 만들어 개발 씬에서 시험
- 아바타는 임시 메시(캡슐 등)로 구현. 메시를 교체할 수 있게 참조를 노출
- 레퍼런스 분위기 판단과 **사람 검증은 3단계 뒤에 진행**. 1단계에는 기능 확인만 요청

## 2. 납품물

| 파일 | 역할 |
|---|---|
| `Render/MediaWallController.cs` 수정 | `IWallPresentation` 구현 (색 보간, 흡수 힘) |
| `Presentation/PhasePresenter.cs` | `PhaseChanged` → 테마, 안광, BGM, 슬로우 모션 |
| `Presentation/CombatPresenter.cs` | `CombatEventRaised` → 탄막, 실드, 패링, 폭발 이펙트와 효과음 |
| `Presentation/PlayerTrail.cs` | 플레이어 위치 네온 트레일 |
| `Presentation/PlayerAvatarView.cs` | 아바타 점 렌더링, 위치 추적 |
| `Presentation/ArenaBackdrop` (프리팹 또는 쉐이더) | 전투 배경: 격자, 광선, 데이터 기둥 |
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
- [x] `IWallPresentation` 구현, 기존 미디어 월 동작 회귀 없음
- [x] 파티클 흡수 (ForceField, 게이지 연동)
- [x] 각성 색 전환, 안광
- [x] 홍채 쉐이더, 눈 감김
- [x] 조준선·탄막·실드·패링 이펙트 (`Projectile.prefab` 빛 탄 교체 포함)
- [x] 플레이어 아바타 점 렌더링
- [x] 전투 배경
- [x] Victory 폭발 + 슬로우 모션, GameOver 연출, 복귀
- [x] `AudioDirector` (BGM 페이드 인·아웃, SFX 풀, 클립 누락 허용)

### 자동 검증 — 담당: G5 Presentation
- [x] 기존·신규 EditMode 테스트 전부 통과
- [x] 전체 순환 후 `Time.timeScale == 1`, 테마 값이 초기값으로 복귀
- [x] 전투 중 Play 중단 후 재시작해도 `timeScale`과 색이 정상
- [x] 전투 중 fps 60 이상, GC 0 B/frame (전환 프레임 제외)

### 사람 검증 — 담당: 사용자
- [ ] 흔드는 동안 파티클이 보스로 빨려 들어가고, 2초 동안 화면이 붉게 각성함
- [ ] 패링과 승리 폭발이 타격감 있게 느껴짐
- [ ] 전투 화면이 레퍼런스와 같은 분위기임
- [ ] 광선이 많아도 조준선과 공격을 알아보고 피할 수 있음
- [ ] 아바타가 내 움직임을 따라오고, 맞음과 회피가 그림과 맞아 보임
- [ ] 결과 후 미디어 월로 자연스럽게 복귀함

## 5. 통합 인계

기획 세션 작업: Main에 Presenter 배치, `IWallPresentation`·`IGamePhaseSource`·`ICombatStateSource` 연결.

## 6. 제외 범위
판정 수치 변경(G3), HP 바·결과 문구(G6)

## 작업 로그
- 2026-11-07 3단계 (G4 병합 후, main `ebf9745` 기준)
  - 부착: `BossEye.prefab`의 `Visual/Iris`(위치 0,0,0, 크기 0.75). 임시 `Pupil` 삭제, 쓰지 않는 임시 머티리얼 4개 삭제. **`Visual` 크기 1.8로 확대** (레퍼런스 대비 작았음. 판정은 보스 위치만 쓰므로 영향 없음)
  - 외피: `BossShell.mat` 원본은 그대로 두고 `BossIrisView`가 인스턴스에서 색을 어둡게(`shellTint`), Emission을 `glow`에 연동(`shellEmissionGain` 3). glow 0이면 외피 Emission도 0
  - 아바타: `PlayerAvatar.fbx` 표면 샘플링 12,000점. 회전 (270,0,0)을 점에 굽고 뷰가 등을 카메라로 돌림. 어깨 폭(bounds의 0.71)을 판정 지름에 맞추니 키가 화면 높이와 거의 같음. 기준점은 키의 70% (가슴)
  - 흡수: `absorbGravity` 25 → 10. 25에서는 게이지 0.3에 이미 포화. 10에서 가짜 손 입력으로 입자 평균 거리 4.60 → 1.54 (게이지 0 → 0.7), 보스를 향한 속도 양수, 화면에서 흐름 확인. 실제 손은 사람 검증
  - 붉은 테마 입자 색을 낮춤 (겹치면 흰색으로 날아갔음)
  - 호출 순서 보강: `ArenaBackdrop.Bind`, `PlayerAvatarView.Bind`가 상대의 `Awake`보다 먼저 불려도 동작
- 3단계 자동 검증
  - EditMode 191/191
  - GameOver 실제 Play: 5회 피격 → Result GameOver, timeScale 1 유지, 2.5초에 눈 감김·안광 0, 복귀 후 테마 5.00초에 0
  - 전투 중 Play 중단: timeScale 1, 원본 머티리얼 불변. 재시작 후 crimson 0, `_ColorTop`·`_GlowColor` 초기값
  - GC: Fired, ShieldRaised, ShieldLowered, Parried, PlayerHit 각 20회 할당 0. **BossDefeated는 20회에 5회 할당** (원인 미조사. 전투당 1회라 전환 프레임으로 취급)
  - fps: 실제 모델, 블룸 켬, 1920x1080, 에디터에서 전투 평균 275. 빌드 수치 아님
  - 화면 확인: Awaken, 전투, 패링, 승리 폭발, GameOver, 미디어 월(불 꺼진 조형물, 눈 감김)
  - 첫 Play 콘솔 에러(PlayerLoop 재귀, Missing Profiler.EndSample): 이번 세션 Play 전체에서 재발 없음, 원인은 여전히 미확정
  - 블룸 값과 `OverlayCamera` 조건 변경 없음: threshold 1.1, intensity 0.8, scatter 0.7
- 미확인: 실제 손·몸 입력 플레이(사람 검증), 사운드 재생(파일 미수령), Main 씬 통합, 빌드 fps
- 2026-11-06 1단계 완료 후 폴더 반납. 브랜치 `feature/game-presentation`
  - 자동 검증: EditMode 190/190. Victory 시나리오 1회 순환, 복귀 후 `timeScale` 1, 테마 5.00초에 0, 홍채·배경 0
  - 성능 (개발 씬, 에디터, 1920x1080, 블룸 켬, 오버레이 끔): 전투 평균 362 fps. Presenter·월의 Update 계열 600회 호출 할당 0 (`new object` 600회로 측정기 확인)
  - `MediaWall` 단계 화면은 블룸을 켜고 꺼도 차이 없음. 흡수 지점 뷰포트 (0.50, 0.72)로 보스와 일치
- 1단계 미확인 (3단계에서 확인)
  - **GameOver 연출**: Play 미실행, `PresentationTimeline` 단위 테스트만 통과
  - **파티클 흡수의 실제 움직임**: 필드 위치와 gravity 값만 확인. 손 입력 파티클이 없는 시나리오라 빨려드는 모습은 못 봄. `absorbGravity` 25는 미조정
  - **전투 중 Play 중단 후 재시작**: Result 슬로우 모션 중 중단 후 `timeScale` 1은 확인. 전투 중 중단과 재시작 후 색은 미확인 (체크리스트 미완료)
  - **이벤트 프레임 GC**: 패링·폭발의 `Emit` 묶음은 미측정
  - **Awaken 2초 화면과 번쩍임**: 값만 확인, 화면 미캡처
  - **마지막 조정**: 실드 응집 입자와 스파크 크기 축소 후 화면 미확인
  - **사운드**: 클립이 없어 실제 재생 미확인. 클립 없는 경우 건너뜀만 테스트
- 3단계 할 일
  - `BossIris.prefab`을 `BossEye.prefab`에 부착. 지금은 개발 씬 부트스트랩이 런타임에 붙이고 임시 `Pupil`을 끔. 위치·크기는 부트스트랩 값(`0, 0, 1.01`, 크기 2) 기준이며 모델에 맞게 조정. 쿼드 정면은 보스 +Z
  - 아바타: `PlayerAvatarView.sourceMesh` 교체(또는 `SetMesh`). **Read/Write 필요**. 면적 기준 표면 샘플링이라 정점 쏠림과 무관. `torsoOverBounds`로 몸통 폭을 `hitRadius`에 맞추고, 방향은 35번 작업 로그 확인(+Z가 정면이어야 뒷모습)
  - 보스 렌더러는 패배 시 `CombatPresenter`가 모두 끄고 미디어 월 복귀 때 복원
- 기획 세션 요청 (범위 밖)
  - **블룸**: 현재 Main 카메라 포스트 꺼짐, 기본 프로필 Bloom 강도 0. 전역 Volume에 Bloom을 추가하고 `BossCombatRig/OverlayCamera`의 Post Processing을 켜야 함. 개발 씬 값: threshold 1.1, intensity 0.8, scatter 0.7. 레이어 마스크는 Volume 레이어를 포함할 것
- 통합 인계 (Main)
  - `PresentationRig.prefab` 배치 후 호출: `PhasePresenter.Initialize(phase, combat, wall, wallCamera, config.awakenTransitionSec)`, `CombatPresenter.Initialize(phase, combat, config.hitRadius)`. `combat.Initialize` 뒤, 보스에 홍채가 붙은 뒤 호출. 예시: `Dev/Presentation/PresentationDevBootstrap.cs`
  - `MediaWall.prefab`에 `AbsorbField` 자식 추가, 두 파티클의 External Forces 켬. 프리팹 내부 참조만 추가했으나 Main에서 연결 유지 확인 필요
  - 효과음 슬롯은 `PresentationRig`의 `AudioDirector`에 5개 (BGM + 4종)
  - 쓰지 않게 된 `AimLine_Temp.mat`, `Projectile_Temp.mat`은 남겨 둠
