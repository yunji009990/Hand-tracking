using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace SignBridge.HandSize
{
    /// <summary>
    /// 사용자의 손 길이(손목 ~ 중지 끝, 뼈 길이 합)를 실시간으로 측정해
    /// 기준(성인) 손 대비 비율을 제공한다. 씬에 하나만 둔다.
    ///
    /// - 손가락이 굽어 있어도 값이 크게 변하지 않도록 직선거리 대신 관절 사이 거리의 합을 쓴다.
    /// - 튀는 값에 강하도록 최근 샘플의 중앙값을 쓴다.
    /// - 손이 일정 시간 이상 사라지면(헤드셋을 벗고 다른 아동이 착용) 다시 측정한다.
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

        [Tooltip("이 범위를 벗어나는 측정값은 트래킹 오류로 보고 버린다(m).")]
        [SerializeField]
        private Vector2 _validLengthRange = new Vector2(0.08f, 0.25f);

        [Header("측정")]
        [Tooltip("중앙값을 계산할 최근 샘플 수.")]
        [SerializeField, Range(10, 300)]
        private int _sampleWindow = 60;

        [Tooltip("처음 추정값을 내기 위해 필요한 최소 샘플 수.")]
        [SerializeField, Range(5, 300)]
        private int _minSamples = 20;

        [Tooltip("추정값이 이 비율 이상 바뀔 때만 갱신 이벤트를 보낸다(떨림 방지).")]
        [SerializeField, Range(0f, 0.2f)]
        private float _changeThreshold = 0.03f;

        [Tooltip("양손이 이 시간(초) 이상 추적되지 않으면 새 사용자로 보고 다시 측정한다. 0이면 끔.")]
        [SerializeField, Min(0f)]
        private float _resetAfterLostSeconds = 3f;

        [Header("에디터 테스트 (헤드셋 없이 확인용)")]
        [Tooltip("0보다 크면 실제 측정 대신 이 손 길이(m)를 사용한다. 에디터에서만 적용.")]
        [SerializeField, Range(0f, 0.25f)]
        private float _editorSimulatedHandLength = 0f;

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

        /// <summary>실제 측정(또는 시뮬레이션) 값이 한 번 이상 나왔는지.</summary>
        public bool IsCalibrated { get; private set; }

        /// <summary>손 길이 추정값이 바뀔 때 호출. 인자는 HandScale.</summary>
        public event Action<float> WhenHandScaleChanged = delegate { };

        private const float RetryInterval = 2f;

        private readonly List<IHand> _resolvedHands = new List<IHand>();
        private readonly Dictionary<IHand, float> _retryAt = new Dictionary<IHand, float>();
        private readonly List<float> _samples = new List<float>();
        private readonly List<float> _sortBuffer = new List<float>();
        private int _sampleCursor;
        private float _lastTrackedTime;
        private float _lastSimulated = -1f;

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
                if (!Mathf.Approximately(_editorSimulatedHandLength, _lastSimulated))
                {
                    _lastSimulated = _editorSimulatedHandLength;
                    SetEstimate(_editorSimulatedHandLength, force: true);
                }
                return;
            }
            _lastSimulated = -1f;
#endif
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

                float length;
                Handedness handedness;
                try
                {
                    if (!hand.IsConnected || !hand.IsTrackedDataValid) continue;
                    anyTracked = true;
                    if (!hand.IsHighConfidence) continue;
                    handedness = hand.Handedness;
                    if ((handedness == Handedness.Left && leftDone) ||
                        (handedness == Handedness.Right && rightDone)) continue;
                    if (!TryMeasure(hand, out length)) continue;
                }
                catch (Exception)
                {
                    // 데이터 소스가 아직 없는 Hand(헤드셋 미연결 등)는 예외를 내므로 잠시 뒤에 다시 시도한다.
                    _retryAt[hand] = Time.unscaledTime + RetryInterval;
                    continue;
                }
                _retryAt.Remove(hand);

                if (handedness == Handedness.Left) leftDone = true; else rightDone = true;
                AddSample(length);
            }

            if (anyTracked)
            {
                _lastTrackedTime = Time.unscaledTime;
            }
            else if (_resetAfterLostSeconds > 0f && _samples.Count > 0 &&
                     Time.unscaledTime - _lastTrackedTime > _resetAfterLostSeconds)
            {
                // 새 사용자일 수 있으니 샘플만 비운다. 다음 추정값이 나올 때까지 현재 크기는 유지.
                _samples.Clear();
                _sampleCursor = 0;
            }
        }

        private static bool TryMeasure(IHand hand, out float length)
        {
            length = 0f;
            if (!hand.GetJointPose(MiddleChain[0], out Pose prev)) return false;
            for (int i = 1; i < MiddleChain.Length; i++)
            {
                if (!hand.GetJointPose(MiddleChain[i], out Pose next)) return false;
                length += Vector3.Distance(prev.position, next.position);
                prev = next;
            }
            return length > 0f;
        }

        private void AddSample(float length)
        {
            if (length < _validLengthRange.x || length > _validLengthRange.y) return;

            if (_samples.Count < _sampleWindow)
            {
                _samples.Add(length);
            }
            else
            {
                _samples[_sampleCursor] = length;
                _sampleCursor = (_sampleCursor + 1) % _sampleWindow;
            }

            if (_samples.Count >= Mathf.Min(_minSamples, _sampleWindow))
            {
                SetEstimate(Median(), force: !IsCalibrated);
            }
        }

        private float Median()
        {
            _sortBuffer.Clear();
            _sortBuffer.AddRange(_samples);
            _sortBuffer.Sort();
            int mid = _sortBuffer.Count / 2;
            return _sortBuffer.Count % 2 == 1
                ? _sortBuffer[mid]
                : 0.5f * (_sortBuffer[mid - 1] + _sortBuffer[mid]);
        }

        private void SetEstimate(float length, bool force)
        {
            if (!force && Mathf.Abs(length - HandLength) / HandLength < _changeThreshold) return;

            HandLength = length;
            IsCalibrated = true;
            if (_logChanges)
            {
                Debug.Log($"[HandSize] 손 길이 {length * 100f:F1}cm → 기준 대비 {HandScale:F2}배", this);
            }
            WhenHandScaleChanged(HandScale);
        }

        /// <summary>수동으로 다시 측정을 시작한다(예: 다음 아동 차례 버튼).</summary>
        public void Recalibrate()
        {
            _samples.Clear();
            _sampleCursor = 0;
            IsCalibrated = false;
        }

        /// <summary>코드에서 Hand를 연결할 때 사용.</summary>
        public void SetHands(IEnumerable<IHand> hands)
        {
            _resolvedHands.Clear();
            _resolvedHands.AddRange(hands);
        }
    }
}
