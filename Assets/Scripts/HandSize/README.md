# Hand Size 모듈

플레이어가 손을 움직이는 동안 손 데이터를 계속 쌓아서, 잡을 수 있는 물체가 그 손에 맞는 크기가 되도록 자동으로 조정합니다.

## 구성

| 스크립트 | 붙이는 곳 | 역할 |
|---|---|---|
| `HandSizeEstimator` | 씬에 1개 | 손별로 최근 900프레임(약 15초) 동안의 손 데이터를 쌓고, 0.25초마다 손 프로필을 다시 계산합니다. |
| `GraspSizeAdapter` | 잡는 물체 (Grabbable 루트) | 손 프로필에 맞춰 물체 크기를 부드럽게 바꿉니다. 잡고 있는 동안에는 바꾸지 않습니다. |

### 쌓는 손 데이터

| 값 | 측정 방법 | 계산 |
|---|---|---|
| 손 길이 | 손목부터 중지 끝까지 뼈 길이의 합 | 중앙값 |
| 손바닥 너비 | 검지와 새끼 손가락 첫 마디 사이 거리 | 중앙값 |
| **최대 벌림 폭** | 엄지 끝에서 검지 끝, 엄지 끝에서 중지 끝 거리 중 큰 값 | 상위 95% |

- **최대 벌림 폭**은 "이 손으로 감쌀 수 있는 최대 크기"를 나타냅니다. 아이가 손을 쥐었다 펴면서 데이터가 쌓일수록 정확해집니다.
- 샘플이 120개(약 2초 분량) 쌓이기 전에는 벌림 폭을 쓰지 않고 손 길이로 대신 계산합니다.
- 아이가 손을 충분히 펴지 않아도 물체가 지나치게 줄지 않도록, 벌림 폭에는 하한이 있습니다(`손 길이 × 0.45`).
- 양손이 10초 이상 안 보이면 쌓인 데이터를 비우고 다시 쌓습니다. 헤드셋을 다음 아동에게 넘길 때를 위한 기능입니다.
  - 5~7세 아동은 손을 내리거나 시야 밖으로 자주 빼기 때문에 넉넉하게 잡았습니다. `Reset After Lost Seconds`에서 바꿀 수 있습니다.

## 설정 방법

1. **Tools > SignBridge > Hand Size > 씬 자동 설정**을 실행합니다.
   - 씬에 `HandSizeEstimator`가 없으면 하나 만듭니다.
   - Hand Grab(Touch / Hand / Distance)이 있는 모든 Grabbable에 `GraspSizeAdapter`를 붙입니다. 테이블처럼 Hand Grab이 없는 물체는 건너뜁니다.
2. 새로 추가한 물체는 **Tools > SignBridge > Hand Size > 선택한 오브젝트에 GraspSizeAdapter 추가**로 붙입니다.

## 크기 조정 방식 (`GraspSizeAdapter.Mode`)

- **FitGripToHand** (기본값): 잡는 부분의 굵기가 최대값을 넘으면 그 최대값까지 줄입니다.
  - 최대 굵기는 `측정된 최대 벌림 폭 × Aperture Fit Ratio(0.8)`입니다.
  - 벌림 폭 데이터가 아직 없으면 `손 길이 × Max Grip Ratio(0.4)`를 대신 씁니다.
  - 예: 손 길이 13cm, 벌림 폭 9.1cm인 아동이면 최대 굵기는 7.3cm입니다. 10cm 큐브는 7.3cm로 줄어듭니다.
  - 잡는 굵기는 메시에서 가장 얇은 축으로 자동으로 잽니다. 축은 `Grip Axis`로, 굵기 값은 `Grip Size Override`로 직접 정할 수 있습니다.
- **ProportionalToHand**: 손 크기 비율(아동 ÷ 성인 기준 18.5cm)만큼 그대로 줄입니다.

**기본적으로 줄이기만 하고 키우지는 않습니다** (`Allow Grow` 꺼짐).
- 원래 크기에서 잘 잡히는 물체는 그대로 둡니다.
- 너무 작아서 집기 어려운 물체만 골라 `Allow Grow`를 켜면, `손 길이 × Min Grip Ratio`까지 키울 수 있습니다.
- 원래 크기 대비 배율은 `Multiplier Limits`(기본 0.5~2배) 안으로 제한됩니다.

## 헤드셋 없이 테스트

`HandSizeEstimator`의 **Editor Simulated Hand Length**에 값을 넣고 Play합니다.

- 예: `0.13`(아동), `0.185`(성인)
- 벌림 폭은 **Editor Simulated Aperture**로 따로 정할 수 있습니다. 0이면 손 길이 × 0.7로 계산합니다.
- 에디터에서만 적용됩니다.
- 테스트가 끝나면 **반드시 0으로 되돌리세요.** 0이 아니면 실제 측정값 대신 이 값이 쓰입니다.

## 코드에서 쓰기

```csharp
var est = SignBridge.HandSize.HandSizeEstimator.Instance;
est.WhenProfileChanged += e =>
    Debug.Log($"손 길이 {e.HandLength:F3}m, 최대 벌림 {e.MaxGripAperture:F3}m, 샘플 {e.SampleCount}");
est.Recalibrate(); // 다음 아동 차례에서 쌓인 데이터를 비우고 다시 측정
```

## 참고

- 콘솔의 `[HandSize]` 로그에서 손 길이, 손바닥 너비, 최대 벌림 폭, 샘플 수를 확인할 수 있습니다.
- 물체가 커질 때 테이블 등에 살짝 파고들면 물리 엔진이 밀어냅니다.
- 질량을 크기에 맞춰 바꾸려면 `Scale Mass`를 켭니다.
