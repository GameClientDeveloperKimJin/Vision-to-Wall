# 56. 세션 RT — Pose Test Rig

- 담당자: **RT Pose Test Rig**
- 브랜치: `feature/roadmap-pose-test`
- 담당: 합성 영상 자동 검증. 정답 관절을 아는 사람 모델 영상을 손 센서와 같은 인식 처리에 넣고, 결과를 정답·미니 캐릭터와 비교
- 수정 가능: `Assets/07_Models/Avatar/PlayerAvatar_Rig.fbx.meta` (임포트 설정만), `Assets/02_Scripts/PoseTest/**` (새 어셈블리 `VisionToWall.PoseTest`, Editor 전용 코드는 `PoseTest/Editor/`), `Assets/02_Scripts/PoseTest/Tests/**`, `Tools/HandSensor/pose_from_media.py`와 그 테스트, 이 문서
- 선행 조건: R3 `main` 병합 (2026-11-08 완료)
- 사용자 결정: 모델은 Mixamo로 리깅한 **`PlayerAvatar_Rig.fbx`**, 동작은 **코드 자세** (2026-11-08)

## 1. 목표

사람이 매번 카메라 앞에서 동작하지 않아도 관절 인식과 그 이후 처리를 수치로 검증한다.

1. R3 미니 캐릭터가 "플레이어 약속"([53번](53_세션R3_PoseCharacter.md) 작업 로그)을 지키는지 클립별로 판정
2. **R4의 기준 데이터**: 정답 뼈 회전을 알고 있는 클립을 R4가 그대로 재사용해 "관절 → 뼈 회전" 결과를 비교할 수 있게 한다

검사 대상은 R2~R3 처리와 MediaPipe 인식 품질이다. CG 모델 인식률이 낮으면 그 자체를 결과로 보고한다 (테스트를 통과시키려고 기준을 낮추지 않음).

## 2. 모델과 자세 (2026-11-08 사용자 결정)

- 모델: **`Assets/07_Models/Avatar/PlayerAvatar_Rig.fbx`**. 전투 아바타 메시를 사용자가 Mixamo 자동 리깅으로 리깅한 것 (뼈 28개 `mixamorig:*`, 정점 18,556, 삼각형 30,000, T-pose, 동작 클립 없음). R4도 같은 모델을 쓴다
- 임포트 설정: RT가 `Humanoid`(아바타 생성), `Read/Write` 켬으로 바꾼다. 이 `.meta` 변경만 허용 (메시 교체·재익스포트 금지)
- **동작 클립을 쓰지 않는다.** 뼈 회전을 코드로 정해 자세를 만든다. 정답 각도를 정확히 알고, 속도·좌우·범위를 조절할 수 있음

| 동작 | 코드 자세 |
|---|---|
| 가만히 서 있기 | 팔 내린 기본 자세 + 작은 호흡 흔들림 |
| 팔 들기 (좌·우) | 어깨 0° → 180° → 0°, 느리게와 빠르게 |
| 흔들기 | 팔을 든 채 팔꿈치 좌우 반복 |
| 쪼그려 앉기 | 골반 높이 낮춤 + 무릎·고관절 굽힘 (단순 형태) |
| 벌려 뛰기 | 팔·다리 벌림 반복 |
| 옆으로 걸어 나갔다 돌아오기 | 몸 전체 이동 (다리 동작은 단순 반복) |

- 텍스처 없는 회색 모델이라 인식률이 낮을 수 있음. 렌더 재질은 음영이 보이는 재질로 RT가 정한다. 그래도 검출률이 기준에 못 미치면 원인과 함께 보고 (Mixamo 기본 캐릭터 추가 여부는 사용자 결정)
- 코드 자세가 부자연스러워 검증이 안 되는 동작이 있으면 그 동작만 Mixamo 클립 요청 (사용자 작업)

## 3. 처리 흐름

| 단계 | 내용 |
|---|---|
| 1. 렌더 | Editor 메뉴 하나로 자세 시퀀스마다 640×480 정면 PNG 시퀀스와 정답 CSV 생성 (`Temp/pose_check/<clip>/`). 단색 배경, 고른 조명, 카메라 높이·거리 고정 (R3 설치 요건: 가슴과 양 팔꿈치가 화면 안) |
| 2. 정답 | `Animator.GetBoneTransform`으로 13관절 (코는 Head 앞 근사). 형식은 R2 녹화 CSV와 같게: 화면 x·y(미러 적용), 월드 x·y·z(골반 중심, Unity 축), visibility 1. 뼈 회전(상완·전완·대퇴·하퇴·척추·목)도 별도 열로 저장 (R4용) |
| 3. 인식 | `pose_from_media.py`: PNG 시퀀스나 영상을 `pose_stream.extract_joints`와 **같은 처리**로 → 같은 형식 CSV. 선택으로 9002 재생 |
| 4. 비교 | EditMode 테스트와 리포트: 정답 vs 인식, 인식 → `PoseSmoother` → 미니 캐릭터 |
| 5. 리포트 | `Temp/pose_check/report.md`: 클립별 PASS 표. 비교 이미지 [합성 프레임 \| 인식(초록)·정답(빨강) \| 미니 캐릭터] |

## 4. 판정 기준 (초안, 측정 후 근거와 함께 조정 가능)

| 항목 | 기준 |
|---|---|
| 검출률 | 사람이 화면에 있는 프레임의 90% 이상에서 검출 |
| 관절 각도 | 팔꿈치·무릎·어깨 각도 오차 15° 이내 (중앙값), 최대값도 기록 |
| 좌우 | 좌우 라벨 뒤바뀜 0 |
| 미니 캐릭터 | 평활 후 각도가 인식 대비 15° 이내, 정지 클립 떨림 기록 |
| 등장·퇴장 | 옆으로 걸어 나가면 사라지고 돌아오면 나타남 |
| 처리 지연 | 동작 시작 → 인형 반응 프레임 수 (카메라·화면 출력 지연은 제외, 별도 표기) |

기준을 바꾸려면 측정값과 이유를 작업 로그에 적고 기획 세션에 보고.

## 5. 체크리스트

### 구현 — 담당: RT Pose Test Rig
- [x] `PlayerAvatar_Rig` 임포트 설정 (Humanoid, Read/Write), 코드 자세 시퀀스, 정면 렌더 장면
- [x] 렌더·정답 CSV 생성 메뉴 (뼈 회전 열 포함)
- [x] `pose_from_media.py`
- [x] 비교 테스트, 리포트, 비교 이미지
- [x] R4 사용법을 이 문서 작업 로그에 기록 (정답 뼈 회전 CSV 읽는 법)

### 자동 검증 — 담당: RT Pose Test Rig
- [x] 기존 EditMode 전체, pytest 전체 통과 (EditMode 348/348 = 기존 301 + RT 47, pytest 107/107 = 기존 100 + 7)
- [x] 리포트 클립별 결과 (PASS가 아니어도 수치로). **9클립 모두 FAIL: 3D 팔꿈치·무릎 각도만** (작업 로그 참고)
- [x] 정답·인식 CSV는 커밋, PNG·영상은 git 제외. 저장소 증가량 약 3.2 MB (CSV 18개 3.0 MB)

### 사람 검증 — 담당: 사용자
- [ ] 비교 이미지를 보고 미니 캐릭터가 동작을 알아볼 만하게 따라 하는지 판단

## 6. 통합 인계

Main 작업 없음 (테스트 도구). 새 패키지가 필요하면 기획 세션에 요청 (`Packages/`는 기획 세션 전용).

## 7. 제외 범위

실제 카메라 지연 측정, 미니 캐릭터·아바타 코드 수정(문제를 찾으면 보고만), 여러 사람

## 작업 로그

### 2026-11-08 RT Pose Test Rig — 구현과 자동 검증

파일:

| 파일 | 내용 |
|---|---|
| `07_Models/Avatar/PlayerAvatar_Rig.fbx.meta` | `animationType` Generic → Humanoid, `avatarSetup` CreateFromThisModel, `isReadable` 1. 아바타 `PlayerAvatar_RigAvatar` 유효, 28뼈 자동 매핑 (Chest = Spine1, UpperChest = Spine2) |
| `PoseTest/PoseTestClips.cs` | 2절 표의 코드 자세 9개 (`idle`, `raise_left/right_slow/fast`, `wave_right`, `squat`, `jacks`, `walk_out`). 시간 → `PoseSpec`(어깨 벌림·굽힘, 팔꿈치, 고관절 벌림·굽힘, 무릎, 상체 숙임, 몸 이동) 순수 함수. 30 fps |
| `PoseTest/PoseRig.cs` | T-pose에서 시작해 팔다리 뼈를 목표 방향으로 최소 회전. 발은 바닥에 고정 (쪼그리면 골반이 내려감). 정답 관절 13개(코 = 얼굴 정점 중 가장 앞), 뼈 회전 14개 |
| `PoseTest/PoseTruth.cs` | 정답 CSV 쓰기·읽기 (R4용) |
| `PoseTest/PoseCheck.cs` | 판정: 검출률, 각도(3D, 참고용 정면 2D), 좌우 뒤바뀜, `PoseSmoother` 60 fps 재생 비교, 지연, 떨림, 퇴장·등장, 깊이 오차 |
| `PoseTest/Editor/PoseTestRenderer.cs` | 메뉴 `VisionToWall/PoseTest/1. Render Clips (PNG + truth CSV)`. preview scene(열린 씬 변경 없음)에서 렌더 |
| `PoseTest/Editor/PoseTestReport.cs` | 메뉴 `VisionToWall/PoseTest/2. Build Report (after pose_from_media.py)`. CSV를 `Tests/Data`로 복사, `report.md`, 비교 이미지 |
| `PoseTest/Tests/` | EditMode 47개: 리그 8 (실제 FBX, 각도·좌우·발 고정·뼈 회전 재현), 자세·CSV·판정 로직 10, 커밋 데이터 29 |
| `PoseTest/Tests/Data/<clip>_truth.csv`, `_detected.csv` | 9클립 × 2 = 18개, 3.0 MB |
| `Tools/HandSensor/pose_from_media.py` | PNG 시퀀스·영상 → R2 녹화 CSV. `hand_sensor.create_pose_landmarker`, `pose_landmarks`, `pose_world_landmarks`, `pose_stream.extract_joints`를 그대로 import (같은 처리). `--all`, `--send`(9002 실시간 재생, 센서와 같은 count 0 규칙) |
| `Tools/HandSensor/tests/test_pose_from_media.py` | 7개 |

실행 순서 (전체 약 1분):
1. Unity 메뉴 `VisionToWall/PoseTest/1. Render Clips` → `Temp/pose_check/<clip>/frame_*.png`, `truth.csv` (1914프레임, 약 30초)
2. `Tools\HandSensor\.venv\Scripts\python.exe pose_from_media.py --all ..\..\Temp\pose_check` → `<clip>/detected.csv` (약 25초)
3. 메뉴 `VisionToWall/PoseTest/2. Build Report` → `Temp/pose_check/report.md`, `<clip>/sheet.png`, `<clip>/compare_NNNNN.png`
4. EditMode `VisionToWall.PoseTest.Tests` (커밋된 `Tests/Data` 사용, 렌더·Python 불필요)

렌더 조건: 키 약 1.7 m, 카메라 3 m 정면, 높이 1.05 m 수평, 세로 화각 43° (C270 근사), 640×480, 전신이 화면 안. 회색 0.55 URP Lit (텍스처 없음, 그림자 없음), 배경 (0.12, 0.12, 0.13), 키·필 방향광 2개.

**리포트 경로**: `Temp/pose_check/report.md` (git 제외. Unity를 재시작하면 `Temp/`가 지워지므로 1~3을 다시 실행)

클립별 결과 (기준 그대로, 수치는 중앙값 / 최대):

| 클립 | 판정 | 검출률 | 팔꿈치 3D | 무릎 3D | 어깨 3D | 좌우 | 인형 vs 인식 (팔꿈치/무릎/어깨) | 지연 (시작 +20°, 인식 / 인형) |
|---|---|---|---|---|---|---|---|---|
| idle | FAIL | 100% | 23.6 / 27.8° | 27.8 / 31.4° | 1.6 / 2.9° | 0 | 0.1 / 0.0 / 0.1° | - |
| raise_left_slow | FAIL | 100% | 20.8 / 31.3° | 28.2 / 34.0° | 4.5 / 28.6° | 0 | 0.5 / 0.2 / 0.4° | 267 / 234 ms |
| raise_left_fast | FAIL | 100% | 21.3 / 32.0° | 28.1 / 34.9° | 3.3 / 34.7° | 0 | 0.7 / 0.2 / 0.4° | 67 / 84 ms |
| raise_right_slow | FAIL | 100% | 21.6 / 32.6° | 30.6 / 35.0° | 2.9 / 31.3° | 0 | 0.4 / 0.1 / 0.5° | 200 / 234 ms |
| raise_right_fast | FAIL | 100% | 21.1 / 37.3° | 30.5 / 34.4° | 3.0 / 30.0° | 0 | 0.4 / 0.2 / 0.4° | 67 / 100 ms |
| wave_right | FAIL | 100% | 17.5 / 44.2° | 28.4 / 32.9° | 2.3 / 17.9° | 0 | 0.5 / 0.1 / 0.5° | 100 / 117 ms |
| squat | FAIL | 100% | 21.5 / 27.8° | 26.0 / 35.2° | 3.3 / 22.2° | 0 | 0.2 / 0.3 / 0.2° | - |
| jacks | FAIL | 100% | 21.7 / 32.1° | 26.9 / 34.7° | 9.3 / 33.2° | 0 | 1.3 / 0.7 / 2.4° | 34 / 67 ms |
| walk_out | FAIL | 100% (전신 219프레임) | 22.2 / 28.7° | 27.2 / 32.3° | 2.2 / 9.3° | 0 | 0.2 / 0.2 / 0.1° | - |

- 실패 항목은 9클립 모두 **3D 팔꿈치·무릎 각도뿐**. 검출률, 3D 어깨 각도, 좌우, 인형, 퇴장·등장은 모두 기준 통과
- 퇴장·등장 (`walk_out`): 몸이 화면을 완전히 벗어난 순간 인형 이미 사라짐 (0.00초), 화면 밖 100프레임 중 오검출 0, 전신이 다시 들어온 뒤 0.02초에 표시. 몸 일부만 보이는(양 어깨는 화면 안) 프레임 중 인형 표시 70% (나머지는 MediaPipe가 가장자리에서 놓침)
- 떨림: 정지 클립 인형 이동 0.0004 몸통/프레임 (패널 0.03 px), 최대 0.1 px. 합성 영상에는 카메라 잡음이 없으므로 실제 카메라(R3 실측 0.7 px)보다 훨씬 작음 → 떨림 판정에는 부적합, 기록만
- 지연 (플레이어 약속 3, 카메라 노출·전송·화면 출력 제외): 빠른 팔 들기에서 인형이 정답보다 84~100 ms 늦음 (인식 67 ms + 평활 17~33 ms). 느린 팔 들기는 +20° 도달 시점이 200~267 ms 늦음 (MediaPipe가 느린 각도 변화를 몇 도 덜 따라가 시간 차가 커 보임, 절반 지점 기준 167~217 ms)

**원인: MediaPipe 월드 깊이(z) 편향** (3D 팔꿈치·무릎 실패):
- 같은 각도를 월드 x·y(정면)로만 계산하면 팔꿈치 5~7°, 무릎 1~3°, 어깨 2~9° (중앙값). 화면 좌표도 1~3°. 즉 위치는 맞고 깊이만 틀림
- 골반 기준 깊이 오차 (인식 − 정답, 중앙값): 어깨 **−15 ~ −22 cm** (카메라 쪽), 손목 −8 ~ −27 cm, 발목 **+9 ~ +21 cm** (먼 쪽). 곧게 선 몸을 "위는 앞으로, 발은 뒤로" 기울어진 것으로 추정 → 곧은 팔다리가 3D에서 20~30° 굽어 보임
- 재질·배경·카메라 높이와 무관: 흰 FBX 재질 + 청회색 배경, 피부색 + 검은 배경, 카메라 1.4 m / 3.5 m 모두 같은 편향 (어깨 −15 ~ −21 cm, 발목 +9 ~ +23 cm)과 같은 3D 오차 (팔꿈치 21~24°, 무릎 25~31°)
- 실제 사람에서도 비슷한 경향: R2 녹화(앉은 자세)에서 어깨 z가 골반보다 −28 cm. 자세가 달라 직접 비교는 불가. CG 모델 때문인지 lite 모델 특성인지는 실제 사람의 정답 없이는 구분 불가
- 기준 조정 제안 (기획 세션 결정): 미니 캐릭터(R3)는 x·y만 쓰므로 R3 판정은 정면 2D가 맞음 (2D면 9클립 모두 PASS). R4(3D 뼈 회전)는 3D가 맞고 지금 FAIL → R4는 월드 z를 그대로 쓰면 팔다리가 앞뒤로 20~30° 굽는다. z 감쇠·제한 또는 정면 평면 우선이 필요. 판정 기준 자체는 바꾸지 않았음 (`PoseClipDataTests.KnownFailure`가 이 실패를 고정해 둠: 고쳐지거나 다른 항목이 실패하면 테스트가 알려줌)

그 밖의 발견:
- **쪼그려 앉기는 정면 2D에서 무릎이 굽어 보이지 않음**: 정답 무릎 안쪽 각 80°인 순간에도 정면 투영은 거의 곧은 다리(2D 무릎 오차는 작음). 인형은 발목 고정 덕분에 몸이 낮아지고 다리가 짧아지는 형태로만 보임 (`squat/sheet.png`). R3 구조상 한계, 코드 수정 안 함
- 편집 모드 `Camera.Render`는 같은 업데이트 안에서 바뀐 뼈로 `SkinnedMeshRenderer`를 다시 스키닝하지 않음 → 매 프레임 `BakeMesh` 결과를 별도 `MeshRenderer`로 그림. 6000.3에서 `BakeMesh(mesh)`는 트랜스폼 스케일을 포함하고 `BakeMesh(mesh, true)`가 미포함 (실측, 문서 설명과 반대로 보임). 스케일이 두 번 적용되면 아바타가 2배가 되어 머리가 화면 밖 → 그 상태에서 팔 내린 자세 검출 0% (재질 문제로 오인했다가 정정). 렌더러가 키를 검사해 5% 넘게 다르면 중단
- **Unity 에디터 크래시 2회** (03:40, 03:50, `Unrecoverable D3D12 device error`, 업로드 메모리 예약 고갈): 원인은 렌더 프레임마다 `ReadPixels` 뒤 `Texture2D.Apply()` (호출당 GPU 메모리 +1.25 MB, 에디터 업데이트 안에서 반납 안 됨, 300프레임 +412 MB). `Apply()` 제거 후 300프레임 +33 MB. 리포트 PNG 인코딩도 텍스처 없이 `EncodeArrayToPNG`
- Unity를 재시작하면 프로젝트 `Temp/`가 지워짐 (크래시 후 렌더·이미지 사라짐). 커밋 대상 CSV는 `Assets/.../Tests/Data`에 있어 영향 없음
- 첫 렌더 프레임은 모델 없이 배경만 나옴 → Stage 생성 시 워밍업 렌더 2회
- 렌더는 같은 Unity 세션 안에서 결정적, MediaPipe도 결정적 (같은 PNG → 같은 CSV). 커밋된 CSV로 테스트는 항상 같은 결과

### R4 사용법 (정답 뼈 회전 CSV)

- 파일: `Assets/02_Scripts/PoseTest/Tests/Data/<clip>_truth.csv` (정답), `<clip>_detected.csv` (같은 프레임의 MediaPipe 결과 = R4 입력)
- 앞 80열은 R2 녹화 형식 그대로 (`t_ms,count,{Joint}_sx,_sy,_wx,_wy,_wz,_v` × 13). `PoseRecording.Parse`로 정답·인식 모두 읽힘 → `PoseFrame`으로 R4 처리에 그대로 넣을 수 있음. 정답 visibility는 화면 안 1, 밖 0. 화면에 관절이 하나도 없으면 count 0 (관절 칸 비움, 뒤 열은 있음)
- 이어서 `Hips_px,py,pz` (장면 미터, 카메라 원점 +z 방향), 그다음 `PoseRig.TruthBones` 14개 뼈마다 `{Bone}_qx,qy,qz,qw`: Hips, Spine, Chest, UpperChest, Neck, Head, Left/RightUpperArm, Left/RightLowerArm, Left/RightUpperLeg, Left/RightLowerLeg
- 쿼터니언 뜻: **아바타 루트 기준 T-pose 대비 회전**. 아바타는 +z를 보고 왼쪽이 −x (Unity 휴머노이드 기본). 같은 모델에 재현: `bone.rotation = root.rotation * q * bindRootRot(bone)`, `bindRootRot = Inverse(root.rotation) * T-pose 때 bone.rotation` (`PoseRig.BindRootRotation`). 재현 오차 5 mm 미만 (`PoseRigTests.BoneDeltas_ReproduceThePose`)
- 읽기: `PoseTruth.Parse(text)` → `.Joints`(PoseRecording), `.BoneDelta(frame, HumanBodyBones)`, `.HipsPosition(frame)`. 어셈블리 `VisionToWall.PoseTest` 참조 (autoReferenced 꺼짐)
- 좌표 주의: 렌더 아바타는 카메라를 **마주 봄**(루트 y 180°). 관절 월드 좌표는 R2와 같은 미러 적용 축이라 R4 아바타(등을 보임)에서는 "화면 같은 쪽" 규칙(54번 2절)으로 그대로 비교 가능. 뼈 쿼터니언은 아바타 자기 기준이라 루트 회전과 무관
- 뼈 방향 비교만 필요하면 정답 관절 월드 좌표의 차(예: 팔꿈치 − 어깨)가 상완 방향. 쿼터니언은 비틀림까지 필요할 때 사용 (코드 자세는 최소 회전이라 비틀림은 T-pose 기준 최소)
- R4 판정 예: `<clip>_detected.csv` → R4 관절→뼈 회전 → 상완·전완·대퇴·하퇴 방향을 정답 관절 방향과 각도 비교. 위 깊이 편향 때문에 3D로는 20~30° 차이가 날 것

### 다른 세션·기획 세션에 보고
- `Tools/HandSensor/README.md`에 `pose_from_media.py` 한 줄 추가 필요 (RT 수정 범위 밖). 사용법은 파일 docstring에 있음
- 이 세션 중 `Assets/01_Scenes/Main.unity`(+236줄), `Assets/06_Font/GmarketSansTTFMedium SDF.asset`이 수정 상태가 됨 (크래시 후 재시작·씬 재열기 부산물로 추정, RT가 편집하지 않음). 커밋하지 않았고 되돌리지도 않음. `Assets/_Recovery/0.unity`, `0 (1).unity`는 크래시 때 Unity가 만든 씬 백업 (커밋 안 함, 확인 후 삭제 판단은 사용자)
- 미니 캐릭터·센서 코드 수정 없음
