# Hand Size 모듈

아동의 손 크기를 실시간으로 측정해서, 잡을 수 있는 물체의 크기를 손에 맞게 자동으로 조정합니다.

## 구성

| 스크립트 | 붙이는 곳 | 역할 |
|---|---|---|
| `HandSizeEstimator` | 씬에 1개 | 손목부터 중지 끝까지 뼈 길이를 더해 손 길이를 잽니다. 최근 60개 값의 중앙값을 씁니다. 양손이 3초 이상 안 보이면 새 사용자로 보고 다시 잽니다. |
| `GraspSizeAdapter` | 잡는 물체 (Grabbable 루트) | 손 길이에 맞춰 물체 크기를 부드럽게 바꿉니다. 잡고 있는 동안에는 바꾸지 않습니다. |

## 설정 방법

1. **Tools > SignBridge > Hand Size > 씬 자동 설정**을 실행합니다.
   - 씬에 `HandSizeEstimator`가 없으면 하나 만듭니다.
   - Hand Grab(Touch / Hand / Distance)이 있는 모든 Grabbable에 `GraspSizeAdapter`를 붙입니다. 테이블처럼 Hand Grab이 없는 물체는 건너뜁니다.
2. 새로 추가한 물체는 **Tools > SignBridge > Hand Size > 선택한 오브젝트에 GraspSizeAdapter 추가**로 붙입니다.

## 크기 조정 방식 (`GraspSizeAdapter.Mode`)

- **FitGripToHand** (기본값): 잡는 부분의 굵기가 `손 길이 × [Min Grip Ratio, Max Grip Ratio]` 범위를 벗어날 때만 범위 안으로 맞춥니다.
  - 기본 범위는 0.08~0.4입니다.
  - 예: 손 길이가 13cm인 아동이면 1.0cm~5.2cm가 됩니다. 10cm 큐브는 5.2cm로 줄어듭니다.
  - 잡는 굵기는 메시에서 가장 얇은 축으로 자동으로 잽니다. 축은 `Grip Axis`로, 굵기 값은 `Grip Size Override`로 직접 정할 수 있습니다.
- **ProportionalToHand**: 손 크기 비율(아동 ÷ 성인 기준 18.5cm)만큼 그대로 줄이거나 키웁니다. 성인 기준으로 만든 물체를 통째로 맞출 때 씁니다.

어느 방식이든 원래 크기 대비 배율은 `Multiplier Limits`(기본 0.5~2배) 안으로 제한됩니다.

## 헤드셋 없이 테스트

`HandSizeEstimator`의 **Editor Simulated Hand Length**에 값을 넣고 Play합니다. 예: `0.13`(아동), `0.185`(성인).

- 에디터에서만 적용됩니다.
- 테스트가 끝나면 **반드시 0으로 되돌리세요.** 0이 아니면 실제 측정값 대신 이 값이 쓰입니다.

## 코드에서 쓰기

```csharp
var est = SignBridge.HandSize.HandSizeEstimator.Instance;
est.WhenHandScaleChanged += scale => Debug.Log($"손 비율 {scale:F2}");
est.Recalibrate(); // 다음 아동 차례에서 다시 측정
```

## 참고

- 물체가 커질 때 테이블 등에 살짝 파고들면 물리 엔진이 밀어냅니다.
- 질량을 크기에 맞춰 바꾸려면 `Scale Mass`를 켭니다.
