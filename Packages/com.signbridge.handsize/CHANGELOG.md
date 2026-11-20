# Changelog

## [1.1.1] - 2026-11-20

### 수정
- 실로폰채처럼 긴 막대형 물체가 1.5배로 커져 너무 길어지던 문제를 고쳤습니다.
  - 가장 긴 축이 굵기의 3배 이상이면(`No Grow Elongation`, 0이면 끔) 얇아도 키우지 않습니다. 막대는 손잡이를 감싸 쥐므로 얇아도 잘 잡힙니다.
  - 두꺼워서 줄이는 것은 그대로 적용됩니다.
  - 탬버린처럼 납작하고 넓은 물체도 같은 기준에 걸려 키우지 않습니다.

## [1.1.0] - 2026-11-20

### 추가
- `HandSizeEstimator`에 **Auto Attach Adapters** 옵션(기본 켜짐)을 추가했습니다.
  - Play하면 손으로 잡을 수 있는 모든 `Grabbable`에 `GraspSizeAdapter`를 자동으로 붙입니다.
  - 실행 중에 생성되는 물체도 `Auto Attach Scan Interval`(기본 1초)마다 찾아서 붙입니다.
- `GraspSizeAdapter.HasHandGrab(GameObject)`: 손으로 잡는 물체인지 판단하는 함수를 공개했습니다. 에디터 메뉴도 같은 기준을 씁니다.
- `HandSizeEstimator.AttachAdaptersInScene()`: 코드에서 바로 연결할 수 있습니다.

### 변경
- 배율 한계 필드를 `Multiplier Limits`(0.5~2)에서 `Scale Limits`(0.1~2)로 바꿨습니다. 크게 놓인 물체가 0.5배에 막혀 편한 굵기까지 줄지 못하던 문제를 고쳤습니다.
  - 필드 이름이 바뀌어, 기존 씬에 저장된 값 대신 새 기본값이 적용됩니다.

## [1.0.0] - 2026-11-20

처음 패키지로 분리한 버전입니다.

### 추가
- `HandSizeEstimator`
  - Meta Interaction SDK `Hand`에서 손 길이, 손바닥 너비, 최대 벌림 폭을 손별로 약 15초 분량씩 쌓아 손 프로필을 만듭니다.
  - 측정 전에는 5~7세 아동 평균(손 길이 13.8cm, Wen et al. 2020)으로 시작합니다.
  - 허용 손 길이는 10~23cm라서 아동과 성인 테스터 모두 측정됩니다.
  - 양손이 10초 넘게 안 보이면 새 사용자로 보고 다시 측정합니다.
  - 헤드셋 없이 확인할 수 있는 에디터 시뮬레이션을 제공합니다.
- `GraspSizeAdapter`
  - 잡는 굵기를 "편하게 쥐는 굵기(손 길이 × 0.25~0.36)"와 비교해, 두꺼우면 줄이고 얇으면 키웁니다(최대 1.5배).
  - 잡는 동안에는 크기를 바꾸지 않습니다.
  - Grabbable의 Transformer를 다시 초기화해서, 잡아도 바뀐 크기가 유지됩니다.
  - 물체마다 크기를 정한 이유를 콘솔에 남깁니다.
- 에디터 메뉴
  - **Tools > SignBridge > Hand Size**: 씬 자동 설정, 선택한 오브젝트에 Adapter 추가.
  - **Tools > SignBridge > Diagnostics**: 빠진 Interaction SDK 참조 찾기, 오브젝트 구조 출력.
