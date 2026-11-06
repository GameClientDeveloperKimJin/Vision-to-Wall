# 37. 세션 G6 — Game UI

- 담당자: **G6 Game UI**
- 브랜치: `feature/game-ui`
- 담당: 전투 HUD, G-FR-08 F1 패널 확장
- 수정 가능: `Assets/02_Scripts/UI/**`, `Assets/03_Prefabs/UI/**`, 이 문서
- 선행 조건: G5 `main` 병합

## 1. 목표

플레이어에게 전투 상황을 보여주고, 현장 엔지니어가 게임을 조정할 수 있게 한다.

## 2. 납품물

| 파일 | 역할 |
|---|---|
| `UI/GameHudView.cs` | 보스 HP(상단), 플레이어 HP(하단), 남은 시간 |
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
- [ ] `GameHudView`, 페이드, 보간
- [ ] `ResultBannerView`
- [ ] `EngineerPanel.SetGameSources`, 단계·입력·몸 스트림 표시
- [ ] 실드 감도·각성 시간 슬라이더, 저장
- [ ] "감도" → "감지 기준값" 라벨 변경
- [ ] `EngineerPanelBuilder` 동기화, `GameHud.prefab`

### 자동 검증 — 담당: G6 Game UI
- [ ] EditMode 전체 통과
- [ ] MCP 스크린샷: 패널·HUD 잘림 없음 (webcam, external)
- [ ] 슬라이더 저장 후 `config.json` 반영 확인 후 원복
- [ ] 패널 닫힘 상태 GC 0 B/frame

### 사람 검증 — 담당: 사용자
- [ ] 미디어 월에선 HUD가 없고, 각성 때 나타남
- [ ] HP 변화와 남은 시간, 결과 문구를 전시 거리에서 읽을 수 있음
- [ ] F1에서 실드 감도와 각성 시간을 바꾸면 게임에 바로 반영됨

## 5. 통합 인계

기획 세션 작업: Main에 `GameHud.prefab` 배치, `AppBootstrap`에서 `SetGameSources` 호출. 프리팹을 빌더로 재생성했다면 `panel` 참조 재연결.

## 6. 제외 범위
게임 규칙, 연출, `sensorMode` UI

## 작업 로그
- (G6 Game UI가 기록)
