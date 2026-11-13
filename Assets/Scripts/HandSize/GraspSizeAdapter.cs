using Oculus.Interaction;
using UnityEngine;

namespace SignBridge.HandSize
{
    /// <summary>
    /// 잡을 수 있는 오브젝트(Grabbable 루트)에 붙인다.
    /// HandSizeEstimator가 측정한 손 크기에 맞춰 오브젝트 크기를 부드럽게 조정한다.
    /// 잡고 있는 동안에는 크기를 바꾸지 않고, 놓은 뒤에 적용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class GraspSizeAdapter : MonoBehaviour
    {
        public enum SizingMode
        {
            [Tooltip("손 크기 비율만큼 그대로 키우거나 줄인다. (성인 기준으로 만든 오브젝트를 아동 손에 맞출 때)")]
            ProportionalToHand,
            [Tooltip("잡는 부분의 굵기가 손 길이 대비 허용 범위를 벗어날 때만 범위 안으로 맞춘다.")]
            FitGripToHand,
        }

        public enum GripAxis { AutoSmallest, X, Y, Z }

        [SerializeField]
        private SizingMode _mode = SizingMode.FitGripToHand;

        [Header("ProportionalToHand")]
        [Tooltip("1이면 손 크기 비율 그대로, 0이면 조정하지 않음.")]
        [SerializeField, Range(0f, 1f)]
        private float _strength = 1f;

        [Header("FitGripToHand")]
        [Tooltip("잡는 부분의 굵기를 잴 축(루트 로컬 기준). Auto는 가장 얇은 축.")]
        [SerializeField]
        private GripAxis _gripAxis = GripAxis.AutoSmallest;

        [Tooltip("0보다 크면 자동 측정 대신 이 값(m)을 원래 잡는 굵기로 쓴다.")]
        [SerializeField, Min(0f)]
        private float _gripSizeOverride = 0f;

        [Tooltip("잡는 굵기의 최소값 = 손 길이 × 이 비율. 너무 작아서 못 집는 것을 방지.")]
        [SerializeField, Range(0.02f, 0.5f)]
        private float _minGripRatio = 0.08f;

        [Tooltip("잡는 굵기의 최대값 = 손 길이 × 이 비율. 너무 커서 손에 안 들어오는 것을 방지.")]
        [SerializeField, Range(0.05f, 1f)]
        private float _maxGripRatio = 0.4f;

        [Header("공통")]
        [Tooltip("원래 크기 대비 배율의 최소/최대 한계.")]
        [SerializeField]
        private Vector2 _multiplierLimits = new Vector2(0.5f, 2f);

        [Tooltip("크기 변화에 걸리는 시간(초). 0이면 즉시.")]
        [SerializeField, Min(0f)]
        private float _transitionTime = 0.25f;

        [Tooltip("크기에 맞춰 Rigidbody 질량도 바꾼다(부피 비례).")]
        [SerializeField]
        private bool _scaleMass = false;

        [Tooltip("비워두면 같은 오브젝트의 Grabbable을 사용. 잡는 중에는 크기를 바꾸지 않는다.")]
        [SerializeField]
        private PointableElement _grabbable;

        private Vector3 _baseScale;
        private float _baseMass;
        private float _baseGripSize;
        private Rigidbody _rigidbody;
        private float _currentMultiplier = 1f;
        private bool _initialized;

        public float BaseGripSize => _baseGripSize;
        public float CurrentMultiplier => _currentMultiplier;
        public float TargetMultiplier { get; private set; } = 1f;

        private void Awake()
        {
            if (_grabbable == null) _grabbable = GetComponent<PointableElement>();
            _rigidbody = GetComponent<Rigidbody>();
            _baseScale = transform.localScale;
            if (_rigidbody != null) _baseMass = _rigidbody.mass;
            _baseGripSize = _gripSizeOverride > 0f ? _gripSizeOverride : MeasureGripSize();
            _initialized = true;
        }

        private void Update()
        {
            HandSizeEstimator estimator = HandSizeEstimator.Instance;
            if (estimator == null || !estimator.IsCalibrated) return;

            TargetMultiplier = ComputeMultiplier(estimator);

            if (IsGrabbed()) return;
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

        private float ComputeMultiplier(HandSizeEstimator estimator)
        {
            float m = 1f;
            switch (_mode)
            {
                case SizingMode.ProportionalToHand:
                    m = Mathf.Lerp(1f, estimator.HandScale, _strength);
                    break;

                case SizingMode.FitGripToHand:
                    if (_baseGripSize <= 0f) break;
                    float min = estimator.HandLength * _minGripRatio;
                    float max = estimator.HandLength * Mathf.Max(_maxGripRatio, _minGripRatio);
                    if (_baseGripSize < min) m = min / _baseGripSize;
                    else if (_baseGripSize > max) m = max / _baseGripSize;
                    break;
            }
            return Mathf.Clamp(m, _multiplierLimits.x, _multiplierLimits.y);
        }

        private void Apply()
        {
            transform.localScale = _baseScale * _currentMultiplier;
            if (_scaleMass && _rigidbody != null)
            {
                _rigidbody.mass = _baseMass * _currentMultiplier * _currentMultiplier * _currentMultiplier;
            }
        }

        private bool IsGrabbed() => _grabbable != null && _grabbable.SelectingPointsCount > 0;

        /// <summary>
        /// 자식 메시의 범위를 루트 로컬 축 기준으로 합쳐 실제(월드) 크기로 바꾼 뒤 잡는 축의 길이를 돌려준다.
        /// </summary>
        private float MeasureGripSize()
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
                    Bounds b = c.bounds;
                    Encapsulate(ref local, ref hasBounds, worldToRoot, b);
                }
            }
            if (!hasBounds) return 0f;

            Vector3 lossy = transform.lossyScale;
            Vector3 size = Vector3.Scale(local.size, new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
            switch (_gripAxis)
            {
                case GripAxis.X: return size.x;
                case GripAxis.Y: return size.y;
                case GripAxis.Z: return size.z;
                default: return Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            }
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
            if (_maxGripRatio < _minGripRatio) _maxGripRatio = _minGripRatio;
            if (_multiplierLimits.y < _multiplierLimits.x) _multiplierLimits.y = _multiplierLimits.x;
        }
    }
}
