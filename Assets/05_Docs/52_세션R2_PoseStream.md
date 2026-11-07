# 52. 세션 R2 — Pose Stream

- 담당자: **R2 Pose Stream**
- 브랜치: `feature/roadmap-pose-stream`
- 담당: 손 센서(`hand_sensor.py`)가 이미 돌리는 MediaPipe Pose 결과에서 관절 13개를 UDP 9002로 송신
- 수정 가능: `Tools/HandSensor/**`, 이 문서
- 선행 조건: R1 `main` 병합, 기획 세션 선행 작업 반영

## 1. 목표

미니 캐릭터(R3)와 전투 아바타(R4)가 쓸 관절 좌표를 보낸다. 기존 9000(손)과 9001(몸 중심) 스트림은 그대로 둔다.

## 2. 기획 세션 선행 작업

`Core/PoseProtocol.cs`, `Core/PoseContracts.cs`(`PoseFrame`, `IPoseSource`), config 필드 `udpPosePort`(기본 9002)를 `main`에 넣는다. Python 쪽은 이 문서 규격대로 R2가 구현한다.

### 관절 (고정 순서, MediaPipe 인덱스)

| # | 관절 | MP | # | 관절 | MP |
|---|---|---|---|---|---|
| 0 | 코 | 0 | 7 | 왼쪽 골반 | 23 |
| 1 | 왼쪽 어깨 | 11 | 8 | 오른쪽 골반 | 24 |
| 2 | 오른쪽 어깨 | 12 | 9 | 왼쪽 무릎 | 25 |
| 3 | 왼쪽 팔꿈치 | 13 | 10 | 오른쪽 무릎 | 26 |
| 4 | 오른쪽 팔꿈치 | 14 | 11 | 왼쪽 발목 | 27 |
| 5 | 왼쪽 손목 | 15 | 12 | 오른쪽 발목 | 28 |
| 6 | 오른쪽 손목 | 16 | | | |

왼쪽·오른쪽은 **플레이어 몸 기준** (미러와 무관). 미러는 화면 좌표 x에만 적용한다.

### 패킷 (little-endian)

| 오프셋 | 내용 |
|---|---|
| 0 | magic `uint32` `0x57325602` |
| 4 | seq `uint32` |
| 8 | timestampMs `uint32` |
| 12 | count `uint16` (0 = 사람 없음, 아니면 13) |
| 14~ | 관절마다 `float32` 6개: 화면 x, 화면 y, 월드 x, 월드 y, 월드 z, visibility |

- 화면 좌표: 0~1, 원점 왼쪽 아래, 미러 적용 (9000·9001과 같음)
- 월드 좌표: MediaPipe `pose_world_landmarks` (미터, 골반 중심 원점). **Unity 좌표계로 변환해서 보냄**: x 오른쪽(+, 미러 적용), y 위(+), z 카메라에서 멀어지는 쪽(+)
- 패킷 크기: 14 + 13 × 24 = 326바이트

## 3. 처리 설계

- Pose 추론이 돌아간 프레임마다 1패킷. `PoseThrottle`이 건너뛴 프레임은 보내지 않음
- 사람이 사라지면 count 0 패킷을 1번 보내고, 이후 사람이 다시 잡힐 때까지 보내지 않음
- visibility가 낮은 관절도 그대로 보냄. 판단은 수신 측
- 월드 좌표가 없는 모델 출력이면 월드 값 0, 작업 로그에 기록
- 지연 측정: 프레임 캡처 시각 기준 `timestampMs`. 추론 시간(`inferMs`)이 늘어나는지 전후 비교
- `--no-body`면 관절 스트림도 보내지 않음

## 4. 체크리스트

### 구현 — 담당: R2 Pose Stream
- [x] 관절 추출, 좌표 변환 (화면·월드, 미러, Unity 축)
- [x] 패킷 인코더, 9002 송신, count 0 처리
- [x] 관절 시험 패턴 송신기 (팔 흔들기 등, R3·R4가 카메라 없이 시험할 수 있게). `Tools/FakeSender/fake_sender.py`는 수정 범위 밖이라 `Tools/HandSensor/fake_pose_sender.py`로 만듦
- [x] `README.md`에 관절 스트림 규격 요약

### 자동 검증 — 담당: R2 Pose Stream
- [x] pytest 전체 통과. 패킷 바이트 고정 예시, 미러 시 좌우 라벨 유지, count 0
- [x] 관절 송신 전후 `inferMs`, fps 비교 기록 (손이 화면에 보이는 상태)
- [x] 끝나면 손 센서 종료, `config.json` 원복 여부 사용자에게 알림

### 사람 검증 — 담당: 사용자
- [ ] (R2 수신 확인 도구로) 오른팔을 들면 오른쪽 손목의 y가 오르고, 화면 x가 미러 기준으로 맞음

## 5. 통합 인계

Unity 쪽 변경 없음. 9002 수신기는 R3가 만든다. `requirements.txt`가 바뀌면 적을 것.

## 6. 제외 범위

손가락 관절, 얼굴, 2명 이상, Unity 수신기(R3)

## 작업 로그

### 2026-11-08 구현 (R2 Pose Stream)

변경 파일 (모두 `Tools/HandSensor/`):

| 파일 | 내용 |
|---|---|
| `pose_stream.py` (신규) | 관절 추출, 화면·월드 좌표 변환, 패킷 인코더·디코더, `PoseStreamer` (count 0 1회 처리). 순수 모듈 |
| `hand_sensor.py` | Pose가 실행된 프레임마다 9002 송신. 캡처 시각을 `timestampMs`로 사용. 로그에 `joints=` 추가, 종료 로그에 `pose packets=` 추가 |
| `sensor_config.py` | `udpPosePort` 읽기 (기본 9002, 0 = 끔). 0~65535 clamp, `udpPort`·`udpBodyPort`와 같으면 경고 후 끔 (Unity `ConfigService`와 같은 규칙) |
| `pose_monitor.py` (신규) | 9002 수신 확인 도구. 손목·팔꿈치·코 출력, 종료 시 패킷 수, seq 끊김, 지연 |
| `fake_pose_sender.py` (신규) | 카메라 없이 가짜 관절 송신. 패턴 `wave`, `raise`, `jacks`, 사람 있음/없음 주기 |
| `tests/test_pose_stream.py` (신규), `tests/test_sensor_config.py` | 바이트 고정 예시, 미러 시 좌우 라벨 유지, Unity 축 변환, count 0 1회, `PoseProtocol.cs` 상수 대조, 포트 규칙 |
| `README.md` | 관절 스트림 규격, 확인 도구 사용법 |

- 9000·9001 패킷 형식과 송신 시점은 바꾸지 않음. 관절 패킷은 몸 스트림 소켓으로 다른 포트에 보냄
- `requirements.txt` 변경 없음. 새 도구는 표준 라이브러리만 사용
- 월드 좌표: `mediapipe==1.2.2`의 `PoseLandmarkerResult`에 `pose_world_landmarks`가 있음을 확인. 없을 때는 0으로 보내고 `[pose] model gives no world landmarks` 로그 1회
- 좌표 변환: MediaPipe 월드 (x 이미지 오른쪽, y 아래, z 작을수록 카메라 쪽) → Unity (x = 미러면 `-x`, y = `-y`, z 그대로)
- 사람이 처음 잡히기 전에는 아무것도 보내지 않음 (count 0도 보내지 않음)
- 화면 좌표는 9000·9001과 같이 0~1로 clamp. 화면 밖 관절(앉았을 때 발목 등)은 가장자리 값이 되므로 visibility로 판단할 것

### 2026-11-08 검증

pytest: `94 passed` (기존 68 + 신규 26: `test_pose_stream.py` 24, `test_sensor_config.py` 2).

가짜 송신기 → 수신 도구 (`fake_pose_sender.py --pattern raise --on 3 --off 1.5 --duration 5`): 106패킷, count 0 1회, bad 0, seq 끊김 0. `raise`에서 오른쪽 손목이 화면 오른쪽(x 0.59)에서 y 0.78로 오르고, 2초 뒤 왼쪽 손목이 화면 왼쪽(x 0.41)에서 오름.

관절 송신 전후 비교: Logi C270, 640×480, `--exposure -7`, 미리보기 없음, 20초씩 전 → 후 → 전 → 후, 사용자가 카메라 앞에서 손을 천천히 움직임. "전"은 `main`의 `hand_sensor.py`를 그대로 실행. 처음 2초 제외한 1초 로그 평균.

| 측정 | fps | inferMs | hands 평균 | body | 패킷 (손 / 몸 / 관절) |
|---|---|---|---|---|---|
| 전 1 | 29.8 | 24.9 | 0.80 | 1.00 | 544 / 544 / - |
| 후 1 | 29.8 | 26.1 | 1.00 | 1.00 | 544 / 544 / 544 |
| 전 2 | 29.8 | 24.9 | 0.33 | 1.00 | 543 / 543 / - |
| 후 2 | 29.8 | 25.6 | 1.00 | 1.00 | 543 / 543 / 543 |

- fps 변화 없음. `inferMs`는 후가 0.7~1.2 ms 높지만, 관절 처리는 `inferMs` 측정 구간(손·Pose 추론) 밖이다. 후 측정에서 손이 더 자주 보였고(hands 1.00 vs 0.33~0.80), 손이 보이면 손 추론이 길어지므로 차이는 손 유무로 보인다. 조건별 2회라 확정은 아님
- 관절 추출 + 패킷 생성 단독 측정: 프레임당 약 6.5 µs (`timeit` 2만 회)
- 수신 도구: 544/544, 543/543 수신, seq 끊김 0, 지연(캡처 → 수신) 평균 26.3 / 25.8 ms, 최대 47 ms. 지연 대부분은 손 + Pose 추론 시간
- 이 측정에서는 사람이 계속 보여 count 0 패킷은 없었음 (count 0은 pytest와 가짜 송신기로 확인)
- 측정 종료 후 노출은 `auto`로 복구됨 (정상 종료 로그 `[camera] exposure auto`). `config.json`은 바꾸지 않음
- 측정 전에 사용자가 켜둔 `hand_sensor.py --exposure -7` (PID 36880, 26416)을 사용자 승인 후 강제 종료함. 강제 종료라 그 프로세스는 노출을 복구하지 않았으나, 이어진 측정 실행이 정상 종료하며 `auto`로 되돌림
- 측정용 집계 명령과 로그는 scratchpad에만 있고 커밋하지 않음

### 2026-11-08 사람 검증 방식 변경

- 1·2차 시도 (`pose_monitor.py`를 30초·47초 실행): 사용자가 안내대로 동작하지 않아 팔 든 구간이 없음. 스트림은 정상 (839/839, 1313/1313 수신, seq 끊김 0)
- 사용자 요청으로 **미리보기 창에서 Enter로 녹화** 방식 추가. `pose_record.py` (신규): 9002로 보낸 관절 값을 메모리에 모았다가 정지할 때 `recordings/*.csv`로 저장하고 팔 들기 판정 출력. 카메라 영상은 저장 안 함. `Tools/HandSensor/.gitignore`에 `recordings/` 추가
- 판정: 손목 visibility 0.5 이상이고 화면 y > 코 y면 든 상태. 오른팔은 1초 이상, 화면 x 오른쪽 절반, 월드 y가 내렸을 때보다 높아야 PASS. 왼팔은 왼쪽 절반
- pytest `100 passed` (`test_pose_record.py` 6개 추가: 가짜 `raise` 패턴 PASS, 좌우 라벨을 바꾸면 FAIL, 한쪽만 들면 FAIL, visibility 낮으면 든 것으로 안 셈, CSV 왕복)
