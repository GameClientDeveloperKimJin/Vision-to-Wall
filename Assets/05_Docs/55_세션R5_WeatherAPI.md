# 55. 세션 R5 — Weather API (v2.2)

- 담당자: **R5 Weather API**
- 브랜치: `feature/roadmap-weather`
- 담당: 외부 날씨 REST API 조회, 미디어 월 색 테마와 보스 공격 연출 연동
- 수정 가능: `Assets/02_Scripts/Weather/**` (새 어셈블리 `VisionToWall.Weather`), `Assets/02_Scripts/Weather/Tests/**`, `Assets/02_Scripts/Weather/Dev/**`, 이 문서
- 선행 조건: RS `main` 병합, 1절 사용자 결정(2026-11-08 완료), 기획 세션 선행 작업(완료), **사용자 API 키 발급**

## 1. 착수 전 사용자 결정

| 항목 | 선택지 |
|---|---|
| API | 기상청 단기예보 (공공데이터포털, 한국 좌표 격자) / OpenWeatherMap (영문, 위경도) |
| 위치 | 전시장 위치 (도시 또는 좌표) |
| "보스 공격 속성" 범위 | 연출만 (색, 탄 모양, 효과음) / 규칙까지 (탄 속도, 조준 시간 등 판정 값) |
| 날씨 분류 | 맑음·비·눈 3종 기본. 흐림·안개 추가 여부 |

**사용자 결정 (2026-11-08)**: 기상청 단기예보, 서울(격자 60, 127, 전시 장소가 정해지면 config만 변경), 공격 속성은 **연출만**, 날씨 5종(맑음·흐림·비·눈·안개).

- 안개: 기상청 단기예보에는 안개 항목이 없음(하늘상태 SKY, 강수형태 PTY만). 실제 안개 판정은 별도 API(지상관측 시정)가 필요하므로 사용자에게 확인 중. 확정 전까지 안개는 `weatherOverride: "fog"`로만 표시
- 사용 API: 초단기실황(`getUltraSrtNcst`, 강수형태)과 초단기예보 또는 단기예보(하늘상태). 분류: PTY 비·빗방울 → Rain, 눈·눈날림 → Snow, 비/눈 → Rain, PTY 없음이면 SKY 맑음 → Clear, 구름많음·흐림 → Cloudy

## 2. 기획 세션 선행 작업

**완료 (2026-11-08)**:
- config 필드: `weatherEnabled`(false), `weatherProvider`(`kma`), `weatherLocationName`(서울), `weatherGridX`·`weatherGridY`(60, 127), `weatherRefreshMin`(30, 10~180), `weatherOverride`(빈 값 = 실제, `clear`/`cloudy`/`rain`/`snow`/`fog` = 고정, 시연용)
- **API 키는 config.json에 넣지 않는다.** `Assets/StreamingAssets/secrets.json` (`kmaServiceKey`, git 제외, 양식 `secrets.example.json`). 사용자가 직접 붙여넣음. 세션은 키를 출력·로그·커밋하지 말 것
- Core 계약 `Core/WeatherContracts.cs`: `WeatherKind { Unknown, Clear, Cloudy, Rain, Snow, Fog }`, `IWeatherSource { Current; IsOverride; event Changed }`

## 3. 납품물

| 파일 | 역할 |
|---|---|
| `Weather/WeatherClient.cs` | `UnityWebRequest` 비동기 조회, 타임아웃, 재시도, 응답 파싱 |
| `Weather/WeatherService.cs` | `IWeatherSource` 구현. 주기 조회, 실패 시 마지막 값 유지, `weatherOverride` |
| `Weather/WeatherTheme.cs` | 날씨별 색 팔레트와 보스 공격 연출 값 (ScriptableObject) |

## 4. 처리 설계

- 네트워크 실패, 키 없음, 오프라인이 기본 상황이라고 가정: 게임은 항상 기본 테마로 정상 동작
- 조회는 메인 루프를 막지 않음. 응답 파싱 할당은 조회 시점에만 허용 (프레임 루프 할당 0)
- 테마 적용은 G5 `IWallPresentation` 경로를 사용. 각성·전투 색 전환(G5)과 충돌하지 않게 "기본 색"만 바꿈. 필요한 확장은 기획 세션에 요청
- F1 패널: 현재 날씨, 마지막 조회 시각, 오류 표시 (연결은 기획 세션)

## 5. 체크리스트

### 구현 — 담당: R5 Weather API
- [x] 클라이언트, 파서, 서비스, 오버라이드
- [x] 날씨별 테마 3종 이상 (5종 + 기본)
- [x] Dev 씬: 오버라이드로 날씨 전환 확인
- [ ] Main 미디어 월·탄 적용: 7절 인터페이스 변경 후 기획 세션 통합 (Dev 씬은 대역 화면)

### 자동 검증 — 담당: R5 Weather API
- [x] EditMode: 응답 파싱(정상, 누락 필드, 오류 응답), 분류 매핑, 실패 시 값 유지
- [x] 키 없이 실행해도 콘솔 에러 없이 기본 테마 (경고 1회)
- [x] 키·계정 정보가 커밋에 없음 (`git log -p`로 확인)
- [x] 기존 EditMode 전체 통과

### 사람 검증 — 담당: 사용자
- [x] 오버라이드로 맑음·비·눈을 바꾸면 미디어 월과 보스 공격 연출이 바뀜 (Dev 대역 화면, 2차에서 대기 연출 포함 통과)
- [x] 실제 API로 현재 날씨가 표시됨

## 6. 제외 범위

판정 값 변경(1절에서 "규칙까지"를 고르면 범위 재정의), 예보, 다중 위치

## 7. 구현 결과 (R5 Weather API)

### 파일
| 파일 | 역할 |
|---|---|
| `Weather/KmaProtocol.cs` | 기준 시각(KST), URL 생성, 키 가림(`Redact`), JSON·XML 오류 파싱, PTY/SKY → `WeatherKind` 분류, override 문자열 |
| `Weather/WeatherClient.cs` | `IWeatherTransport`(테스트 대역 가능), `UnityWebRequestTransport`(completed 콜백, 코루틴 없음), 초단기실황 → 초단기예보 순서 조회 |
| `Weather/WeatherPoller.cs` | 엔진 무관 핵심: 주기 조회, 실패 시 값 유지, 재시도, override, 경고 1회, 키 문자열 제거 |
| `Weather/WeatherService.cs` | `IWeatherSource` MonoBehaviour. `Initialize(AppConfig)`, `SetOverride(kind)`, `RefreshNow()`, `Poller`(F1 표시용 상태) |
| `Weather/WeatherSecrets.cs` | `secrets.json`의 `kmaServiceKey` 읽기. 결과는 상태만 알림 (`Ok/Missing/Unreadable/Empty/Placeholder`) |
| `Weather/WeatherTheme.cs` + `WeatherTheme.asset` | 날씨별 벽 기본 색(`WeatherWallColors`)과 탄 연출(`WeatherAttackLook`). `Unknown` = 현재 프리팹 값 그대로 |
| `Weather/Dev/WeatherDev.unity` | 실제 `WeatherService` + 대역 벽·탄 화면 (IMGUI). 0 실제, 1~5 맑음/흐림/비/눈/안개, R 즉시 조회, C 전투 색, M 음소거, O 오버레이 |

### 동작 규칙
- 조회: `getUltraSrtNcst`(PTY, 기준 HH:00, 15분 지연) → `getUltraSrtFcst`(SKY·PTY, 기준 HH:30, 45분 지연, 가장 이른 예보 시각). 비·눈은 둘 중 하나로 충분, 강수 없을 때만 SKY 필요
- 분류: PTY 3·7 → 눈, 그 밖의 PTY>0(1, 2, 4, 5, 6) → 비, PTY 0이면 SKY 1(2) → 맑음, 3·4 → 흐림
- 실패: 마지막 값 유지(처음부터 실패면 `Unknown` = 기본 테마). 재시도 1, 2, 4분 ... 최대 `weatherRefreshMin`. 인증 오류(HTTP 401/403, 코드 20/30/31/32)는 60분 뒤, 예보 호출 생략
- 로그: 경고는 세션당 1회(키 없음, 첫 실패, 잘못된 override), 이후는 일반 로그. 요청 URL은 `serviceKey=***`로만 출력
- override: config 값이 있으면 네트워크 없이 고정(`weatherEnabled` false여도 동작). `SetOverride(Unknown)`이면 마지막 실제 값으로 복귀
- 할당: `Update`는 시각 비교만. 조회 시점에만 할당

### 테마 (연출만, 판정 값 없음)
| 날씨 | 벽 기본 색 | 탄 (꼬리 시간·폭·광채·효과음 높이) |
|---|---|---|
| 기본(Unknown) | 현재 MediaWall 값 | 현재 Projectile 값, 1·1·1·1 |
| 맑음 | 조금 밝은 하늘색, 금빛 주변 입자 | 주황빛 붉은색, 1·1·1.1·1.05 |
| 흐림 | 회청색, 채도 낮음 | 어두운 진홍, 1.2·1.3·1.25·0.9 |
| 비 | 깊은 청록, 주변 입자 진하게 | 붉은 자홍 긴 줄기, 1.8·0.6·0.85·1.15 |
| 눈 | 얼음빛 흰색·라벤더 | 분홍빛 짧은 결정, 0.6·1.2·1.3·1.25 |
| 안개 | 대비 낮은 회녹색 | 흐린 붉은색 큰 광채, 1.4·1.5·1.6·0.85 |

탄 색은 모두 붉은 계열 유지 (공격은 붉은색이라는 G5 규칙, 테스트 `AttackBolt_StaysRed`). 전투 crimson 색은 날씨와 무관.

### 대기 연출 (`WeatherAtmosphere`, 사람 검증 피드백 반영)
사람 검증 1차: "통과지만 실제 날씨 느낌이 전혀 안 남". 원인은 색만 바뀌고 주변 입자의 움직임과 모양이 모든 날씨에서 같았던 것. 날씨별로 주변 입자의 모양·속도·크기·양, 화면 막(veil), 햇빛을 추가.

| 날씨 | 입자 모양 (스프라이트) | 움직임 | 그 밖 |
|---|---|---|---|
| 기본 | Dot (현재 원형 빛) | 현재와 같음 | 없음 |
| 맑음 | Sparkle (4각 별빛) | 거의 정지, 반짝임 | 오른쪽 위 햇빛 |
| 흐림 | Puff (구름) | 가로로 천천히 흐름, 큰 크기 | 옅은 막 0.08 |
| 비 | Streak (빗줄기 2×2) | 빠르게 비스듬히 낙하, 양 4배 | 막 0.06 |
| 눈 | Flake (눈송이 2×2) | 천천히 흔들리며 낙하, 회전 | 막 0.03 |
| 안개 | Haze (연무) | 아주 큰 연무가 가로로 흐름 | 막 0.2 |

- 단위: 벽 높이/초. 실제 `AmbientParticles`(Box 18×10, 수명 6~10초, 노이즈 0.15) 대응: velocity → Velocity over Lifetime, stretch → Stretched Billboard length, sway → Noise, size → Start Size 배율, density → Emission rate 배율, veil → 벽 전체 오버레이 알파, topLight → 오른쪽 위 따뜻한 광원
- 스프라이트: `Weather/Textures/` (VARCO 3D `GenerateImage`, 2026-11-08 사용자 승인). 흰색·검정 배경, 임포트에서 Alpha from Grayscale로 투명도 생성, 색은 실행 중에 입힘. 빗줄기·눈송이는 2×2 시트 (`WeatherTheme.GetSpriteGrid`)
  - `WeatherFlakeSheet.png`, `WeatherStreakSheet.png`(512), `WeatherCloudPuff.png`, `WeatherFogPuff.png`, `WeatherSparkle.png`(256)
  - 비용: 시험 1장 50pt + 4장 200pt = 250pt (견적은 20pt/장이었으나 계정에 기억된 모델 `gpt-image-2.5-flare` 단가 50pt 적용, 시험 후 사용자에게 알리고 재승인)
- 전투(crimson)로 전환되면 막·햇빛은 사라지고 입자 색은 crimson 쪽으로 Lerp (Dev 씬 C 키로 확인)

### 인터페이스 변경 요청 (기획 세션, Core)
`IWallPresentation`은 그대로 두고 별도 인터페이스 2개 추가 제안 (기존 구현·mock 수정 불필요):

```csharp
// Core/WeatherContracts.cs 에 추가. Weather/WeatherTheme.cs 의 WeatherWallColors, WeatherAtmosphere,
// WeatherParticleShape, WeatherAttackLook 을 그대로 이동
public interface IWallBaseTheme      // Render.MediaWallController 구현
{
    /// 평상시(crimson 0) 색만 교체. crimson 색은 그대로 두고 ApplyTheme()으로 Lerp(base, crimson, _crimson) 재적용.
    /// 날씨가 바뀔 때만 호출 (매 프레임 아님)
    void SetBaseColors(in WeatherWallColors colors);

    /// 주변 입자(AmbientParticles)의 모양·움직임·막·햇빛. sprite는 WeatherTheme.GetSprite(shape), grid는 GetSpriteGrid(shape).
    /// 막·햇빛은 crimson이 올라가면 (1 - crimson)으로 약해짐
    void SetAtmosphere(in WeatherAtmosphere atmosphere, Texture2D sprite, int spriteGrid);
}

public interface IAttackLookTarget   // Game.Presentation 구현 (ProjectileVisual 풀 + AudioDirector)
{
    /// 탄 trail/glow 머티리얼 _Color, TrailRenderer.time·widthMultiplier 배율, glow 시각 크기 배율, LaserFire pitch.
    /// 판정(콜라이더, 속도, 조준 시간)에는 사용 금지
    void SetAttackLook(in WeatherAttackLook look);
}
```
- `MediaWallController`: `backgroundTop/Bottom`, `glowColor`, `particleColorLow/High`와 시작 시 읽는 `_ambientColor`를 기본 색으로 교체 가능하게. 각성 중 날씨가 바뀌어도 crimson 쪽은 변하지 않으므로 충돌 없음
- 탄: 풀의 모든 탄에 머티리얼 인스턴스 대신 `MaterialPropertyBlock` 또는 공유 머티리얼 2개 값 변경 권장 (날씨 변경 시 1회)
- 연결(기획 세션 `AppBootstrap`): `WeatherService.Initialize(config)` → `Changed`에서 `theme.Get(kind)` → 두 인터페이스 호출. 시작 시 `Current`로 1회 적용

## 작업 로그
- 2026-11-08 R5: `feature/roadmap-weather` 생성. Weather 어셈블리, 테스트 69개(파싱·분류·재시도·override·키 가림·테마), Dev 씬
- 2026-11-08 R5: 실제 API 확인 — 키 동작, HTTP 200, 서울(60, 127) 흐림 (PTY 0, SKY 4, 예보 기준 20261108 1430). Dev 씬 Play에서도 같은 값, 콘솔 에러·경고 0
- 2026-11-08 R5: Play 중 `WeatherService.Update` 600회 GC 할당 0 (기준선 `new object()` 600회 = 600). override 5종 전환과 실제 값 복귀 확인
- 2026-11-08 R5: EditMode 전체 455/455 (기존 386 + Weather 69). Editor.log·소스·문서에 키 문자열 없음 확인 (값 출력 없이 검사)
- 2026-11-08 R5: 사람 검증 1차 3/3 통과, 단 "실제 날씨 느낌이 없음". `WeatherAtmosphere`(입자 모양·움직임·막·햇빛) 추가, VARCO 스프라이트 5장(250pt) 적용, Dev 씬 대역 화면 갱신. 5종 캡처로 확인 후 크기·투명도 조정 (눈송이·구름·안개 확대, 맑음은 별빛 확대·햇빛 광원). EditMode 456/456
- 2026-11-08 R5: Dev 화면의 고정 점 무리(손 반응 대역)가 잡음으로 보인다는 피드백 → 기본 숨김, P 키로 마우스 주변에만 표시 (`84ff9ed`)
- 2026-11-08 R5: 사람 검증 2차 3/3 통과 (5종 날씨 느낌, 전투 색 전환, 실제 날씨 표시). 사용자: "이상 없다"

## 8. R5b — 날씨 적용 (R5 세션이 이어서 진행)

- 브랜치: `feature/roadmap-weather-hookup`
- 선행 작업 (기획 세션, 2026-11-08 완료): `WeatherWallColors`, `WeatherAtmosphere`, `WeatherParticleShape`, `WeatherAttackLook`를 `Core/WeatherContracts.cs`로 이동, `IWallBaseTheme`(`SetBaseColors`, `SetAtmosphere`), `IAttackLookTarget`(`SetAttackLook`) 추가. `WeatherTheme.asset` 직렬화 값 유지 확인
- 수정 가능 (이번만 확장): `Assets/02_Scripts/Render/MediaWallController.cs`(`IWallBaseTheme` 구현), `Assets/03_Prefabs/Render/MediaWall.prefab`, `Assets/02_Scripts/Game/Presentation/**`(`IAttackLookTarget` 구현), `Assets/03_Prefabs/Game/PresentationRig.prefab`, `Projectile.prefab`, `Assets/02_Scripts/Weather/**`, 각 Tests·Dev, 이 문서
- 지킬 것: 각성·전투 색 전환(G5)과 충돌 금지 (막·햇빛은 crimson에 따라 약해짐), 날씨 꺼짐(Unknown)에서 현재 벽과 같음, 판정 값 사용 금지, 프레임 할당 0, fps 전후, **미디어 월 손 반응(파티클·물결)이 날씨 입자에 묻히지 않을 것** (비 입자 4배가 가장 위험, 실제 벽 기준으로 양·밝기 재조정)
- 통합 인계: `AppBootstrap`에서 `WeatherService.Initialize(config)` → `Changed`와 시작 시 1회 `theme.Get(kind)`로 두 인터페이스 호출 (기획 세션)
- 사람 검증 (Main 또는 Dev, `weatherOverride`로 5종): ① 날씨별 미디어 월 느낌 ② 손을 흔들면 손 반응이 보임 ③ 비·눈에서 탄 연출이 바뀌고 판정은 그대로

### R5b 구현 결과

| 파일 | 변경 |
|---|---|
| `Render/MediaWallController.cs` | `IWallBaseTheme` 구현. 평상시 색을 런타임 필드(`_baseTop` 등)로 분리해 Lerp(기본, crimson). 막은 배경색에 섞음(모든 입자 뒤). 날씨 입자 시스템과 햇빛 quad는 처음 날씨가 켜질 때 생성 |
| `Weather/Shaders/WeatherParticle.shader`, `Weather/Materials/WeatherParticle.mat` | 가산 블렌딩 스프라이트 입자. 스프라이트 알파 × 정점 색 × `_Fade` |
| `MediaWall.prefab` | `weatherMaterial` 지정 (그 밖의 값은 코드 기본값) |
| `Game/Presentation/ProjectileVisual.cs` | `ApplyLook`: trail·glow `_Color`(MaterialPropertyBlock, 공유 머티리얼 불변), trail time·width, glow 시각 크기 |
| `Game/Presentation/AudioDirector.cs` | `LaserPitch` (LaserFire만, 0.5~2) |
| `Game/Presentation/CombatPresenter.cs` | `IAttackLookTarget`. Initialize 전 호출도 보관했다가 풀의 탄에 적용 |
| `Weather/WeatherThemeBinder.cs` | 시작 시 1회 + `Changed`마다 두 인터페이스 호출 |
| `Weather/Dev/Editor/WeatherPlayMenu.cs` | Play 중 메뉴 `VisionToWall/Weather (Play)/…`로 실제 Main 벽에 5종 적용 (AppBootstrap 연결 전 사람 검증용, 저장 없음) |

규칙
- Unknown(기본 대기 Dot, 스프라이트 없음): 날씨 입자·햇빛 꺼짐, 막 0, 색 = 직렬화 값 그대로 → 현재 벽과 같음 (테스트 `DefaultTheme_LeavesWallExactlyAsBefore`, 객체 생성도 없음)
- 전투: 배경·글로우·입자 색은 crimson 1에서 날씨와 무관하게 같음. 날씨 입자 밝기는 crimson에 따라 `weatherCombatKeep`(0.35)까지, 햇빛은 0까지
- 손 반응 보호: 날씨 입자는 손 입자 뒤(z +0.2, Queue Transparent-10), 손 활동량에 따라 최대 60% 흐려짐(`weatherHandDim`). 막은 배경에 섞여 손 입자를 덮지 않음
- 낙하 입자 수명 = 화면 통과 시간 (스폰 박스 1.3배 높이 / 낙하 속도, 0.5~12초), 날씨 전환 시 `Simulate`로 즉시 채움
- 판정: 탄 프리팹에 콜라이더 없음(테스트 `Bolt_HasNoCollider_SoLookCannotChangeHits`), 판정 코드(Combat) 수정 없음

검증 (2026-11-08)
- EditMode 470/470 (+14: Render 5, Presentation 5, Weather 4)
- Main Play (가짜 손 프레임 80점 주입, 실제 카메라 구독 해제 후 복구): fps 날씨 없음 460/415(평상/손) → 맑음 408/456, 흐림 466/474, 비 478/437, 눈 426/452, 안개 460/467. 측정 편차 범위 안 (에디터 비포커스)
- 비 상태 벽 `Update` 600회 GC 할당 0 (기준선 600)
- 스크린샷 (로컬 `Temp/`, 커밋 안 함): `r5b_contact.png`(6종 × 평상/손), `r5b2_contact.png`(비 수명 수정 후 비·눈). 모든 날씨에서 손 반응(흰 덩어리 + 물결)이 뚜렷함
- 조정: 실제 벽에서 비가 성김 → 낙하 수명 하한 1.5초 → 0.5초 (화면 밖 소모 제거)

통합 인계 (기획 세션, `AppBootstrap`)
```csharp
[SerializeField] private WeatherService weatherService;   // 씬에 컴포넌트 추가
[SerializeField] private WeatherTheme weatherTheme;       // Assets/02_Scripts/Weather/WeatherTheme.asset
private WeatherThemeBinder _weatherBinder;

// wall.Initialize(...), combat.Initialize(...), combatPresenter.Initialize(...) 다음
weatherService.Initialize(config);
_weatherBinder = new WeatherThemeBinder(weatherTheme);
_weatherBinder.Bind(weatherService, wall, combatPresenter);
// OnDestroy: _weatherBinder?.Unbind();
```
- 순서 주의: `combatPresenter.Initialize` 뒤에 Bind해야 탄 풀에 바로 적용됨 (앞이어도 Initialize 때 재적용되므로 동작은 함)
- `PhasePresenter`의 `SetTheme(crimson)`과 독립. 호출 순서 무관
- 개발 메뉴 `VisionToWall/Weather (Play)`는 바인더를 따로 만들므로, AppBootstrap 연결 후에는 마지막에 호출한 쪽이 적용됨 (검증용)

