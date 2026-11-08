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
