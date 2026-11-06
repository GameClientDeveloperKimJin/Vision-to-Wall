# 37. 세션 G6 — Game UI

- 담당자: **G6 Game UI**
- 브랜치: `feature/game-ui`
- 담당: 전투 HUD, G-FR-08 F1 패널 확장
- 아트 기준: `Assets/04_Docs/eye_of_the_future_art_reference.png`
- 수정 가능: `Assets/02_Scripts/UI/**`, `Assets/03_Prefabs/UI/**`, `Assets/09_UI/**` (VARCO UI 이미지), 이 문서
- 선행 조건: G5 `main` 병합

## 1. 목표

플레이어에게 전투 상황을 보여주고, 현장 엔지니어가 게임을 조정할 수 있게 한다.

## 2. 납품물

| 파일 | 역할 |
|---|---|
| `UI/GameHudView.cs` | 보스 HP(상단), 플레이어 HP(하단), 실드 상태, 콤보, 남은 시간 |
| `UI/ResultBannerView.cs` | `VICTORY` / `GAME OVER` |
| `UI/EngineerPanel.cs` 수정 | 게임 단계, 위치 소스, 강도, 몸 스트림 상태 표시 |
| `UI/ConfigEditorView.cs` 수정 | 실드 발동 감도, 각성 요구 시간 슬라이더 |
| `03_Prefabs/UI/GameHud.prefab` | HUD Canvas |

공개 API (기존 `Initialize` 시그니처 유지, `SetCameraInfo`처럼 추가):

```csharp
// GameHudView
public void Initialize(IGamePhaseSource phase, ICombatStateSource combat);
// EngineerPanel
public void SetGameSources(IGamePhaseSource phase, IPlayerInputSource input, INetworkStats bodyStatsOrNull);
```

## 3. 처리 설계

### HUD
- MediaWall 단계: 완전히 숨김 (`CanvasGroup.alpha = 0`)
- Awaken 단계: 홀로그램 HP 바 페이드인 (`CanvasGroup` 알파 보간)
- Result 종료 후: 페이드아웃
- HP 변화는 보간 표시. 텍스트 갱신은 값이 바뀔 때만
- 전시 거리에서 읽을 수 있는 크기. 1920×1080 기준으로 잘림 없을 것

### HUD 배치 (레퍼런스)

| 위치 | 내용 |
|---|---|
| 상단 중앙 | `BOSS: EYE OF THE FUTURE`, 붉은 HP 바, 퍼센트 |
| 하단 중앙 오른쪽 | `PLAYER HP: 85%`, 시안 바, 실드 중 `SHIELD ACTIVE (실드 가동!)` |
| 우측 하단 | `COMBO x5` 홀로그램 패널 |
| 남은 시간 | 레퍼런스에 없음. 보스 HP 바 아래에 작게 |

- 콤보 = 연속 패링 수. **`CombatEventRaised`만으로 HUD에서 계산**: `Parried`에 +1, 플레이어 피격에 0, 전투 시작에 0. 게임 규칙과 계약은 바꾸지 않음. 2 미만이면 숨김
- 레퍼런스 콤보 패널 아래의 작은 글자는 알아볼 수 없어 넣지 않음
- 폰트는 `GmarketSansTTFMedium SDF`만 사용
- **VARCO UI 이미지 적용** (2026-11-07 사용자 요청, [35번 문서](35_세션G4_AssetPipeline.md) AR-03): G4가 틀 이미지를 생성하면 G6가 `Assets/09_UI/`로 임포트해 HP 바 틀, 콤보 패널, 결과 배너에 적용. 글자와 HP 채움은 Unity에서 표시. 그 전까지 현재 그래픽으로 동작 유지, `Image` 스프라이트를 교체하는 것으로 끝나도록 구성
- 스프라이트는 Sprite, 9-slice 테두리 설정, Mipmap 끔. 검정 배경 이미지로 오면 Additive 합성 머티리얼 사용

### F1 패널
요구사항 G-FR-08 추가 항목:

| 항목 | 표시 |
|---|---|
| 게임 단계 | `단계: 미디어 월 / 각성 / 전투 / 결과` + 각성 게이지 |
| 플레이어 입력 | 위치 소스(몸 / 모션 / 없음), 강도 |
| 몸 스트림 | 수신 수, 버림 수 (external일 때만) |
| 패킷 로그 | 기존 JSON 형식 유지 (`PKT: {x, y, v}` 요구사항 충족) |

| 슬라이더 | config 필드 | 범위 | 비고 |
|---|---|---|---|
| 실드 발동 감도 | `shieldThreshold` | 0.3~1.0 | 도움말: "높일수록 크게 흔들어야 발동" |
| 각성 요구 시간 | `awakenChargeSec` | 1~10초 | |

- 기존 "감도" 라벨은 슬라이더를 올릴수록 둔감해져 혼동을 줌 → **"감지 기준값"** 으로 변경, 도움말에 "낮을수록 민감" 명시
- 패널 높이가 1080을 넘지 않는지 webcam·external 두 모드에서 확인
- 패널 닫힘 상태 비용 0 유지

## 4. 체크리스트

### 구현 — 담당: G6 Game UI
- [x] `GameHudView`, 페이드, 보간
- [x] 실드 상태 표시, 콤보 계산과 표시
- [x] `ResultBannerView`
- [x] `EngineerPanel.SetGameSources`, 단계·입력·몸 스트림 표시
- [x] 실드 감도·각성 시간 슬라이더, 저장
- [x] "감도" → "감지 기준값" 라벨 변경
- [x] `EngineerPanelBuilder` 동기화, `GameHud.prefab`

### 자동 검증 — 담당: G6 Game UI
- [x] EditMode 전체 통과 (234/234)
- [x] MCP 스크린샷: 패널·HUD 잘림 없음 (webcam, external)
- [x] 슬라이더 저장 후 `config.json` 반영 확인 후 원복
- [x] 패널 닫힘 상태 GC 0 B/frame (600회 호출 할당 0건)

### 사람 검증 — 담당: 사용자
- [x] 미디어 월에선 HUD가 없고, 각성 때 나타남
- [x] HP 변화와 남은 시간, 결과 문구를 전시 거리에서 읽을 수 있음
- [ ] HUD 배치와 색이 레퍼런스와 어울림 (VARCO 이미지 적용 후 확인)
- [x] 연속 패링하면 콤보가 오르고, 맞으면 사라짐
- [x] F1에서 실드 감도와 각성 시간을 바꾸면 게임에 바로 반영됨

## 5. 통합 인계

기획 세션 작업: Main에 `GameHud.prefab` 배치, `AppBootstrap`에서 `SetGameSources` 호출. 프리팹을 빌더로 재생성했다면 `panel` 참조 재연결.

## 6. 제외 범위
게임 규칙, 연출, `sensorMode` UI

## 작업 로그
- 2026-11-07 구현과 자동 검증 완료. 기능 사람 검증 4/4 통과 (사용자, Main + 손 센서, external, HUD는 Play 중 런타임 연결). 레퍼런스 어울림 항목과 VARCO 이미지 적용 대기
- 검증 수치 (Main, 1920x1080, Play 중 프리팹을 런타임 생성해 시험. 씬 저장 안 함)
  - 승리 흐름은 실제 규칙 경유 (실드 + 탄 도착을 강제): 콤보 0→3, 보스 바 0.15초에 0, 보스 숨김 0.6초, 배너 0.6초 시작 1.06초 완료, 미디어 월 복귀 후 0.6초에 HUD 알파 0. **보스 숨김과 HP 바 겹침 없음** (G5 요청 확인)
  - 패배 흐름: `GAME OVER`, 플레이어 0%, webcam 모드 패널 하단 약 800/1080. external 모드 약 730/1080
  - 슬라이더: `shieldThreshold` 0.5, `awakenChargeSec` 6이 즉시 `ConfigService.Current`에 반영되고 저장 시 파일에 기록됨. 게임이 그 값으로 동작하는지는 사람 검증 대상
  - GC: HUD 값 변화 없는 프레임 0건, 패널 닫힘 0건. TMP `SetText`는 호출당 2건 (string 포맷, StringBuilder, CharArray 모두 동일). 그래서 HP 퍼센트는 움직이는 바가 아니라 실제 값으로 갱신 (피격당 1회), 시계는 1초당 1회
  - 첫 Play 콘솔: PlayerLoop 재귀, Missing Profiler.EndSample 재발 없음. webcam 모드의 `Could not start graph`는 손 센서 실행 중이라 발생 (코드 문제 아님). MCP 스크린샷 뒤 Play 일시정지 재현됨
- 2026-11-07 아바타 재수정 `753dc11` 확인 (기획 세션 요청, 코드 수정 없음). 쉐이더 에러 0, EditMode 241/241. Main 전투 1920x1080 에디터 fps: 30,000점 227.1, 12,000점 234.7. 위치별 yaw: x 0.15 +24.5, x 0.5 0, x 0.85 -24.5. 사용자 판단: 등 잘 보임, 이상 없음
- 구현 메모
  - 전투 상태는 다음 `Begin`까지 이전 판 값을 유지함 (승리 후 `BossHp01` 0). 그래서 Awaken 동안 HP는 100%, 시간은 `combatDurationSec`로 표시
  - HUD는 Screen Space Overlay라 블룸을 받지 않음. 색이 그대로 나오는 대신 네온 번짐이 없음. 배경 대비를 위해 어두운 판을 깔았음
  - VARCO 틀 적용: `Assets/09_UI`에 투명본 사용 (일반 알파로 합성 문제 없어 Additive 안 씀). 메뉴 `VisionToWall/UI/Build GameHud Prefab`이 임포트 설정(Sprite, Mipmap 끔, 9-slice)과 배치를 함께 처리. 틀의 구멍 크기는 알파에서 측정해 `GameHudBuilder`에 상수로 둠
  - 콤보 이미지는 한 장을 `ComboPanelFrame.png`와 `ComboBase.png`로 나눔 (190행, 경계 14행 페이드). 패널만 세로로 늘리기 위함
  - 결과 배너 틀은 승리·패배 색으로 틴트
- 통합 인계 (Main)
  - `GameHud.prefab` 배치 후 호출: `hud.Initialize(phaseMachine, combat)`, `panel.SetGameSources(phaseMachine, playerInput, external ? bodyReceiver : null)`
  - `EngineerPanel.prefab`을 빌더로 재생성함. 루트 컴포넌트 fileID `3235550296448969791`은 유지되어 `panel` 참조는 끊기지 않음. Main의 자식 레이아웃 오버라이드(값 0)는 다시 기록될 수 있음
  - HUD 상단 판은 화면 위 116까지 차지함. 전투 중 보스 링 상단은 약 90~105여서 링 윗부분이 판과 조금 겹침 (스크린샷 기준, 글자는 가리지 않음). 틀 이미지 적용 때 다시 확인
