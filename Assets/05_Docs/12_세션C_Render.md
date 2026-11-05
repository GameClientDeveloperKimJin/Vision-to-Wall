# 12. 세션 C — Render (미디어 월 & 상태 머신)

- 담당자: **세션 C**
- 브랜치: `feature/render`
- 담당 FR: FR-06 전시 상태 머신 / FR-07 비주얼 피드백
- 수정 가능 폴더: `Assets/02_Scripts/Render/` (asmdef `VisionToWall.Render`, Core + Unity.TextMeshPro 참조), `Assets/03_Prefabs/Render/`
- 선행 조건: Phase 0 완료 ([01_Core_공통계약](01_Core_공통계약.md))

## 1. 납품물

| 파일 | 역할 |
|---|---|
| `ExhibitionStateMachine.cs` | Idle ↔ Active FSM. `IExhibitionStateSource` 구현 |
| `MediaWallController.cs` | 포인트 → 월드 좌표 변환, 파티클 Emit, 물결 파라미터 갱신 |
| `RippleBuffer.cs` | 순수 C#. 물결 링 버퍼 + 근접 중복 억제 (단위 테스트 대상) |
| `Shaders/RippleWall.shader` | URP Unlit HLSL. 배경 그라디언트 + 물결 왜곡 + 글로우 |
| `AttractModeView.cs` | Idle 연출: 안내 문구, 잔잔한 배경 파티클, 페이드 |
| `03_Prefabs/Render/MediaWall.prefab` | 카메라 + 월 Quad + 파티클 + Attract UI 묶음 |
| `Dev/MockPointSource.cs` + `Dev/RenderDev.unity` | 마우스/자동 패턴으로 가짜 포인트 공급 |

## 2. 공개 API (App이 호출)

```csharp
namespace VisionToWall.Render
{
    public class ExhibitionStateMachine : MonoBehaviour, IExhibitionStateSource
    {
        public void Initialize(AppConfig config, IPointSource source);
    }

    public class MediaWallController : MonoBehaviour
    {
        public void Initialize(IPointSource source, IExhibitionStateSource state);
    }
}
```

## 3. 처리 설계

### 3.1 FR-06 상태 머신
| 현재 | 조건 | 다음 | 연출 |
|---|---|---|---|
| Idle | `FrameReceived` 중 `Count > 0` | Active | 즉시 전환, 안내 문구 0.3초 페이드아웃 |
| Active | `Count > 0` 프레임 없이 `idleTimeoutSec`(5초) 경과 | Idle | 안내 문구 1초 페이드인, 배경 잔잔하게 |

- 판정 시간은 `Time.unscaledTime`.
- 시작 상태 = Idle. 패킷 자체가 끊겨도(네트워크 단절) 동일하게 Idle로.
- 전환 시 `StateChanged` 이벤트 발생 (UI가 표시).

### 3.2 FR-07 좌표 변환
- 월 = 직교(Orthographic) 카메라 화면 전체를 덮는 Quad.
- `world = Vector2.Lerp(wallMin, wallMax, (x, y))`. `wallMin/Max`는 시작 시·해상도 변경 시 1회 계산.

### 3.3 FR-07 파티클
- `ParticleSystem.Emit(EmitParams, 1)` 사용 (`EmitParams`는 필드로 캐시, 할당 0).
- 크기·밝기 = `v`에 비례. 프레임당 Emit 상한 64 (포인트가 많으면 균등 간격 샘플링).
- Idle 중에도 배경 파티클은 저속 유지, Active는 별도 시스템.

### 3.4 FR-07 물결 셰이더
- 셰이더 파라미터: `_Ripples[32]` (`float4`: x, y, 시작시각, 강도), `_RippleCount`, `_RippleSpeed`, `_RippleDecay`, `_GlowColor`.
- C#: `Vector4[32]`를 미리 할당, `material.SetVectorArray` 매 프레임 1회.
- `RippleBuffer`: 같은 위치(거리 < 0.05) 0.15초 이내 재생성 억제 → 60Hz 패킷 폭주 방지. 32개 초과 시 가장 오래된 것 덮어씀.
- Active 강도 ↔ Idle 강도는 0.5초 보간.

### 3.4.1 실측 입력 특성 (2026-11-05, 웹캠 C270 640×480)
- 패킷 약 **25 Hz** (렌더 60+ fps보다 느림) → 패킷 사이 프레임은 직전 값 유지 또는 보간. 패킷 수신 시점에만 이펙트가 튀지 않게 할 것
- 손을 흔드는 중에도 포인트 수가 프레임마다 크게 오르내림 (방향 전환 순간 ≈ 0). 강도·방출량은 **시간 평활**(지수 감쇠, 예: 0.2~0.3초) 적용
- 첫 프레임 등 순간적으로 포인트 100~200개가 몰릴 수 있음 → 프레임당 Emit 상한(64) 유지

### 3.5 성능
- 목표: 1920×1080 전체화면 60FPS 이상.
- `Update`/콜백 GC 0. 문자열 갱신은 상태 전환 시에만.

## 4. 체크리스트

### 구현 — 담당: 세션 C
- [x] FR-06 `ExhibitionStateMachine` Idle/Active + `idleTimeoutSec` 반영
- [x] FR-06 `StateChanged` 이벤트, `IExhibitionStateSource` 구현
- [x] FR-06 `AttractModeView` 안내 문구("손을 흔들어 보세요") + 페이드
- [x] FR-07 정규화 좌표 → 월드 좌표 변환
- [x] FR-07 `ParticleSystem.Emit(EmitParams)` + 프레임당 상한
- [x] FR-07 `RippleWall.shader` (URP Unlit, 물결 + 글로우)
- [x] FR-07 `RippleBuffer` 링 버퍼 + 근접 억제
- [x] FR-07 EditMode 테스트: `RippleBuffer` 근접 억제, 32개 초과 덮어쓰기
- [x] `MediaWall.prefab` 구성
- [x] `Dev/MockPointSource` (마우스 드래그 + 5초 ON / 7초 OFF 자동 패턴)
- [x] `Dev/RenderDev.unity`

### 검증 — 담당: 사용자 (Unity 에디터)
- [x] RenderDev: 마우스 위치에 파티클 + 물결 발생
- [x] 입력 멈추고 5초 후 Idle 전환, 입력 시 즉시 Active
- [ ] 세션 B `FakeMotionSender` 연결 시에도 동일 동작 (통합 전 선택 검증)
- [x] Game 뷰 1920×1080, Stats 창 60FPS 이상
- [x] Profiler: GC Alloc 0 B/frame

## 5. 제외 범위
- F1 패널 (세션 D), UDP (세션 B)
- 날씨 테마 연동 — v2.2 범위. 단, 색상 값은 `[SerializeField]`로 빼둘 것 (추후 데이터 드리븐 확장 대비)

## 작업 로그
- 2026-11-06 (세션 C) 구현 완료. EditMode `VisionToWall.Render.Tests` 5/5 통과, 셰이더 컴파일 에러 0
  - 파일: `Render/` ExhibitionStateMachine · MediaWallController · RippleBuffer · AttractModeView · `Shaders/RippleWall.shader` · `Shaders/GlowParticle.shader`(텍스처 없는 가산 파티클), `Render/Dev/` MockPointSource · RenderDevBootstrap · RenderDev.unity, `03_Prefabs/Render/MediaWall.prefab` + `Materials/`
  - MediaWall.prefab 루트에 `ExhibitionStateMachine` + `MediaWallController` 함께 있음 → App은 프리팹 1개 배치 후 `fsm.Initialize(config, receiver); wall.Initialize(receiver, fsm);` (AttractModeView는 wall이 Bind). 프리팹에 자체 직교 카메라 포함 → Main 씬 기존 Main Camera는 제거하거나 비활성
  - 3.4.1 반영: 패킷(≈25 Hz)은 상태만 갱신(보유 포인트 · 목표 활동도 · 물결 추가), 방출량·셰이더 강도는 매 렌더 프레임 지수 평활(τ 0.25 s) 활동도로 구동. count=0 프레임엔 직전 비어있지 않은 포인트를 유지해 제자리에서 페이드. 패킷 0.25 s 끊기면 목표 활동도 0
  - 물결: 패킷당 강도 상위 1개(`ripplesPerPacket`)만 추가 + RippleBuffer 근접 억제. 셰이더 글로우·왜곡은 소프트 포화(겹침 시 화면 하얗게 뜨지 않게)
  - asmdef: `Unity.InputSystem` 참조 추가 (activeInputHandler = Input System only, MockPointSource 마우스용)
  - 한글 폰트: 프로젝트에 Hangul TMP 폰트 없음 → `AttractModeView`가 런타임에 OS 폰트(Malgun Gothic)로 동적 TMP 폰트 생성. 배포 PC가 Windows가 아니면 `font` 필드에 폰트 에셋 지정 필요
  - Linear 색공간이라 어두운 배경에 더한 작은 값이 크게 보임 → 배경 패턴 0.012로 낮춤. 색상 전부 `[SerializeField]`(MediaWallController Colors 헤더)
  - 에디터 비포커스 상태에선 Play 프레임이 멈춰 Idle 전환·FPS·GC는 자동 확인 못 함 → 아래 사용자 검증 필요
  - MockPointSource: 좌클릭 드래그 = 커서 위치 3×3 블롭, 미입력 시 5초 손 흔들기 / 7초 빈 프레임 자동 패턴, 25 Hz · flicker 0.3으로 실측 특성 흉내
- 2026-11-06 검증: 1·2·4번 사용자 확인 통과. 3번(FakeMotionSender)은 AppBootstrap에 Render 연결 후 통합 단계에서 확인
  - 5번 GC: 세션 C가 Play 중 `ProfilerRecorder("GC Allocation In Frame Count")`로 측정. 각 경로 600회 호출 결과 전부 측정 오버헤드(2건/64B)와 같음 → 호출당 0 B
    - 측정 경로: wall.OnFrameReceived(포인트 200개 / 0개), fsm.OnFrameReceived, wall.Update(Emit 64개 + SetVectorArray), fsm.Update, AttractModeView.Update, MockPointSource.Update, 전체 사이클
    - 측정 방식 검증: `new object()` 600회 → 600건 / 9,600B로 정상 검출
    - 예외: 상태 전환 프레임의 RenderDevBootstrap 로그 문자열 (Dev 전용, 전환 시에만)
