using UnityEngine;

/// <summary>
/// 플레이어 VR 리그 루트 마커 (CRC_Assessment 에서 이식).
/// 리그 루트(Player_CRC)에 부착한다.
/// 위치/회전 조정 코드는 리그 구조를 몰라도 PlayerVrRig.Current.transform 만 다루면 된다.
/// </summary>
public class PlayerVrRig : MonoBehaviour
{
    public static PlayerVrRig Current { get; private set; }

    private void Awake()
    {
        Current = this;
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
    }
}
