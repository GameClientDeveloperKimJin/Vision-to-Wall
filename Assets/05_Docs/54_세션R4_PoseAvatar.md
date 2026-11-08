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
- **RT 실측 깊이 편향** ([56번](56_세션RT_PoseTestRig.md) 작업 로그): 어깨 z가 실제보다 15~22 cm 카메라 쪽, 발목 +9~+21 cm. 3D 각도 오차 팔꿈치 중앙값 17.5~23.6°, 무릎 26.0~30.6° (정면 x·y 각도는 1~9°). 실제 R2 녹화도 어깨 z −28 cm 경향. z를 그대로 쓰면 팔과 다리가 앞으로 꺾여 보이므로 z 감쇠, 범위 제한, 또는 x·y 우선 방식을 쓰고 **RT 데이터(`PoseTest/Tests/Data/<clip>_detected.csv` 입력 → `_truth.csv` 정답 뼈 회전)로 개선 전후 수치를 비교**
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
- [x] 뼈 가중치를 포함한 점 메시 생성 (스키닝)
- [x] 관절 → 뼈 회전, 평활, 기본 자세 복귀
- [x] 보스 방향 회전과 결합
- [x] Dev 씬: `fake_sender.py` 관절 패턴 (실제 손 센서는 사람 검증)

### 자동 검증 — 담당: R4 Pose Avatar
- [x] EditMode: 뼈 가중치 보간(합 1), 관절 → 뼈 방향 계산, 좌우 매핑, 미수신 복귀
- [x] 기존 EditMode 전체 통과 (`FacingYaw_TurnsTowardScreenCenter` 포함): 361/361 = 기존 348 + R4 13
- [x] 프레임 할당 0, fps 전후 비교 (에디터만. 빌드 fps 미측정)
- [x] 생성 프롬프트와 소요 크레딧을 작업 로그에 기록 (해당 없음: Mixamo 리깅, VARCO 미사용)

### 사람 검증 — 담당: 사용자
- [ ] 팔을 뻗으면 아바타가 같은 쪽 팔을 뻗음 (등 뒤에서 본 모습으로 자연스러움)
- [ ] 걷거나 몸을 기울여도 점이 찢어지거나 튀지 않음
- [ ] 등 표현과 밝기가 이전보다 나빠지지 않음

## 5. 통합 인계

기획 세션 작업: `PlayerAvatarView`에 `IPoseSource` 연결 (Initialize 시그니처가 바뀌면 `CombatPresenter`/`AppBootstrap` 연결 수정). 모델 교체로 프리팹 참조가 바뀌면 적을 것.

## 6. 제외 범위

판정 변경, 손가락, 얼굴 표정, 미니 캐릭터(R3)

## 작업 로그

### 2026-11-08 R4 Pose Avatar — 구현과 자동 검증

파일 (`02_Scripts/Game/` 기준):

| 파일 | 내용 |
|---|---|
| `Presentation/Avatar/AvatarPointMesh.cs` | 점 메시 생성. 점마다 삼각형 세 정점의 뼈 가중치를 무게중심 비율로 합쳐 상위 4개를 합 1로 정규화, 원본 bind pose 복사 → `SkinnedMeshRenderer`용. uv1에 휴식 자세 위치 저장 (쉐이더의 척추선·견갑골 주름이 스키닝 후에도 몸에 붙어 있게) |
| `Presentation/Avatar/AvatarPoseSolver.cs` | 순수 로직. 관절 월드 좌표 → 아바타 기준 팔다리 방향 8개 + 몸통 방향. 깊이 처리(아래), 관절별 One Euro, 미수신·가림 시 0.3초 복귀. 할당 0 |
| `Presentation/Avatar/AvatarSkeleton.cs` | 리그 뼈 적용. 휴식 자세 = 메시 bind pose(팔 내린 자세)를 `meshEuler`로 돌린 것 → 기존 정적 점 아바타와 점 단위로 같음 (오차 1.6e-7). 몸통 회전은 Spine·Chest·UpperChest에 나눠 최대 40°, 팔다리는 최소 회전으로 방향 맞춤 (RT `PoseRig.Aim`과 같은 방식). 할당 0 |
| `Presentation/Avatar/AvatarOneEuro.cs` | R3 `PoseSmoother`와 같은 One Euro (최소 컷오프 1.2 Hz, 미분 1 Hz). beta는 미터 단위로 1.8 (R3 0.8 몸통/초 ≈ 1.8 m/s). R3 코드는 UI 어셈블리라 참조하지 않고 Avatar 폴더에 둠 → Core 이동 요청 불필요 |
| `Presentation/PlayerAvatarView.cs` | `rigModel` 필드(리그 FBX). 있으면 Awake에서 리그를 자식으로 만들고 그 SMR을 점 메시로 교체, Animator 끔. `SetPoseSource(IPoseSource poseOrNull)` 추가. 없으면 기존 정적 경로 그대로. `Bind`·`SetVisible`·`Flash`·`FacingYawDeg` 시그니처 변화 없음 |
| `Presentation/Shaders/AvatarPoints.shader` | 주름 계산을 `positionOS` 대신 uv1(휴식 위치)로. 조명·윤곽은 스키닝 후 위치·법선 사용 |
| `03_Prefabs/Game/PresentationRig.prefab` | Avatar의 `rigModel` = `PlayerAvatar_Rig.fbx` (1줄). `sourceMesh`, `pointCount 30000`, `torsoOverBounds 0.71`, `anchorHeight 0.7` 그대로 |
| `Tests/Avatar/` (새 어셈블리 `VisionToWall.Game.Avatar.Tests`, Core·UI·Game·PoseTest 참조) | 13개: 가중치 보간 2, 스키닝 점 메시(실제 FBX, 합 1, 휴식 자세 = 정적 아바타, uv1 동일), 좌표(x 유지·z 반전), 오른팔 → 아바타 오른팔 화면 오른쪽(실제 리그), 리그가 방향을 1° 안에 재현, 미수신 복귀, count 0·null, 가려진 다리만 복귀, 깊이 제한, 쪼그려 앉기 허벅지 깊이 복원, RT 정답 일관성, RT 비교 |
| `Dev/Avatar/` (`VisionToWall.Game.Dev.Avatar`) | `AvatarPoseDev.unity` (PresentationDev 복사 + 키보드 위치, Victory), `AvatarPoseDevBootstrap`: `UdpPoseReceiver`(udpPosePort) 생성 후 `SetPoseSource`. P로 켜고 끔 |

좌표와 순서:
- 아바타 기준 = 아바타 루트(등을 보임: 앞 +z 화면 안쪽, 오른쪽 +x). 관절 월드(미러 적용, +x = 플레이어 오른쪽 = 화면 오른쪽) → (x, y, −z). 좌우 라벨은 바꾸지 않음: 플레이어 오른팔 = 아바타 RightUpperArm = 화면 오른쪽 (테스트로 고정)
- 적용 순서: `LateUpdate`에서 위치·보스 쪽 회전(`maxFacingYawDeg`)을 루트에 먼저 → 포즈는 루트 기준이라 그 위에 얹힘. 몸이 보스를 향해 돌아도 팔 방향은 몸 기준
- 판정 불변: 위치·크기는 기존처럼 `PlayerPosition`과 `hitRadius` (`TorsoWorldWidth`, 휴식 자세 bounds 기준). 골반 위치는 고정이라 쪼그리면 다리만 접힘

깊이 처리 (RT 실측으로 결정, Python 시제품으로 후보 비교 후 C# 이식):
- 팔다리 z × 0.25 (`LimbDepthGain`). 0이면 팔 중앙값이 조금 더 좋지만(4.1° vs 4.9°) 앞으로 뻗는 동작이 사라지므로 일부 유지
- 몸통 z 무시 (`TorsoDepthGain` 0). 원래 z는 대부분 편향(20° 넘게 앞으로 숙임). 모델 자체의 휴식 자세 기울기(9° 뒤로)는 유지
- 허벅지: 정면 길이가 지금까지 본 최대 길이의 0.8 아래로 줄면 길이로 앞쪽 깊이를 복원 (0.55에서 완전). 0.9는 벌려 뛰기에서 MediaPipe 정면 길이 흔들림(±7%)에 오작동 (jacks 허벅지 3.5° → 20.4°)
- 각도 제한: 정면에서 뒤로 팔 30°, 허벅지 30°, 정강이 85°, 앞으로 80° (팔이 등을 뚫지 않게)
- 시도 후 버림: 팔에도 길이 복원(머리 위 팔이 짧게 잡혀 가짜 깊이 → 위팔 95% 26.6° → 44°), 허벅지 앞쪽 z 1.0/0.5 유지(squat 차이 1° 안팎, idle 무릎 7.8° → 9.1~11.8°로 악화)

RT 비교 (`AvatarPoseRtTests`, `<clip>_detected.csv` → 해법 → 실제 리그 뼈 방향 vs `_truth.csv` 정답 뼈 회전 × T-pose 방향. "raw" = 같은 경로에서 깊이 그대로. 60 fps 재생, 전신이 화면 안이고 팔다리 모두 추적 중인 프레임. 중앙값 / 최대, 도):

| clip | upper arm raw | upper arm R4 | forearm raw | forearm R4 | thigh raw | thigh R4 | shin raw | shin R4 | elbow raw | elbow R4 | knee raw | knee R4 | 
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| idle | 11.7 / 13.3 | 3.6 / 5.3 | 20.5 / 24.2 | 7.7 / 9.9 | 6.1 / 7.2 | 2.7 / 3.2 | 23.8 / 25.3 | 6.5 / 7.1 | 23.8 / 26.8 | 0.6 / 1.0 | 29.2 / 31.2 | 7.8 / 8.5 | 
| raise_left_slow | 12.2 / 37.4 | 7.6 / 22.2 | 21.8 / 36.4 | 7.0 / 14.8 | 5.8 / 7.6 | 1.6 / 3.2 | 22.5 / 27.7 | 6.1 / 8.0 | 20.4 / 31.6 | 1.2 / 17.9 | 28.3 / 33.5 | 7.4 / 9.5 | 
| raise_left_fast | 12.1 / 33.9 | 5.9 / 29.5 | 22.0 / 37.4 | 7.4 / 20.4 | 5.9 / 7.1 | 1.6 / 3.1 | 22.8 / 28.2 | 6.1 / 8.4 | 21.3 / 33.1 | 0.8 / 22.8 | 28.2 / 34.2 | 7.5 / 10.0 | 
| raise_right_slow | 11.4 / 37.1 | 5.5 / 22.9 | 21.8 / 36.3 | 8.9 / 12.1 | 6.8 / 8.0 | 2.5 / 4.7 | 23.8 / 27.3 | 6.4 / 7.6 | 21.6 / 32.9 | 1.1 / 25.6 | 30.6 / 34.7 | 8.1 / 9.3 | 
| raise_right_fast | 11.4 / 37.3 | 5.0 / 29.5 | 20.5 / 37.4 | 8.5 / 17.2 | 6.7 / 7.7 | 2.4 / 4.4 | 24.8 / 26.8 | 6.7 / 7.4 | 21.0 / 38.7 | 0.6 / 31.0 | 30.5 / 33.8 | 8.3 / 9.0 | 
| wave_right | 11.4 / 50.9 | 4.4 / 19.0 | 22.3 / 58.9 | 8.8 / 37.3 | 6.5 / 8.8 | 2.7 / 3.1 | 22.3 / 25.7 | 6.0 / 7.1 | 16.8 / 44.7 | 2.0 / 22.3 | 28.5 / 32.1 | 7.5 / 8.5 | 
| squat | 24.7 / 40.6 | 15.9 / 34.1 | 17.0 / 22.7 | 10.1 / 27.5 | 25.2 / 47.7 | 30.1 / 68.0 | 17.3 / 25.8 | 6.9 / 16.5 | 21.4 / 25.4 | 0.8 / 10.5 | 26.3 / 35.6 | 34.4 / 70.3 | 
| jacks | 15.2 / 44.9 | 6.4 / 30.5 | 22.2 / 47.1 | 8.6 / 22.4 | 8.4 / 11.4 | 3.5 / 9.4 | 19.8 / 25.9 | 7.1 / 12.3 | 22.3 / 34.7 | 2.5 / 22.0 | 26.9 / 33.8 | 8.4 / 18.2 | 
| walk_out | 11.9 / 14.4 | 4.1 / 9.3 | 19.6 / 24.3 | 6.9 / 12.9 | 6.9 / 17.1 | 2.9 / 12.7 | 20.4 / 25.8 | 6.2 / 15.0 | 21.6 / 26.9 | 0.5 / 1.7 | 27.2 / 32.9 | 7.5 / 12.9 | 
| **all** | 12.1 / 50.9 | 5.1 / 34.1 | 20.8 / 58.9 | 8.4 / 37.3 | 6.6 / 47.7 | 2.7 / 68.0 | 21.5 / 28.2 | 6.3 / 16.5 | 21.4 / 44.7 | 0.8 / 31.0 | 28.2 / 35.6 | 7.8 / 70.3 |

- 정답 일관성: 정답 관절 방향과 정답 쿼터니언 방향 차이 2° 미만 (`TruthBoneRotations_MatchTruthJoints`)
- 팔꿈치 각도 오차 21.4° → 0.8°, 무릎 28.2° → 7.8° (중앙값). RT 기준(15° 이내)을 전체로 통과. RT의 3D 팔꿈치 17.5~23.6°, 무릎 26.0~30.6°와 같은 원인(깊이 편향)이 사라짐
- **남은 한계: 쪼그려 앉기**. 허벅지 30.1°(raw 25.2°), 무릎 34.4°(raw 26.3°)로 raw보다 나쁨. 깊게 앉았을 때 정답 허벅지는 거의 수평(정면 길이 3 cm)인데 MediaPipe는 정면 21 cm로 줄여 잡고, 몸통 정면 길이도 0.43 → 0.28 m로 줄여 잡음. 입력 자체가 틀려 길이 복원으로는 58° 정도까지만 앞으로 나옴. 위팔도 15.9°. 다른 8클립은 모든 항목 개선
- 최대값이 큰 프레임은 팔을 머리 위로 올리는 순간(위팔 22~30°): MediaPipe 정면 좌표 자체의 오차

성능 (에디터 Play, 1920×1080, `AvatarPoseDev`, 점 30,000개, 같은 세션 연속 측정 6초씩):

| 상태 | fps | 최악 프레임 |
|---|---|---|
| R4 스키닝 + 포즈 (전투 중, wave) | 396.6 | 7.2 ms |
| R4 스키닝, 포즈 끔 (휴식 자세, 결과 화면) | 390.0 | 20.6 ms |
| R4 이전 정적 메시 (`SetMesh`로 되돌림, MediaWall) | 405.5 | 16.2 ms |

- 측정 구간 단계가 서로 달라 차이(약 2%)는 잡음 수준. 1차 측정(전부 결과 화면)도 387.6 / 399.0 / 385.9 fps로 순서가 섞임 → 스키닝 비용은 에디터에서 구분 안 됨. 60 fps 기준 한참 위라 점 수 조정 불필요. GPU 스키닝 켜짐(`PlayerSettings.gpuSkinning` True)
- **빌드 fps는 측정하지 않음** (빌드 생략). 통합 시 Main 빌드에서 확인 필요
- 프레임 할당: Play 중 `solver.Update` + `skeleton.Apply` 600회 = **0** (기준 `new object()` ×600 = 600, ProfilerRecorder `GC Allocation In Frame Count`)

Dev 씬 확인 (`fake_pose_sender.py`, 스크린샷은 Unity 재시작 시 `Temp/`가 지워지므로 세션 scratchpad에도 복사):
- wave: 화면 오른쪽 팔(아바타 오른팔)이 들려 흔들림, 왼팔은 내림 (`Temp/r4_wave_1.png`, `_2`)
- jacks: 양팔 대칭으로 벌림·올림, 등 척추선·견갑골 주름 유지 (`Temp/r4_jacks_1.png`, `_2`)
- 포즈 끔·정적 메시: `Temp/r4_rest_combat.png`, `Temp/r4_static_combat.png` (휴식 자세 = 기존 모습)
- 수신 drop 108~118은 Play 진입 직후 `execute_code` 정지로 생긴 pool drop (알려진 에디터 한계, R3 작업 로그와 같음). 이후 증가 없음
- 실제 손 센서(`run_hand_sensor.bat --exposure -7`)는 카메라 앞 사람이 필요해 이 세션에서 돌리지 않음 → 사람 검증. `config.json`의 `sensorMode`는 바꾸지 않았음 (Dev 씬은 sensorMode와 무관하게 9002를 엶)

기획 세션에 보고·결정 요청:
- **아바타가 보스를 가림** (40번 미확인 항목): 팔을 들면 손이 보스 눈 위로 올라감 (`r4_wave_1.png`) → 사용자 결정 "보스 근처 점 흐리게", 아래 "보스 눈 가림 처리"에서 반영
- 이 세션 중에도 `Assets/01_Scenes/Main.unity`(+236줄, TMP 글자색 오버라이드), `06_Font/GmarketSansTTFMedium SDF.asset`이 수정 상태가 됨 (Play·씬 열기 부산물, RT와 같은 내용). 커밋하지 않았고 되돌리지도 않음

### 통합 인계 (호출 순서)

1. Main 배치 변경 없음: `PresentationRig.prefab`의 Avatar가 `rigModel`을 이미 가짐 (Main에 Avatar 오버라이드 없음 확인)
2. `AppBootstrap`: R3 순서대로 `receiver.Initialize(config)`(external이고 `udpPosePort != 0`일 때) → `combatPresenter.Initialize(...)`(기존, 내부에서 `avatar.Bind`) → **`playerAvatarView.SetPoseSource(receiver)`**. webcam 모드이거나 포트 0이면 `SetPoseSource(null)` 또는 호출 생략(기본 null = 위치만). `Bind`와 순서 무관, Awake 이후면 됨
3. 수신기는 미니 캐릭터와 같은 `UdpPoseReceiver` 하나를 공유 (`IPoseSource`는 `Latest`만 읽고 이벤트 구독 안 함)
4. `PlayerAvatarView` 참조는 `CombatPresenter`의 private 필드라 `AppBootstrap`에는 직접 참조가 필요 (`[SerializeField] PlayerAvatarView` 추가 또는 `FindAnyObjectByType`)
5. 설치 요건은 R3와 같음 (가슴과 양 팔꿈치가 화면 안). 다리가 화면 밖이면 다리는 0.3초 뒤 휴식 자세

### 보스 눈 가림 처리 (2026-11-08, 기획 세션 회신·사용자 결정 "보스 근처 점 흐리게")

- `AvatarPoints.shader`: 점 중심을 화면에 투영해 보스 눈과의 거리(화면 높이 단위, 가로는 종횡비 보정)가 반경 안이면 밝기를 낮춤. 경계는 smoothstep으로 부드럽게. 흐림은 기본 색에만 곱하고 피격 플래시는 그 뒤에 섞으므로 **플래시는 그대로**. 척추선·견갑골 주름도 같은 곱이라 비율 유지
- `PlayerAvatarView`: `Bind`에서 `BossLookAt`(presenter들과 같은 보스 기준점, `BossEye`)을 찾고, `LateUpdate`마다 `OverlayCamera.WorldToViewportPoint`로 보스 화면 위치를 재질에 넣음 (보스가 움직여도 따라감, 카메라 뒤면 끔). 보스가 없으면 반경 0 = 꺼짐
- 직렬화 필드: `bossDimRadius` 0.16 (화면 높이, 0 = 끔), `bossDimEdge` 0.06, `bossDimBrightness` 0.25. 프리팹은 기본값 사용 (YAML 변경 없음). 크기·위치·판정 변화 없음
- 확인 (`AvatarPoseDev`, `fake_pose_sender.py --pattern wave`, 1920×1080): 보스 화면 위치 (0.500, 0.720)
  - 팔 들기, 보스 왼쪽 (`Temp/r4_dim_side.png` 흐림 켬 / `r4_nodim_side.png` 끔): 링 안으로 들어온 손이 흐려지고 홍채와 조준선이 보임
  - 가운데 서기 (`r4_dim_center.png` 팔 든 상태, `r4_dim_center_rest.png` 포즈 끔): 머리 꼭대기만 살짝 흐려지고 홍채·링·조준선이 모두 보임. 등 척추선 그대로
- fps (전투 중 5초씩, 같은 Play): 흐림 끔 388.0 / 흐림 켬 393.2 (차이는 잡음 수준). 프레임 할당: `UpdateBossDim` 600회 = 0 (기준 600)
- EditMode 361/361

### 사람 검증 (병합 후 Main, 실제 손 센서, 기획 세션이 사용자에게 안내)

1. 카메라 앞에 서서 전투에 들어감 → 오른팔을 옆으로 들었다 내림 → 화면 오른쪽 팔이 같이 올라갔다 내려오고, 손이 보스 눈 위에 오면 그 부분만 흐려져 눈이 보임
2. 전투 중 → 좌우로 걷고 몸을 기울이다 화면 밖으로 나감 → 점이 찢어지거나 튀지 않고, 나가면 0.3초에 걸쳐 차렷 자세로 돌아옴
3. 전투 중 → 가만히 서 있다가 피격당함 → 등 척추선과 밝기가 R4 이전과 같고, 피격 플래시는 보스 근처에서도 빨갛게 보임
