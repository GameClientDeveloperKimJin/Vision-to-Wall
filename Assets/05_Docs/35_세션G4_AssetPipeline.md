# 35. 세션 G4 — Asset Pipeline

- 담당자: **G4 Asset Pipeline**
- 브랜치: `feature/game-assets`
- 담당: AR-01 VARCO 3D 에셋, AR-02 사운드 임포트
- 아트 기준: `Assets/04_Docs/eye_of_the_future_art_reference.png`
- 수정 가능: `Assets/07_Models/**`, `Assets/08_Audio/**`, `Assets/03_Prefabs/Game/**` (`Visual` 자식만), 이 문서
- 선행 조건: G3 `main` 병합 + 사용자의 아트 컨셉 전달 (2026-11-06 수령)
- G5와 교대로 진행: VARCO 승인과 생성 구간은 작업 폴더와 Unity를 쓰지 않음. 다운로드는 프로젝트 밖 임시 폴더에 두고, **기획 세션이 폴더를 넘겨준 뒤** 임포트. 상세는 [36번 문서](36_세션G5_Presentation.md)
- 제작 도구: **VARCO AI FLOW** (사용자 결정)

## 1. 목표

임시 도형을 VARCO AI FLOW로 만든 모델로 교체하고, 사운드 4종을 프로젝트에 넣는다. 레이저, 보호막, 배경, HUD는 모델이 아니라 G5·G6가 쉐이더와 UI로 만든다.

**비용 승인 전에는 생성하지 않는다.** 레퍼런스 이미지를 VARCO에 업로드해 입력으로 쓰려면 함께 승인받는다.

## 2. 에셋 목록

| ID | 내용 | 프롬프트 방향 |
|---|---|---|
| AR-01-1 | 메인 보스 (기계 눈 외피) | 어두운 금속의 동심원 링이 끊어진 호(arc)로 겹친 SF 기계 눈. 정면 중앙은 **비워 둠** (홍채는 G5 쉐이더) |
| AR-01-3 | 플레이어 아바타 | 뒷모습이 자연스러운 근육질 인간형, 차렷에 가까운 서 있는 자세. 표면 디테일 불필요 (G5가 점으로 렌더링) |

- AR-01-2 탄막 파편은 **생성하지 않음** (레퍼런스에 없음, 크레딧 절약). `Projectile.prefab`은 G5가 빛 탄으로 교체
- 링을 부위별로 분리해 생성할 수 있으면 회전 연출이 가능하므로 비용과 함께 보고. 안 되면 한 덩어리로 진행
- AR-01-3: Flow의 리깅·애니메이션(`get_animation_catalog`) 가능 여부와 비용을 보고. 대기 애니메이션 정도만 고려하며, 필수 아님

| ID | 내용 | 파일명 |
|---|---|---|
| AR-02-1 | 전투 BGM (사이버펑크 로우 베이스 루프) | `08_Audio/bgm_combat.wav` |
| AR-02-2 | 보스 각성·레이저 발사음 | `08_Audio/sfx_awaken_fire.wav` |
| AR-02-3 | 패링 성공음 (금속성 타격) | `08_Audio/sfx_parry.wav` |
| AR-02-4 | 보스 피격·파괴 폭발음 | `08_Audio/sfx_explosion.wav` |

## 3. 진행 방식

### VARCO AI FLOW (MCP 사용)
0. `get_node_catalog`, `get_flow_graph`로 사용 가능한 노드와 현재 Flow 확인. 입력 노드 → 생성 노드 순으로 구성 (`create_node`, `connect_edge`, `set_node_input`)
1. `get_account_and_pricing`으로 잔여 크레딧과 예상 비용 확인
2. **사용자에게 프롬프트와 비용을 보여주고 승인받은 뒤 생성** (`run_flow`는 크레딧 소모)
3. 모델 생성은 건당 수 분~10분 이상 걸림. `get_flow_graph`로 상태 확인
4. `get_output_downloads`로 다운로드. 형식 확인: Unity가 바로 읽는 FBX 우선. GLB만 있으면 glTF 패키지가 필요하므로 기획 세션에 요청
5. 내려받은 파일은 새 폴더에 두고 확인한 뒤 `07_Models/`로 이동

### VARCO SOUND (사용자 작업)
MVP 계획 기준으로 이 환경에 VARCO SOUND 도구가 연결되어 있지 않다. Flow 카탈로그에 사운드 노드가 있으면 사용자에게 알리고 방식을 확인한다. 사용자가 웹에서 생성해 위 파일명으로 `Assets/08_Audio/`에 넣는다. 세션은 위 표를 프롬프트와 함께 사용자에게 전달하고, 들어온 파일의 임포트 설정만 한다.

### 임포트 기준
- 보스: 삼각형 10만 개 이하를 목표로 하고, 넘으면 사용자에게 보고. 텍스처 2K 이하
- 아바타: 정점 수를 보고 (G5가 점으로 표시하므로 정점 분포가 몸 전체에 고를수록 좋음). 텍스처 불필요
- URP Lit 머티리얼로 변환 (핑크 머티리얼 없을 것). 링 가장자리에 약한 Emission
- 보스 정면이 프리팹 `+Z`를 향하도록 `Visual`에서 회전 보정
- BGM: Loop, Streaming / SFX: Decompress On Load
- 프리팹 루트와 스크립트 참조는 변경 금지. `Visual` 자식만 교체
- 아바타: 씬이나 프리팹에 배치하지 않고 `07_Models/`까지만 납품. `Read/Write Enabled` 켜기 (G5가 정점을 읽음). 뒷모습이 `+Z`를 향하는 회전 보정값 기록

## 4. 체크리스트

### 구현 — 담당: G4 Asset Pipeline
- [x] 사용자 아트 컨셉 수령
- [x] 프롬프트 확정
- [x] 비용 확인, 사용자 승인
- [x] AR-01-1 생성·임포트, `BossEye.prefab` `Visual` 교체
- [x] AR-01-3 생성·임포트, `07_Models/`에 납품
- [ ] 사운드 프롬프트 전달, 파일 수령, 임포트 설정 (프롬프트 전달 완료, **파일 미수령**. `08_Audio/`는 나중에 추가)
- [x] 생성에 쓴 프롬프트와 소요 크레딧을 작업 로그에 기록 (포트폴리오 자료). 크레딧은 추정치, 실제 차감액 미확인

### 자동 검증 — 담당: G4 Asset Pipeline
- [x] 콘솔 에러 0, 누락 참조 0, 핑크 머티리얼 0 (첫 Play에서 1회 에러 있었음. 작업 로그 참고)
- [x] 보스 삼각형 수, 아바타 정점 수, 텍스처 크기 보고
- [x] Main 씬 전투 중 fps 60 이상 유지 확인 (360 fps, 에디터 기준)
- [x] 기존 EditMode 테스트 전부 통과 (174/174)

### 사람 검증 — 담당: 사용자
- [x] 보스 외피 형태와 동작, 정면이 플레이어를 향함 (2026-11-07 통과. 레퍼런스와의 어울림은 G5 홍채 부착 후 재확인)
- [x] 아바타 형태가 레퍼런스와 어울림 (2026-11-07 통과. 점 표현은 G5에서 확인)
- [ ] 사운드 4종 품질 확인

## 5. 통합 인계

프리팹 참조가 유지되므로 기획 세션의 씬 작업은 없음. 모델 정면 보정값과 Emission 프로퍼티 이름을 G5용으로 작업 로그에 남길 것.

## 6. 제외 범위
홍채 쉐이더, 각성 안광, 레이저, 보호막, 배경, 아바타 점 렌더링, 사운드 재생 코드(G5). HUD(G6)

## 작업 로그
### 2026-11-06~07 모델 생성·임포트

#### 납품 수치 (Unity 임포트 후 재확인)

| | 보스 `07_Models/Boss/BossEye_Shell.fbx` | 아바타 `07_Models/Avatar/PlayerAvatar.fbx` |
|---|---|---|
| 삼각형 | 80,000 | 30,000 |
| 정점 | 69,070 | 18,556 |
| 텍스처 | 2048² 3장 (BaseColor, Normal, MetallicSmoothness) + Emission 마스크 2048² | 없음 |
| Read/Write | 꺼짐 | **켜짐** |
| 메시 크기 | 지름 약 1.0, 두께 약 0.11 | 키 약 1.0, 피벗은 몸 중앙 (발 y≈-0.5) |

폴더 전체 41MB. Git LFS 설정이 없어 일반 파일로 커밋함 (FBX 최대 21.7MB).

#### 보스 (G5 인계)
- 구조: `Visual/Shell` (메시), `Visual/Pupil` (임시). `Ball`은 삭제. 프리팹 루트와 `BossLookAt`은 변경 없음
- `Shell` 변환: 위치 `0,0,0`, 회전 `0,0,45`, 크기 `2`. 메시 +Z가 정면이고, 45°는 바깥 호를 레퍼런스처럼 대각선에 놓는 보정
- **정면 중앙: 프리팹 로컬 `(0, 0, 0)`, 구멍 반경 약 `0.36`**. 외피 두께 범위는 로컬 z 약 ±0.11
- `Pupil`은 **삭제하지 않음**: 구멍 자리로 이동 (위치 `0,0,0`, 크기 `0.72`). G5 개발 부트스트랩이 `Visual/Pupil`을 찾아 끄는 구조라 유지
- 홍채 부착값 변경 필요: 현재 부트스트랩 값 (`0,0,1.01`, 크기 2)은 임시 구체 기준이라 외피 앞으로 튀어나옴. 구멍 지름 0.72에 맞춰 z 약 0, 크기 약 0.75부터 조정
- 머티리얼: `07_Models/Boss/BossShell.mat` (URP Lit, 양면)
- Emission 프로퍼티: `_EmissionColor` = `(0.25, 0.45, 0.8)`, `_EmissionMap` = `BossShell_EmissionMask.png`, 키워드 `_EMISSION`. 마스크는 Normal 맵의 급한 경사로 만든 근사값이며 면적의 약 2.7%
- AO는 제외: 원본 값이 평균 0.996으로 효과 없음
- 링 분리 불가: 삼각형 80,000개 중 79,818개가 한 덩어리. 회전 연출은 보스 전체 단위로만 가능
- 뒷면은 AI 추정이며 앞면과 비슷한 형태

#### 아바타 (G5 인계)
- **등이 +Z를 향하는 회전: 오브젝트 회전 `(270, 180, 0)`** (임포트 기본 `(270, 0, 0)`에 Y 180°를 더한 값). 기본값에서는 얼굴이 +Z. 렌더링으로 확인
- 메시 좌표는 FBX 축 그대로 (키가 로컬 Z). 표면 샘플링 결과에 위 회전을 적용할 것
- 씬·프리팹에 배치하지 않음. 머티리얼 임포트를 꺼서 그대로 렌더링하면 머티리얼이 없음. G5가 자체 머티리얼 지정
- 높이 10구간 정점 수 (발→머리): 2342, 993, 1199, 1357, 3756, 1946, 2378, 2051, 1357, 1177. 손·골반 높이에 몰리고 정강이가 성김. 점 밀도를 고르게 하려면 정점 직접 사용보다 표면 샘플링이 적합
- 팔이 몸에서 떨어져 있고 메시는 한 덩어리. 얼굴은 눈 주변이 뭉개졌지만 앞면이라 게임에서 보이지 않음

#### 검증
- EditMode 174/174. 누락 참조 0, 핑크 머티리얼 0 (`BossEye`, `BossCombatRig`)
- Main 씬 전투 구간 7초 평균 **360 fps** (에디터, 포커스 상태, 스크린샷 없음). 빌드 수치는 아님. 전투는 코드로 `Awaken`을 강제해 진입했고 플레이어 입력 없이 측정
- 정면 확인: Play 중 `Shell` 정면이 월드 -Z, 즉 카메라 쪽. 보스가 플레이어 쪽으로 회전함

#### 미해결·주의
- **첫 Play에서 콘솔 에러 4건 1회 발생**: `PlayerLoop internal function has been called recursively`, `Missing Profiler.EndSample` 3건. 첫 MCP 스크린샷 무렵에 발생했으나 원인은 확정 못 함. 이후 Play 전투 구간과 스크린샷에서는 재현 안 됨
- MCP `manage_camera screenshot` 직후 에디터가 일시정지됨 (2회 관찰). 그동안 프레임이 멈추므로 측정 전에 `isPaused` 확인 필요
- 전투 중 화면 전체가 밝고 희뿌옇게 보임. `Shell`을 꺼도 그대로라 모델과 무관. G4 범위 밖이라 조사 안 함

#### 생성 기록 (VARCO AI FLOW, Flow `InteractiveGame`)
보스 이미지 프롬프트. 참조 이미지로 레퍼런스의 보스 영역 크롭 (200×150 JPEG) 사용:
```
Front orthographic view of a sci-fi mechanical eye housing, centered on a plain neutral gray background. Concentric dark gunmetal rings made of broken arc segments, layered at different depths, with panel lines and small mechanical details. The center is a completely empty circular hole: no iris, no pupil, no lens, no glow. Hard-surface game asset, matte dark metal, no text, no lights.
```
아바타 이미지 프롬프트. 참조 이미지 없음:
```
Full-body back view of a muscular athletic human figure, standing straight in a relaxed attention pose, arms slightly away from the torso, legs slightly apart, facing away from the camera. Plain neutral gray background. Smooth bald mannequin-like body with no clothing details, no hair, no accessories. Clear anatomical silhouette, even lighting.
```

| 단계 | 설정 | 크레딧 (실행 시 추정치) |
|---|---|---|
| 보스 이미지 2장 | `gpt-image-2.5-flare`, 1:1 | 100 |
| 보스 3D | 80,000폴리, tri, PBR, 2K, PRIVATE | 200 |
| 아바타 이미지 2장 | `gpt-image-2.5-flare`, 3:4 (**1장 실패**) | 100 (실패분 미차감이면 50) |
| 아바타 3D | 30,000폴리, tri, 텍스처 없음, PRIVATE | 100 |
| **합계** | | **450~500** |

- API로 잔여 크레딧을 조회할 수 없어 위 값은 모두 `run_flow` 응답의 추정치. 실제 차감액은 VARCO 웹에서 확인 필요
- 요금표의 이미지 단가 20 P는 기본 모델에 맞지 않았음 (실제 장당 50 P). 승인 전 안내한 40 P보다 60 P 초과했고 사용자에게 보고함
- 리깅·애니메이션은 생성하지 않음. 요금표상 0 P지만 T-pose 생성이 전제이고 필수가 아님
- 출력 형식: FBX, GLB, OBJ, STL 선택 가능. glTF 패키지 불필요

#### 사람 검증 피드백 (2026-11-07)
- 보스 동작은 문제없음. 아바타 형태는 잘 묘사됨
- 보스가 텍스처 없이 밋밋해 보임: 텍스처는 적용됐으나 어두운 회색이라 단색처럼 보임 (BaseColor 평균 약 0.23, 채도 약 0.007). G5 홍채 부착 후 재확인하고, 그래도 밋밋하면 `BossShell.mat`의 Emission이나 색을 조정 (재생성 불필요)
- 가운데 눈이 없고 인게임에 플레이어가 안 보임: G5 담당이며 G5 병합 전이라 정상
- 링 회전과 아바타 애니메이션은 담당 없음. G5 통합 화면을 본 뒤 사용자가 결정

#### 사운드
Flow 카탈로그에 사운드 노드 없음. 사용자에게 프롬프트를 전달했고 파일은 미수령.

| 파일 | 프롬프트 |
|---|---|
| `bgm_combat.wav` | Cyberpunk combat music loop, low heavy bass, driving pulse, tense and dark, seamless loop, no vocals |
| `sfx_awaken_fire.wav` | Giant machine eye awakening, rising energy charge into a sharp sci-fi laser blast |
| `sfx_parry.wav` | Short sharp metallic impact, energy shield deflecting a hit, bright ringing clang |
| `sfx_explosion.wav` | Heavy mechanical explosion, deep boom with metal debris and fading rumble |
