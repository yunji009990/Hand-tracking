using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace SignBridge.HandSize
{
    /// <summary>
    /// 플레이어가 손을 움직이는 동안 손 데이터를 계속 쌓아서 손 프로필을 만든다. 씬에 하나만 둔다.
    ///
    /// 쌓는 데이터 (프레임마다, 손별로):
    /// - 손 길이: 손목 ~ 중지 끝 뼈 길이의 합 (손가락을 굽혀도 거의 변하지 않음) → 중앙값
    /// - 손바닥 너비: 검지 ~ 새끼 손가락 첫 마디 사이 거리 → 중앙값
    /// - 벌림 폭: 엄지 끝 ~ 검지/중지 끝 거리 중 큰 값 → 상위 백분위수(기본 95%)
    ///   손을 크게 벌린 순간들이 쌓일수록 "이 손으로 감쌀 수 있는 최대 크기"에 가까워진다.
    ///
    /// 양손이 일정 시간 이상 사라지면(헤드셋을 벗고 다른 아동이 착용) 쌓인 데이터를 비우고 다시 쌓는다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class HandSizeEstimator : MonoBehaviour
    {
        public static HandSizeEstimator Instance { get; private set; }

        [Header("손 입력 (비워두면 씬의 Hand 컴포넌트를 자동으로 찾음)")]
        [SerializeField, Interface(typeof(IHand))]
        private List<UnityEngine.Object> _hands = new List<UnityEngine.Object>();

        [Header("기준값")]
        [Tooltip("오브젝트가 원래 크기로 보이는 기준 손 길이(m). 성인 평균 손목~중지 끝 약 0.185m.")]
        [SerializeField, Range(0.10f, 0.25f)]
        private float _referenceHandLength = 0.185f;

        [Tooltip("이 범위를 벗어나는 손 길이 측정값은 트래킹 오류로 보고 버린다(m).")]
        [SerializeField]
        private Vector2 _validLengthRange = new Vector2(0.08f, 0.25f);

        [Header("데이터 누적")]
        [Tooltip("손별로 보관할 최근 프레임 수. 60fps 기준 900 = 약 15초 분량. 많을수록 안정적이지만 변화에 느리게 반응한다.")]
        [SerializeField, Range(60, 3600)]
        private int _historySize = 900;

        [Tooltip("손 길이 첫 추정값을 내기 위해 필요한 최소 샘플 수.")]
        [SerializeField, Range(5, 300)]
        private int _minSamples = 20;

        [Tooltip("벌림 폭을 쓰기 시작할 최소 샘플 수. 아이가 손을 몇 번 쥐었다 펴 볼 시간이 필요하다. (60fps 기준 120 = 약 2초)")]
        [SerializeField, Range(10, 1800)]
        private int _minApertureSamples = 120;

        [Tooltip("벌림 폭을 정할 백분위수. 1에 가까울수록 '가장 크게 벌린 순간'에 가깝고, 낮출수록 튀는 값에 강하다.")]
        [SerializeField, Range(0.5f, 1f)]
        private float _aperturePercentile = 0.95f;

        [Tooltip("벌림 폭의 하한 = 손 길이 × 이 비율. 아이가 손을 충분히 펴지 않아 폭이 너무 작게 잡히는 것을 막는다.")]
        [SerializeField, Range(0f, 1f)]
        private float _minApertureRatio = 0.45f;

        [Tooltip("쌓인 데이터로 프로필을 다시 계산하는 간격(초).")]
        [SerializeField, Range(0.05f, 2f)]
        private float _recomputeInterval = 0.25f;

        [Tooltip("값이 이 비율 이상 바뀔 때만 갱신 이벤트를 보낸다(떨림 방지).")]
        [SerializeField, Range(0f, 0.2f)]
        private float _changeThreshold = 0.03f;

        [Tooltip("양손이 이 시간(초) 이상 추적되지 않으면 새 사용자로 보고 데이터를 비운다. 0이면 끔. " +
                 "어린 아동은 손을 내리거나 시야 밖으로 자주 빼므로 넉넉하게 둔다.")]
        [SerializeField, Min(0f)]
        private float _resetAfterLostSeconds = 10f;

        [Header("에디터 테스트 (헤드셋 없이 확인용)")]
        [Tooltip("0보다 크면 실제 측정 대신 이 손 길이(m)를 사용한다. 에디터에서만 적용.")]
        [SerializeField, Range(0f, 0.25f)]
        private float _editorSimulatedHandLength = 0f;

        [Tooltip("시뮬레이션할 때의 벌림 폭(m). 0이면 손 길이 × 0.7로 계산.")]
        [SerializeField, Range(0f, 0.25f)]
        private float _editorSimulatedAperture = 0f;

        [SerializeField]
        private bool _logChanges = true;

#if ISDK_OPENXR_HAND
        private static readonly HandJointId[] MiddleChain =
        {
            HandJointId.HandWristRoot, HandJointId.HandMiddle0, HandJointId.HandMiddle1,
            HandJointId.HandMiddle2, HandJointId.HandMiddle3, HandJointId.HandMiddleTip
        };
#else
        private static readonly HandJointId[] MiddleChain =
        {
            HandJointId.HandWristRoot, HandJointId.HandMiddle1,
            HandJointId.HandMiddle2, HandJointId.HandMiddle3, HandJointId.HandMiddleTip
        };
#endif

        /// <summary>추정된 손 길이(m). 측정 전에는 기준값.</summary>
        public float HandLength { get; private set; }

        /// <summary>기준 손 대비 비율(아동은 보통 0.7~0.85).</summary>
        public float HandScale => HandLength / _referenceHandLength;

        public float ReferenceHandLength => _referenceHandLength;

        /// <summary>손바닥 너비(m, 검지~새끼 손가락 첫 마디). 측정 전에는 0.</summary>
        public float PalmWidth { get; private set; }

        /// <summary>쌓인 데이터에서 구한 최대 벌림 폭(m, 엄지 끝 ~ 검지/중지 끝). HasGripAperture가 false면 0.</summary>
        public float MaxGripAperture { get; private set; }

        /// <summary>벌림 폭을 믿을 만큼 데이터가 쌓였는지.</summary>
        public bool HasGripAperture { get; private set; }

        /// <summary>손 길이 추정값이 한 번 이상 나왔는지.</summary>
        public bool IsCalibrated { get; private set; }

        /// <summary>현재 사용자에 대해 쌓인 샘플 수(양손 합).</summary>
        public int SampleCount => _left.Count + _right.Count;

        /// <summary>손 길이 추정값이 바뀔 때 호출. 인자는 HandScale.</summary>
        public event Action<float> WhenHandScaleChanged = delegate { };

        /// <summary>손 프로필(길이, 너비, 벌림 폭) 중 하나라도 의미 있게 바뀌면 호출.</summary>
        public event Action<HandSizeEstimator> WhenProfileChanged = delegate { };

        private const float RetryInterval = 2f;

        private readonly List<IHand> _resolvedHands = new List<IHand>();
        private readonly Dictionary<IHand, float> _retryAt = new Dictionary<IHand, float>();
        private readonly HandHistory _left = new HandHistory();
        private readonly HandHistory _right = new HandHistory();
        private readonly List<float> _scratch = new List<float>();
        private float _lastTrackedTime;
        private float _nextRecomputeTime;
        private bool _dirty;
        private float _lastSimulatedLength = -1f, _lastSimulatedAperture = -1f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[HandSize] HandSizeEstimator가 여러 개 있습니다. '{name}'은(는) 무시됩니다.", this);
                enabled = false;
                return;
            }
            Instance = this;
            HandLength = _referenceHandLength;
            ResolveHands();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void ResolveHands()
        {
            _resolvedHands.Clear();
            foreach (var obj in _hands)
            {
                if (obj is IHand hand) _resolvedHands.Add(hand);
            }

            if (_resolvedHands.Count == 0)
            {
                foreach (var hand in FindObjectsByType<Hand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    _resolvedHands.Add(hand);
                }
            }

            if (_resolvedHands.Count == 0)
            {
                Debug.LogWarning("[HandSize] 씬에서 Hand 컴포넌트를 찾지 못했습니다. Hands 목록에 직접 연결하세요.", this);
            }
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (_editorSimulatedHandLength > 0f)
            {
                ApplySimulation();
                return;
            }
            _lastSimulatedLength = -1f;
#endif
            CollectSamples();

            if (_dirty && Time.unscaledTime >= _nextRecomputeTime)
            {
                _dirty = false;
                _nextRecomputeTime = Time.unscaledTime + _recomputeInterval;
                Recompute();
            }
        }

        private void CollectSamples()
        {
            bool leftDone = false, rightDone = false, anyTracked = false;

            for (int i = _resolvedHands.Count - 1; i >= 0; i--)
            {
                IHand hand = _resolvedHands[i];
                if (hand == null || (hand is UnityEngine.Object o && o == null))
                {
                    _resolvedHands.RemoveAt(i);
                    continue;
                }
                if (_retryAt.TryGetValue(hand, out float retryTime) && Time.unscaledTime < retryTime) continue;

                Handedness handedness;
                Sample sample;
                try
                {
                    if (!hand.IsConnected || !hand.IsTrackedDataValid) continue;
                    anyTracked = true;
                    if (!hand.IsHighConfidence) continue;
                    handedness = hand.Handedness;
                    if ((handedness == Handedness.Left && leftDone) ||
                        (handedness == Handedness.Right && rightDone)) continue;
                    if (!TryMeasure(hand, out sample)) continue;
                }
                catch (Exception)
                {
                    // 데이터 소스가 아직 없는 Hand(헤드셋 미연결 등)는 예외를 내므로 잠시 뒤에 다시 시도한다.
                    _retryAt[hand] = Time.unscaledTime + RetryInterval;
                    continue;
                }
                _retryAt.Remove(hand);

                if (sample.Length < _validLengthRange.x || sample.Length > _validLengthRange.y) continue;

                if (handedness == Handedness.Left) { leftDone = true; _left.Add(sample, _historySize); }
                else { rightDone = true; _right.Add(sample, _historySize); }
                _dirty = true;
            }

            if (anyTracked)
            {
                _lastTrackedTime = Time.unscaledTime;
            }
            else if (_resetAfterLostSeconds > 0f && SampleCount > 0 &&
                     Time.unscaledTime - _lastTrackedTime > _resetAfterLostSeconds)
            {
                // 새 사용자일 수 있으니 쌓인 데이터만 비운다. 다음 추정값이 나올 때까지 현재 크기는 유지.
                ClearHistory();
                if (_logChanges) Debug.Log("[HandSize] 손이 사라져 쌓인 데이터를 비웠습니다. 다시 측정합니다.", this);
            }
        }

        private static bool TryMeasure(IHand hand, out Sample sample)
        {
            sample = default;

            if (!hand.GetJointPose(MiddleChain[0], out Pose prev)) return false;
            float length = 0f;
            for (int i = 1; i < MiddleChain.Length; i++)
            {
                if (!hand.GetJointPose(MiddleChain[i], out Pose next)) return false;
                length += Vector3.Distance(prev.position, next.position);
                prev = next;
            }
            if (length <= 0f) return false;

            if (!hand.GetJointPose(HandJointId.HandThumbTip, out Pose thumbTip) ||
                !hand.GetJointPose(HandJointId.HandIndexTip, out Pose indexTip) ||
                !hand.GetJointPose(HandJointId.HandMiddleTip, out Pose middleTip) ||
                !hand.GetJointPose(HandJointId.HandIndex1, out Pose indexKnuckle) ||
                !hand.GetJointPose(HandJointId.HandPinky1, out Pose pinkyKnuckle))
            {
                return false;
            }

            sample.Length = length;
            sample.Aperture = Mathf.Max(
                Vector3.Distance(thumbTip.position, indexTip.position),
                Vector3.Distance(thumbTip.position, middleTip.position));
            sample.PalmWidth = Vector3.Distance(indexKnuckle.position, pinkyKnuckle.position);
            return true;
        }

        private void Recompute()
        {
            int count = SampleCount;
            if (count < _minSamples) return;

            float length = Percentile(s => s.Length, 0.5f);
            float palm = Percentile(s => s.PalmWidth, 0.5f);
            bool hasAperture = count >= _minApertureSamples;
            float aperture = hasAperture
                ? Mathf.Max(Percentile(s => s.Aperture, _aperturePercentile), length * _minApertureRatio)
                : 0f;

            SetProfile(length, palm, aperture, hasAperture, force: !IsCalibrated);
        }

        private float Percentile(Func<Sample, float> selector, float p)
        {
            _scratch.Clear();
            _left.CopyTo(_scratch, selector);
            _right.CopyTo(_scratch, selector);
            _scratch.Sort();
            float index = p * (_scratch.Count - 1);
            int lo = Mathf.FloorToInt(index);
            int hi = Mathf.Min(lo + 1, _scratch.Count - 1);
            return Mathf.Lerp(_scratch[lo], _scratch[hi], index - lo);
        }

        private void SetProfile(float length, float palm, float aperture, bool hasAperture, bool force)
        {
            bool lengthChanged = force || Changed(length, HandLength);
            bool apertureChanged = hasAperture != HasGripAperture || (hasAperture && Changed(aperture, MaxGripAperture));
            bool palmChanged = Changed(palm, PalmWidth);
            if (!lengthChanged && !apertureChanged && !palmChanged) return;

            if (lengthChanged) HandLength = length;
            if (palmChanged) PalmWidth = palm;
            if (apertureChanged) { MaxGripAperture = aperture; HasGripAperture = hasAperture; }
            IsCalibrated = true;

            if (_logChanges && (lengthChanged || apertureChanged))
            {
                string ap = HasGripAperture ? $"{MaxGripAperture * 100f:F1}cm" : "측정 중";
                Debug.Log($"[HandSize] 손 길이 {HandLength * 100f:F1}cm (기준 대비 {HandScale:F2}배), " +
                          $"손바닥 {PalmWidth * 100f:F1}cm, 최대 벌림 {ap}, 샘플 {SampleCount}", this);
            }
            if (lengthChanged) WhenHandScaleChanged(HandScale);
            WhenProfileChanged(this);
        }

        private bool Changed(float next, float current) =>
            current <= 0f ? next > 0f : Mathf.Abs(next - current) / current >= _changeThreshold;

#if UNITY_EDITOR
        private void ApplySimulation()
        {
            float aperture = _editorSimulatedAperture > 0f ? _editorSimulatedAperture : _editorSimulatedHandLength * 0.7f;
            if (Mathf.Approximately(_editorSimulatedHandLength, _lastSimulatedLength) &&
                Mathf.Approximately(aperture, _lastSimulatedAperture)) return;

            _lastSimulatedLength = _editorSimulatedHandLength;
            _lastSimulatedAperture = aperture;
            SetProfile(_editorSimulatedHandLength, _editorSimulatedHandLength * 0.45f, aperture, true, force: true);
        }
#endif

        private void ClearHistory()
        {
            _left.Clear();
            _right.Clear();
            _dirty = false;
        }

        /// <summary>쌓인 데이터를 비우고 다시 측정한다(예: 다음 아동 차례 버튼).</summary>
        public void Recalibrate()
        {
            ClearHistory();
            IsCalibrated = false;
            HasGripAperture = false;
        }

        /// <summary>코드에서 Hand를 연결할 때 사용.</summary>
        public void SetHands(IEnumerable<IHand> hands)
        {
            _resolvedHands.Clear();
            _resolvedHands.AddRange(hands);
        }

        private struct Sample
        {
            public float Length;
            public float Aperture;
            public float PalmWidth;
        }

        /// <summary>한 손의 최근 샘플을 고정 크기 원형 버퍼로 보관한다.</summary>
        private class HandHistory
        {
            private readonly List<Sample> _samples = new List<Sample>();
            private int _cursor;

            public int Count => _samples.Count;

            public void Add(Sample sample, int capacity)
            {
                if (_samples.Count < capacity)
                {
                    _samples.Add(sample);
                }
                else
                {
                    if (_samples.Count > capacity) { _samples.RemoveRange(capacity, _samples.Count - capacity); }
                    _cursor %= capacity;
                    _samples[_cursor] = sample;
                    _cursor = (_cursor + 1) % capacity;
                }
            }

            public void CopyTo(List<float> target, Func<Sample, float> selector)
            {
                for (int i = 0; i < _samples.Count; i++) target.Add(selector(_samples[i]));
            }

            public void Clear()
            {
                _samples.Clear();
                _cursor = 0;
            }
        }
    }
}
