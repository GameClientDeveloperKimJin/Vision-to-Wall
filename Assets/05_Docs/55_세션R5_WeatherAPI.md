# 55. 세션 R5 — Weather API (v2.2)

- 담당자: **R5 Weather API**
- 브랜치: `feature/roadmap-weather`
- 담당: 외부 날씨 REST API 조회, 미디어 월 색 테마와 보스 공격 연출 연동
- 수정 가능: `Assets/02_Scripts/Weather/**` (새 어셈블리 `VisionToWall.Weather`), `Assets/02_Scripts/Weather/Tests/**`, `Assets/02_Scripts/Weather/Dev/**`, 이 문서
- 선행 조건: R4 `main` 병합, **1절 사용자 결정**, 기획 세션 선행 작업

## 1. 착수 전 사용자 결정

| 항목 | 선택지 |
|---|---|
| API | 기상청 단기예보 (공공데이터포털, 한국 좌표 격자) / OpenWeatherMap (영문, 위경도) |
| 위치 | 전시장 위치 (도시 또는 좌표) |
| "보스 공격 속성" 범위 | 연출만 (색, 탄 모양, 효과음) / 규칙까지 (탄 속도, 조준 시간 등 판정 값) |
| 날씨 분류 | 맑음·비·눈 3종 기본. 흐림·안개 추가 여부 |

권장: API는 전시 장소가 국내면 기상청. 공격 속성은 **연출만** (판정은 G3 그대로 두어 난이도가 날씨로 흔들리지 않게).

## 2. 기획 세션 선행 작업

- config 필드: `weatherEnabled`(false), `weatherProvider`, `weatherLocation`, `weatherRefreshMin`(30), `weatherOverride`(빈 값 = 실제 날씨, `clear`/`rain`/`snow` = 고정. 시연용)
- **API 키는 config.json에 넣지 않는다.** `StreamingAssets/secrets.json`(gitignore) 또는 환경변수. `.gitignore` 추가는 기획 세션
- Core 계약: `WeatherKind` enum, `IWeatherSource { WeatherKind Current; event Action<WeatherKind> Changed; }`

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
- [ ] 클라이언트, 파서, 서비스, 오버라이드
- [ ] 날씨별 테마 3종 이상
- [ ] Dev 씬: 오버라이드로 날씨 전환 확인

### 자동 검증 — 담당: R5 Weather API
- [ ] EditMode: 응답 파싱(정상, 누락 필드, 오류 응답), 분류 매핑, 실패 시 값 유지
- [ ] 키 없이 실행해도 콘솔 에러 없이 기본 테마
- [ ] 키·계정 정보가 커밋에 없음 (`git log -p`로 확인)
- [ ] 기존 EditMode 전체 통과

### 사람 검증 — 담당: 사용자
- [ ] 오버라이드로 맑음·비·눈을 바꾸면 미디어 월과 보스 공격 연출이 바뀜
- [ ] 실제 API로 현재 날씨가 표시됨

## 6. 제외 범위

판정 값 변경(1절에서 "규칙까지"를 고르면 범위 재정의), 예보, 다중 위치

## 작업 로그
- (R5 Weather API가 기록)
