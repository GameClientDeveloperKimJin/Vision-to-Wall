# 54. 세션 R4 — Pose Avatar

- 담당자: **R4 Pose Avatar**
- 브랜치: `feature/roadmap-pose-avatar`
- 담당: 전투 아바타(점 인간형)가 실제 팔다리 동작을 따라 함
- 수정 가능: `Assets/02_Scripts/Game/Presentation/Avatar/**`, `Assets/02_Scripts/Game/Presentation/PlayerAvatarView.cs`, `Assets/02_Scripts/Game/Presentation/Shaders/AvatarPoints.shader`, 각 `Tests/`·`Dev/` 하위 `Avatar` 폴더, `Assets/07_Models/Avatar/**`, `Assets/03_Prefabs/Game/PresentationRig.prefab`, 이 문서
- 선행 조건: R3, RT `main` 병합 (`IPoseSource`, 수신기, RT 정답 뼈 회전 데이터 사용)
- 사용자 결정: 포함. 리깅은 사용자가 Mixamo로 완료 (`PlayerAvatar_Rig.fbx`), VARCO 비용 없음

## 1. 목표

지금의 점 아바타는 한 덩어리로 위치만 따라간다. 리깅된 모델로 바꾸고 관절 월드 좌표로 뼈를 돌려, 레퍼런스처럼 팔을 뻗는 동작이 화면 앞 아바타에 나타나게 한다. **판정은 바꾸지 않는다** (회피·피격은 여전히 몸 중심 1점과 `hitRadius`).

## 2. 접근

| 단계 | 내용 |
|---|---|
| 1. 리깅 | **완료**: 사용자가 Mixamo 자동 리깅으로 `Assets/07_Models/Avatar/PlayerAvatar_Rig.fbx` 제작 (2026-11-08, 뼈 28개, T-pose). VARCO 리깅 불필요. 임포트 설정은 RT가 Humanoid로 바꿈 |
| 2. 임포트 | Unity Humanoid 또는 Generic. 뼈 이름과 T/A 포즈 기록. Read/Write 유지 |
| 3. 점 렌더링 | 점 샘플링 시 삼각형 정점의 뼈 가중치도 함께 보간해 **점 메시 자체를 스키닝 메시로 만든다** (`SkinnedMeshRenderer` + 기존 `AvatarPoints` 쉐이더). 쉐이더는 스키닝 이후 정점을 받으므로 등 표현(척추선, 조명)은 유지 |
| 4. 포즈 적용 | 관절 월드 좌표(`IPoseSource`)로 상완·전완·대퇴·하퇴·척추·목 방향을 계산해 뼈 회전 (2뼈 방향 맞춤, 필요하면 간단한 IK) |

### 좌표 주의
- 플레이어는 카메라를 보고 서 있고 아바타는 **등을 보인다**. 미러 적용된 월드 x를 그대로 쓰면 화면 같은 쪽 팔이 움직여야 함. 좌우 라벨은 몸 기준이므로 플레이어 오른팔 = 아바타 오른팔 = 화면 오른쪽 (아바타가 등을 보이므로)
- 월드 z(깊이)는 MediaPipe 추정이라 흔들림이 큼. 평활하고, 팔이 몸을 뚫는 각도는 제한
- 보스 쪽으로 돌리는 회전(`maxFacingYawDeg`)과 포즈 회전의 적용 순서를 정할 것

## 3. 처리 설계

- 관절 미수신, webcam 모드, visibility 낮은 관절: 기본 자세로 0.3초에 걸쳐 복귀
- 평활은 R3 `PoseSmoother` 재사용 (R3 코드 수정 필요하면 기획 세션에 요청)
- 아바타 위치·크기는 지금처럼 판정 위치와 `hitRadius`로 결정 (`TorsoWorldWidth` 유지)
- 성능: 스키닝 점 30,000개. 에디터와 빌드 기준 fps를 R4 전후로 비교. 60 미만이면 점 수 조정
- 프레임 할당 0 (뼈 회전 계산 포함)
- 결정 필요 시 기획 세션에 보고: 아바타가 보스를 가리는 문제([40번](40_게임_통합_검증.md) 미확인 항목)를 포즈와 함께 해결할지

## 4. 체크리스트

### 구현 — 담당: R4 Pose Avatar
- [x] 리깅 모델 (`PlayerAvatar_Rig.fbx`, Mixamo, 사용자 제작)
- [ ] 뼈 가중치를 포함한 점 메시 생성 (스키닝)
- [ ] 관절 → 뼈 회전, 평활, 기본 자세 복귀
- [ ] 보스 방향 회전과 결합
- [ ] Dev 씬: `fake_sender.py` 관절 패턴과 실제 손 센서

### 자동 검증 — 담당: R4 Pose Avatar
- [ ] EditMode: 뼈 가중치 보간(합 1), 관절 → 뼈 방향 계산, 좌우 매핑, 미수신 복귀
- [ ] 기존 EditMode 전체 통과 (`FacingYaw_TurnsTowardScreenCenter` 포함)
- [ ] 프레임 할당 0, fps 전후 비교
- [ ] 생성 프롬프트와 소요 크레딧을 작업 로그에 기록

### 사람 검증 — 담당: 사용자
- [ ] 팔을 뻗으면 아바타가 같은 쪽 팔을 뻗음 (등 뒤에서 본 모습으로 자연스러움)
- [ ] 걷거나 몸을 기울여도 점이 찢어지거나 튀지 않음
- [ ] 등 표현과 밝기가 이전보다 나빠지지 않음

## 5. 통합 인계

기획 세션 작업: `PlayerAvatarView`에 `IPoseSource` 연결 (Initialize 시그니처가 바뀌면 `CombatPresenter`/`AppBootstrap` 연결 수정). 모델 교체로 프리팹 참조가 바뀌면 적을 것.

## 6. 제외 범위

판정 변경, 손가락, 얼굴 표정, 미니 캐릭터(R3)

## 작업 로그
- (R4 Pose Avatar가 기록)
