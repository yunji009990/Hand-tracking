using Oculus.Interaction;
using UnityEngine;

namespace SignBridge.HandSize
{
    /// <summary>
    /// 잡을 수 있는 오브젝트(Grabbable 루트)에 붙인다.
    /// HandSizeEstimator가 측정한 플레이어 손 크기를 기준으로, 원래 스케일로는 잡기 어려운 물체만 조정한다.
    ///
    /// 잡는 굵기(가장 얇은 축)를 "편하게 쥐는 굵기" 범위(측정된 손 길이 × 0.25~0.36)와 비교한다.
    /// - 범위보다 두꺼우면 줄인다. (예: 성인 18cm 손에서 10cm 박스 → 6.5cm)
    /// - 범위보다 얇으면 키운다(최대 1.5배). (예: 성인 18cm 손에서 3.7cm 컵 → 4.5cm)
    /// - 범위 안이면 그대로 둔다. (예: 5.6cm 식빵)
    ///
    /// 기준이 측정된 손 길이의 비율이라, 아동이든 성인 테스터든 그 손에 맞춰 판단한다.
    /// 잡고 있는 동안에는 크기를 바꾸지 않고, 놓은 뒤에 적용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class GraspSizeAdapter : MonoBehaviour
    {
        // 씬에 정수로 저장되므로 순서를 바꾸지 말 것. (0 = ProportionalToHand, 1 = FitGripToHand)
        public enum SizingMode
        {
            [Tooltip("손 크기 비율(측정 손 / 기준 손)만큼 그대로 키우거나 줄인다.")]
            ProportionalToHand = 0,
            [Tooltip("잡기 어려울 만큼 크거나 작을 때만 조정한다. (권장)")]
            FitGripToHand = 1,
        }

        public enum GripAxis { AutoSmallest, X, Y, Z }

        [SerializeField]
        private SizingMode _mode = SizingMode.FitGripToHand;

        [Header("잡는 굵기")]
        [Tooltip("잡는 부분의 굵기를 잴 축(루트 로컬 기준). Auto는 가장 얇은 축.")]
        [SerializeField]
        private GripAxis _gripAxis = GripAxis.AutoSmallest;

        [Tooltip("0보다 크면 자동 측정 대신 이 값(m)을 원래 잡는 굵기로 쓴다.")]
        [SerializeField, Min(0f)]
        private float _gripSizeOverride = 0f;

        [Header("편하게 쥐는 굵기 = 측정된 손 길이 × 비율")]
        [Tooltip("이보다 두꺼우면 줄인다. 0.36 → 성인 18cm 손이면 6.5cm, 아동 13.8cm 손이면 5.0cm.")]
        [SerializeField, Range(0.1f, 0.8f)]
        private float _comfortMaxRatio = 0.36f;

        [Tooltip("이보다 얇으면 키운다. 0.25 → 성인 18cm 손이면 4.5cm, 아동 13.8cm 손이면 3.5cm.")]
        [SerializeField, Range(0.05f, 0.6f)]
        private float _comfortMinRatio = 0.25f;

        [Tooltip("켜면, 너무 얇아 집기 어려운 물체를 키운다.")]
        [SerializeField]
        private bool _growWhenTooSmall = true;

        [Tooltip("키울 때의 최대 배율. 너무 많이 커지는 것을 막는다.")]
        [SerializeField, Range(1f, 3f)]
        private float _maxGrowMultiplier = 1.5f;

        [Tooltip("가장 긴 축이 굵기의 이 배수 이상이면 막대형(채, 스틱, 연필 등)으로 보고 키우지 않는다. " +
                 "막대는 손잡이를 감싸 쥐므로 얇아도 잘 잡히고, 키우면 길이가 너무 길어진다. 0이면 끈다. " +
                 "두꺼워서 줄이는 것은 그대로 적용된다.")]
        [SerializeField, Min(0f)]
        private float _noGrowElongation = 3f;

        [Header("벌림 폭 상한 (보조)")]
        [Tooltip("켜면, 측정된 최대 벌림 폭 × 비율이 위 최대 굵기보다 작을 때 그 값을 상한으로 쓴다. " +
                 "손을 잘 펴지 못하는 아동을 위한 안전장치.")]
        [SerializeField]
        private bool _useMeasuredAperture = true;

        [Tooltip("벌림 폭 상한 = 측정된 최대 벌림 폭 × 이 비율.")]
        [SerializeField, Range(0.3f, 1.2f)]
        private float _apertureFitRatio = 0.8f;

        [Header("ProportionalToHand")]
        [Tooltip("1이면 손 크기 비율 그대로, 0이면 조정하지 않음.")]
        [SerializeField, Range(0f, 1f)]
        private float _strength = 1f;

        [Header("공통")]
        [Tooltip("원래 크기 대비 배율의 최소/최대 한계. 하한 0.1은 아주 크게 놓인 물체도 편한 굵기까지 줄이기 위한 값.")]
        [SerializeField]
        private Vector2 _scaleLimits = new Vector2(0.1f, 2f);

        [Tooltip("크기 변화에 걸리는 시간(초). 0이면 즉시.")]
        [SerializeField, Min(0f)]
        private float _transitionTime = 0.25f;

        [Tooltip("크기에 맞춰 Rigidbody 질량도 바꾼다(부피 비례).")]
        [SerializeField]
        private bool _scaleMass = false;

        [Tooltip("비워두면 같은 오브젝트의 Grabbable을 사용. 잡는 중에는 크기를 바꾸지 않는다.")]
        [SerializeField]
        private PointableElement _grabbable;

        [Tooltip("목표 배율이 바뀔 때 콘솔에 이유를 남긴다.")]
        [SerializeField]
        private bool _logChanges = true;

        private Vector3 _baseScale;
        private float _baseMass;
        private float _baseGripSize;
        private float _baseLargestSize;
        private Rigidbody _rigidbody;
        private float _currentMultiplier = 1f;
        private bool _initialized;
        private bool _loggedOnce;
        private bool _wasGrabbed;
        private readonly System.Collections.Generic.List<MonoBehaviour> _components =
            new System.Collections.Generic.List<MonoBehaviour>();

        /// <summary>원래 스케일에서의 잡는 굵기(m).</summary>
        public float BaseGripSize => _baseGripSize;

        /// <summary>원래 스케일에서의 가장 긴 축 길이(m).</summary>
        public float BaseLargestSize => _baseLargestSize;

        /// <summary>가장 긴 축이 굵기의 No Grow Elongation 배 이상인 막대형 물체인지.</summary>
        public bool IsElongated =>
            _noGrowElongation > 0f && _baseGripSize > 0f && _baseLargestSize >= _baseGripSize * _noGrowElongation;

        public float CurrentMultiplier => _currentMultiplier;
        public float TargetMultiplier { get; private set; } = 1f;

        /// <summary>
        /// 손으로 잡는 설정(Touch / Hand / Distance Hand Grab)이 자신이나 자식에 있는지.
        /// 테이블처럼 Grabbable만 있고 손으로 잡지 않는 물체를 걸러낼 때 쓴다.
        /// </summary>
        public static bool HasHandGrab(GameObject go) =>
            go.GetComponentInChildren<TouchHandGrabInteractable>(true) != null ||
            go.GetComponentInChildren<Oculus.Interaction.HandGrab.HandGrabInteractable>(true) != null ||
            go.GetComponentInChildren<Oculus.Interaction.HandGrab.DistanceHandGrabInteractable>(true) != null;

        private void Awake()
        {
            if (_grabbable == null) _grabbable = GetComponent<PointableElement>();
            _rigidbody = GetComponent<Rigidbody>();
            _baseScale = transform.localScale;
            if (_rigidbody != null) _baseMass = _rigidbody.mass;

            Vector3 size = MeasureSize();
            _baseLargestSize = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            _baseGripSize = _gripSizeOverride > 0f ? _gripSizeOverride : SelectGripAxis(size);
            _initialized = true;
        }

        private void Update()
        {
            HandSizeEstimator estimator = HandSizeEstimator.Instance;
            if (estimator == null || !estimator.IsCalibrated) return;

            float target = ComputeMultiplier(estimator, out string reason);
            if (_logChanges && (!_loggedOnce || Mathf.Abs(target - TargetMultiplier) > 0.01f))
            {
                Debug.Log($"[HandSize] {name}: 굵기 {_baseGripSize * 100f:F1}cm, 길이 {_baseLargestSize * 100f:F1}cm " +
                          $"→ {target:F2}배 ({reason})", this);
            }
            _loggedOnce = true;
            TargetMultiplier = target;

            bool grabbed = IsGrabbed();
            if (grabbed) { _wasGrabbed = true; return; }
            if (_wasGrabbed)
            {
                // 놓은 직후: 잡는 동안 다른 코드가 크기를 되돌렸다면 다시 맞춘다.
                _wasGrabbed = false;
                if ((transform.localScale - _baseScale * _currentMultiplier).sqrMagnitude > 1e-10f) Apply();
            }
            if (Mathf.Approximately(_currentMultiplier, TargetMultiplier)) return;

            if (_transitionTime <= 0f)
            {
                _currentMultiplier = TargetMultiplier;
            }
            else
            {
                float speed = Mathf.Max(Mathf.Abs(TargetMultiplier - _currentMultiplier), 0.05f) / _transitionTime;
                _currentMultiplier = Mathf.MoveTowards(_currentMultiplier, TargetMultiplier, speed * Time.deltaTime);
            }
            Apply();
        }

        private float ComputeMultiplier(HandSizeEstimator estimator, out string reason)
        {
            // 편하게 쥐는 굵기 범위. 손 길이는 측정이 안정적이라 주 기준으로 쓰고,
            // 벌림 폭은 그보다 작을 때만 상한으로 쓴다(값이 흔들려도 대부분 영향 없음).
            float maxGrip = estimator.HandLength * _comfortMaxRatio;
            if (_useMeasuredAperture && estimator.HasGripAperture)
            {
                maxGrip = Mathf.Min(maxGrip, estimator.MaxGripAperture * _apertureFitRatio);
            }
            float minGrip = Mathf.Min(estimator.HandLength * _comfortMinRatio, maxGrip);
            string range = $"편한 굵기 {minGrip * 100f:F1}~{maxGrip * 100f:F1}cm";

            float m = 1f;
            reason = $"잘 잡히는 크기, {range}";
            switch (_mode)
            {
                case SizingMode.FitGripToHand:
                    if (_baseGripSize <= 0f) { reason = "크기를 잴 수 없음"; break; }

                    if (_baseGripSize > maxGrip)
                    {
                        // 너무 두꺼워 손에 잘 안 들어감 → 최대 굵기까지 줄인다.
                        m = maxGrip / _baseGripSize;
                        reason = $"너무 큼, {range}";
                    }
                    else if (_growWhenTooSmall && _baseGripSize < minGrip && IsElongated)
                    {
                        // 막대형: 감싸 쥐는 손잡이라 얇아도 잘 잡힌다. 키우면 길이만 지나치게 길어진다.
                        reason = $"막대형이라 키우지 않음 (길이/굵기 {_baseLargestSize / _baseGripSize:F1}배), {range}";
                    }
                    else if (_growWhenTooSmall && _baseGripSize < minGrip)
                    {
                        // 너무 얇아 집기 어려움 → 최소 굵기까지 키운다.
                        m = minGrip / _baseGripSize;
                        reason = $"너무 작음, {range}";
                    }
                    break;

                case SizingMode.ProportionalToHand:
                    m = Mathf.Lerp(1f, estimator.HandScale, _strength);
                    if (m > 1f && !_growWhenTooSmall) m = 1f;
                    reason = $"손 비율 {estimator.HandScale:F2}";
                    break;
            }

            if (m > 1f)
            {
                m = Mathf.Min(m, _maxGrowMultiplier);
                // 키운 결과가 손에 안 들어갈 만큼 두꺼워지면 안 된다.
                if (_baseGripSize > 0f) m = Mathf.Min(m, Mathf.Max(1f, maxGrip / _baseGripSize));
            }
            return Mathf.Clamp(m, _scaleLimits.x, _scaleLimits.y);
        }

        private void Apply()
        {
            transform.localScale = _baseScale * _currentMultiplier;
            if (_scaleMass && _rigidbody != null)
            {
                _rigidbody.mass = _baseMass * _currentMultiplier * _currentMultiplier * _currentMultiplier;
            }
            RefreshGrabTransformers();
        }

        /// <summary>
        /// Grabbable의 Transformer(GrabFreeTransformer 등)는 시작할 때의 크기를 기억해 두고,
        /// 잡는 동안 그 크기로 강제로 되돌린다(기본 스케일 제한 = 원래 크기 × 1).
        /// 크기를 바꿀 때마다 다시 Initialize해서 새 크기를 기준으로 삼게 한다.
        /// </summary>
        private void RefreshGrabTransformers()
        {
            if (!(_grabbable is IGrabbable grabbable)) return;

            _components.Clear();
            _grabbable.GetComponents(_components);
            foreach (MonoBehaviour mb in _components)
            {
                if (mb is ITransformer transformer) transformer.Initialize(grabbable);
            }
        }

        private bool IsGrabbed() => _grabbable != null && _grabbable.SelectingPointsCount > 0;

        private float SelectGripAxis(Vector3 size)
        {
            switch (_gripAxis)
            {
                case GripAxis.X: return size.x;
                case GripAxis.Y: return size.y;
                case GripAxis.Z: return size.z;
                default: return Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            }
        }

        /// <summary>
        /// 자식 메시의 범위를 루트 로컬 축 기준으로 합쳐 실제(월드) 크기로 돌려준다.
        /// </summary>
        private Vector3 MeasureSize()
        {
            Matrix4x4 worldToRoot = transform.worldToLocalMatrix;
            bool hasBounds = false;
            Bounds local = default;

            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Encapsulate(ref local, ref hasBounds, worldToRoot * mf.transform.localToWorldMatrix, mf.sharedMesh.bounds);
            }
            foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Encapsulate(ref local, ref hasBounds, worldToRoot * smr.transform.localToWorldMatrix, smr.localBounds);
            }
            if (!hasBounds)
            {
                foreach (Collider c in GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger) continue;
                    Encapsulate(ref local, ref hasBounds, worldToRoot, c.bounds);
                }
            }
            if (!hasBounds) return Vector3.zero;

            Vector3 lossy = transform.lossyScale;
            return Vector3.Scale(local.size, new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
        }

        private static void Encapsulate(ref Bounds target, ref bool has, Matrix4x4 m, Bounds b)
        {
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!has) { target = new Bounds(p, Vector3.zero); has = true; }
                else target.Encapsulate(p);
            }
        }

        /// <summary>원래 크기로 되돌린다.</summary>
        public void ResetToBase()
        {
            if (!_initialized) return;
            _currentMultiplier = 1f;
            Apply();
        }

        private void OnValidate()
        {
            if (_scaleLimits.y < _scaleLimits.x) _scaleLimits.y = _scaleLimits.x;
        }
    }
}
