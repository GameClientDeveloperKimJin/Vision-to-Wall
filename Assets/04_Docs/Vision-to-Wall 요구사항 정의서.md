# Vision-to-Wall 요구사항 정의서

Nov 5, 2026 · @진

## 1. 프로젝트 개요

웹캠 1대와 PC 1대로 실제 전시 현장의 \[센서 감지 → UDP 전송 → 실시간 렌더링·현장 제어\] 파이프라인을 48시간 안에 모사·검증한다.

| 항목 | 내용 |
| --- | --- |
| 프로젝트명 | Vision-to-Wall (웹캠 비전 센서 및 UDP 통신 기반 인터랙티브 전시 시스템) |
| 개발 기간 | 2일 (48시간 프로토타이핑) |
| 엔진 / 언어 | Unity, C# |
| 렌더링 | URP(또는 Built-in) Custom Shader, Particle System |
| 통신 | .NET Socket (System.Net.Sockets.UdpClient), 127.0.0.1 로컬호스트 |
| 형상 관리 | Git (기능 단위 브랜치) |
| 입력 장치 | 웹캠 1대 (가상 LiDAR 역할) |

기획 의도: 고가의 LiDAR와 대형 미디어 월 없이도, 현장 표준 아키텍처를 그대로 따르는 구조를 만들어 센서 장비만 교체하면 실제 현장에 적용할 수 있음을 증명한다.

## 2. 시스템 아키텍처

시스템은 Sensor → Transport → Sink 3개 모듈이 UDP 패킷으로만 연결되며, config.json과 F1 패널이 현장 제어를 맡는다.

&#91;embedded content: Vision-to-Wall 아키텍처 · 3개 모듈 + 현장 도구\]

실제 LiDAR 도입 시 Sensor 모듈만 교체하면 되며, F1 패널의 Threshold 슬라이더는 Sensor 감도를 실시간으로 바꾼다.

## 3. 채용공고 요구조건 매핑

요구조건 10개 중 8개를 1차 스펙에서 충족하고, 디바이스 제어와 REST API 2개는 v2.0 로드맵으로 넘긴다.

| 구분 | 요구 조건 | 상태 | 구현 내역 / 추후 계획 | 관련 FR |
| --- | --- | --- | --- | --- |
| 담당업무 | Unity 엔진 전시 콘텐츠 제작 | 충족 | 관람객 움직임에 실시간 반응하는 파티클 및 물결(Ripple) 셰이더 미디어 월 | FR-07 |
| 담당업무 | 개발 검증·시연 목적 현장 설치 | 충족 | 조도·네트워크 변화에 대응하는 F1 관제 UI + 외부 config.json 분리 | FR-08, FR-09 |
| 담당업무 | 로봇·모터 등 디바이스 제어 | 불충족 (추후) | (X, Y) 좌표 → Pan/Tilt 각도 패킷 변환, 가상 3D 무빙라이트 + RS-232/485 시리얼 모듈 | v2.1 |
| 자격요건 | C#, Unity 활용 | 충족 | WebCamTexture 픽셀 처리, 링 버퍼/배열 재사용 Zero-GC 설계 | FR-01\~03 |
| 자격요건 | 협업 툴(Git, SVN) | 충족 | Sensor / Network / Render / UI 기능 단위 브랜치·커밋 이력 | — |
| 우대사항 | 전시 체험형 키오스크 콘텐츠 | 충족 | Attract Mode ↔ Interactive Mode 자동 전환 FSM | FR-06 |
| 우대사항 | TCP / UDP 통신 개발 | 충족 | 센서↔렌더러 간 127.0.0.1 UDP 비동기 소켓 파이프라인 | FR-04, FR-05 |
| 우대사항 | LiDAR 등 센서 기반 인터랙션 | 충족 | 16x16 그리드 다운샘플링 + 차영상 → 2D LiDAR와 동일한 정규화 좌표 스트림 | FR-02, FR-03 |
| 우대사항 | AI 실무 활용 | 충족 | Claude Code + MCP로 아키텍처 설계, 셰이더/소켓 보일러플레이트 가속 | — |
| 우대사항 | REST API 연동 | 불충족 (추후) | 초기화 단계에서 날씨 REST API(UnityWebRequest) 연동 → 테마 자동 변경 | v2.2 |

## 4. 기능 요구사항 (MVP)

MVP는 4단계 9개 기능(FR-01\~FR-09)으로 구성되며, 모두 48시간 안에 동작해야 한다.

### STEP 1. Source & Process — 웹캠 기반 가상 LiDAR 센서

| ID | 기능 | 요구사항 | 수용 기준 |
| --- | --- | --- | --- |
| FR-01 | 웹캠 스트림 입력 | WebCamTexture로 실시간 카메라 프레임 수신 | config의 카메라 인덱스로 장치 선택, 프레임 갱신 확인 |
| FR-02 | 해상도 다운샘플링 | 원본(예: 1920x1080)을 연산용 저해상도(64x36 또는 16x16 그리드)로 축소 | 전체 파이프라인 60FPS 이상 유지 |
| FR-03 | 차영상 모션 감지 | 이전/현재 프레임 ΔLuminance 계산, Threshold 초과 셀의 정규화 좌표(X, Y: 0.0\~1.0)와 강도(Intensity) 추출 | 정지 시 감지 0건, 움직임 위치와 좌표 일치 |

### STEP 2. Transport — UDP 네트워크 송수신

| ID | 기능 | 요구사항 | 수용 기준 |
| --- | --- | --- | --- |
| FR-04 | 센서 패킷 직렬화 | 감지 좌표 리스트를 경량 JSON 또는 바이트 배열로 패킹 | 수신 측에서 역직렬화 결과가 원본과 동일 |
| FR-05 | UDP 비동기 송수신 | UdpClient로 센서 스레드 송신, 렌더러 수신 스레드 → ConcurrentQueue → 메인 스레드 전달 | 메인 스레드 블로킹 없음, 종료 시 소켓·스레드 정상 해제 |

### STEP 3. Sink — 미디어 월 렌더링 & 상태 머신

| ID | 기능 | 요구사항 | 수용 기준 |
| --- | --- | --- | --- |
| FR-06 | 전시 상태 머신(FSM) | 5초 이상 모션 패킷 없으면 Idle(안내 문구 + 잔잔한 배경), 패킷 수신 즉시 Active | Idle ↔ Active 자동 전환 확인 |
| FR-07 | 비주얼 피드백 | 수신 (X, Y)를 월드 좌표로 변환해 파티클 Emit 또는 셰이더 물결/글로우 파라미터 갱신 | 움직인 위치에 이펙트 표시 |

### STEP 4. Field Utility — 현장 설치 & 엔지니어 디버그

| ID | 기능 | 요구사항 | 수용 기준 |
| --- | --- | --- | --- |
| FR-08 | F1 엔지니어 오버레이 UI | F1로 좌측 디버그 패널 토글 | 아래 항목 전부 표시·동작 |
| FR-09 | 외부 환경설정 연동 | StreamingAssets/config.json에 카메라 인덱스, UDP IP/Port, 기본 감도 저장·로드 | 재빌드 없이 값 변경 반영 |

FR-08 디버그 패널 구성:

- 웹캠 원본 뷰 / 모션 차분 흑백 뷰 / 감지 타겟 좌표 그리드 뷰
- 실시간 UDP 수신 패킷 로그 및 현재 FPS
- 조도 노이즈 제어용 Threshold 슬라이더
- 카메라 좌우 반전(Mirror) 토글

## 5. 비기능 요구사항 및 데이터 규격

성능 목표는 60FPS 이상과 프레임 루프 내 GC 할당 0이며, 패킷과 설정 파일은 아래 형식을 따른다.

| 분류 | 요구사항 |
| --- | --- |
| 성능 | 렌더링 60FPS 이상, 픽셀 연산은 저해상도 그리드에서만 수행 |
| 메모리 | 링 버퍼·배열 재사용으로 매 프레임 할당 없음(Zero-GC) |
| 스레드 안전 | 수신 스레드는 Unity API 호출 금지, ConcurrentQueue로만 메인 스레드에 전달 |
| 모듈 분리 | Sensor / Network / Render / UI는 UDP 패킷으로만 결합, 실제 LiDAR로 센서부만 교체 가능 |
| 현장 대응 | 카메라·네트워크·감도는 재빌드 없이 config.json 또는 F1 패널에서 변경 |
| 안정성 | 패킷 유실·손상 시 예외 없이 무시, 카메라 미연결 시 안내 후 대기 |

### UDP 패킷 (JSON 형식)

```json
{"points":[{"x":0.5, "y":0.2, "v":0.8}]}
```

| 필드 | 타입 | 범위 | 의미 |
| --- | --- | --- | --- |
| x | float | 0.0\~1.0 | 정규화 가로 좌표 |
| y | float | 0.0\~1.0 | 정규화 세로 좌표 |
| v | float | 0.0\~1.0 | 모션 강도(Intensity) |

### StreamingAssets/config.json (예시)

```json
{
  "cameraIndex": 0,
  "udpIp": "127.0.0.1",
  "udpPort": 9000,
  "threshold": 0.15,
  "mirror": true
}
```

포트 9000, threshold 0.15, mirror 기본값은 예시이며 구현 시 확정한다.

## 6. 48시간 개발 일정 및 Git 전략 (제안)

Day 1에 센서→UDP→수신까지 관통시키고, Day 2에 렌더링·FSM·현장 도구를 올린다. 시간 배분은 제안이며 조정 가능하다.

| 구간 | 작업 | 브랜치 | 완료 기준 |
| --- | --- | --- | --- |
| Day 1 오전 | 프로젝트 세팅, config.json 로더, WebCamTexture 입력·다운샘플링 | feature/sensor | 그리드 픽셀 배열 추출 (FR-01, 02, 09) |
| Day 1 오후 | 차영상 감지, 패킷 직렬화 | feature/sensor | 좌표 리스트 출력 (FR-03, 04) |
| Day 1 저녁 | UDP 송신/수신 스레드, ConcurrentQueue | feature/network | 로그로 수신 확인 (FR-05) |
| Day 2 오전 | 파티클·물결 셰이더, 좌표 매핑 | feature/render | 움직임 위치에 이펙트 (FR-07) |
| Day 2 오후 | Idle/Active FSM, F1 디버그 패널 | feature/render, feature/ui | 자동 전환 + 패널 동작 (FR-06, 08) |
| Day 2 저녁 | 통합, 빌드, 시연 영상, 문서 정리 | main | 빌드본에서 60FPS 유지 |

Git 규칙:

- `main`은 항상 빌드 가능 상태 유지, 기능 브랜치는 완료 기준 충족 후 병합
- 브랜치: `feature/sensor`, `feature/network`, `feature/render`, `feature/ui`
- 커밋 메시지에 FR ID 표기 (예: `feat(sensor): FR-03 frame diff detection`)
- Unity용 `.gitignore` (Library/, Temp/, Logs/ 제외) 적용

## 7. 추후 개발 로드맵 (v2.0)

미충족 요구조건 2개는 현재 UDP 좌표 스트림 위에 모듈을 추가하는 방식으로 확장한다.

### v2.1 디바이스(로봇/모터) 제어 시뮬레이터

- UDP로 수신한 관람객 중심 좌표(Centroid)로 목표 Pan/Tilt 회전 각도를 산출하는 Controller 모듈
- 인게임 상단에 3D Moving Light를 배치해 관람객 실시간 추적
- RS-232/485 시리얼 패킷 규격(STX / Command / Data / Checksum / ETX) 생성 Logger

### v2.2 외부 환경 데이터 연동 REST API 파이프라인

- UnityWebRequest 기반 비동기 REST 클라이언트로 기상청/OpenWeatherMap API 연동
- 실시간 날씨·시간대 파싱 → 미디어 월 컴러 팔레트와 파티클 물리 속성(비, 눈, 바람)에 자동 반영
- 데이터 드리븐(Data-Driven) 연출 파이프라인 완성
