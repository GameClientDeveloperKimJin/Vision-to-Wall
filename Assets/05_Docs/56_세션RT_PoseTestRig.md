# 56. 세션 RT — Pose Test Rig

- 담당자: **RT Pose Test Rig**
- 브랜치: `feature/roadmap-pose-test`
- 담당: 합성 영상 자동 검증. 정답 관절을 아는 사람 모델 영상을 손 센서와 같은 인식 처리에 넣고, 결과를 정답·미니 캐릭터와 비교
- 수정 가능: `Assets/07_Models/TestHumanoid/**`, `Assets/02_Scripts/PoseTest/**` (새 어셈블리 `VisionToWall.PoseTest`, Editor 전용 코드는 `PoseTest/Editor/`), `Assets/02_Scripts/PoseTest/Tests/**`, `Tools/HandSensor/pose_from_media.py`와 그 테스트, 이 문서
- 선행 조건: R3 `main` 병합 (2026-11-08 완료)
- 사용자 결정: 모델과 동작은 **Mixamo** (2026-11-08). 사용자가 Adobe 계정으로 내려받아 전달

## 1. 목표

사람이 매번 카메라 앞에서 동작하지 않아도 관절 인식과 그 이후 처리를 수치로 검증한다.

1. R3 미니 캐릭터가 "플레이어 약속"([53번](53_세션R3_PoseCharacter.md) 작업 로그)을 지키는지 클립별로 판정
2. **R4의 기준 데이터**: 정답 뼈 회전을 알고 있는 클립을 R4가 그대로 재사용해 "관절 → 뼈 회전" 결과를 비교할 수 있게 한다

검사 대상은 R2~R3 처리와 MediaPipe 인식 품질이다. CG 모델 인식률이 낮으면 그 자체를 결과로 보고한다 (테스트를 통과시키려고 기준을 낮추지 않음).

## 2. 사용자 준비물 (Mixamo)

사용자가 [mixamo.com](https://www.mixamo.com)에서 내려받아 폴더 하나에 모아 경로를 알려준다.

| 항목 | 설정 |
|---|---|
| 캐릭터 1개 | 사실적인 사람 비율, 옷이 몸에 붙는 캐릭터 (마네킹·만화 비율 피함). 형식 `FBX for Unity`, `With Skin`, T-pose |
| 동작 클립 | 형식 `FBX for Unity`, `Without Skin`, 30 fps, `In Place` 옵션이 있으면 켬 (걷기 제외) |

동작 (검색어 예시, 비슷한 클립이면 됨):

| 동작 | 검색어 |
|---|---|
| 가만히 서 있기 | `Idle` |
| 한 손 흔들기 | `Waving` |
| 팔 들기 (한쪽) | `Raising Hand`, `Reaching` |
| 쪼그려 앉기 | `Air Squat`, `Squat` |
| 벌려 뛰기 | `Jumping Jacks` |
| 옆으로 걷기 | `Left Strafe Walking`, `Right Strafe Walking` (In Place 끔) |

한쪽 팔 클립은 RT가 미러 재생해 반대쪽도 만든다.

라이선스: Mixamo 캐릭터·동작은 프로젝트 안에서 사용 가능하지만 원본 파일 단독 재배포는 안 된다. 저장소가 비공개(`Vision-to-Wall`)라 커밋하지만, **공개 전환 시 `07_Models/TestHumanoid/`를 제외**할 것.

## 3. 처리 흐름

| 단계 | 내용 |
|---|---|
| 1. 렌더 | Editor 메뉴 하나로 클립마다 640×480 정면 PNG 시퀀스와 정답 CSV 생성 (`Temp/pose_check/<clip>/`). 단색 배경, 고른 조명, 카메라 높이·거리 고정 (R3 설치 요건: 가슴과 양 팔꿈치가 화면 안) |
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
- [ ] Mixamo 파일 임포트 (Humanoid, 클립 설정), 정면 렌더 장면
- [ ] 렌더·정답 CSV 생성 메뉴 (뼈 회전 열 포함)
- [ ] `pose_from_media.py`
- [ ] 비교 테스트, 리포트, 비교 이미지
- [ ] R4 사용법을 이 문서 작업 로그에 기록 (정답 뼈 회전 CSV 읽는 법)

### 자동 검증 — 담당: RT Pose Test Rig
- [ ] 기존 EditMode 전체, pytest 전체 통과
- [ ] 리포트 클립별 결과 (PASS가 아니어도 수치로)
- [ ] 정답·인식 CSV는 커밋, PNG·영상은 git 제외. 저장소 증가량 보고

### 사람 검증 — 담당: 사용자
- [ ] 비교 이미지를 보고 미니 캐릭터가 동작을 알아볼 만하게 따라 하는지 판단

## 6. 통합 인계

Main 작업 없음 (테스트 도구). 새 패키지가 필요하면 기획 세션에 요청 (`Packages/`는 기획 세션 전용).

## 7. 제외 범위

실제 카메라 지연 측정, 미니 캐릭터·아바타 코드 수정(문제를 찾으면 보고만), 여러 사람

## 작업 로그
- (RT Pose Test Rig가 기록)
