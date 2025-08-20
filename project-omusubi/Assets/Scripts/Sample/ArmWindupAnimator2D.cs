using System.Collections;
using UnityEngine;

/// <summary>
/// DragAnywhereLaunch2D のイベントを受け取り、腕の回転だけを制御する。
/// 入力は一切読まない（単一入力元ポリシー）。
/// </summary>
public class ArmWindupFromLauncher : MonoBehaviour
{
    [Header("Bones (shoulder required)")]
    public Transform shoulder;
    public Transform forearm;
    public Transform hand;

    [Header("Hook")]
    public DragAnywhereLaunch2D launcher;   // おむすび側(イベント発行元)

    [Header("Windup Angles (deg)")]
    public float shoulderBackRange = 60f;
    public float forearmBackRange  = 40f;
    public float handBackRange     = 20f;

    [Tooltip("狙い方向に腕を寄せる度合い(0..1)")]
    [Range(0,1)] public float aimInfluence = 0.35f;

    [Header("Release Snap")]
    public float snapOvershoot = 18f;
    public float snapDuration  = 0.08f;
    public float settleDuration= 0.20f;
    public AnimationCurve snapEase = AnimationCurve.EaseInOut(0,0,1,1);

    [Header("Facing")]
    public bool autoFlipByScaleX = true;
    public bool facingRight = true;

    [Header("Smoothing")]
    public float windupSmoothTime = 0.06f;

    // internal
    float sNeutral, fNeutral, hNeutral;
    float vS, vF, vH;
    Coroutine snapCo;

    void Reset()
    {
        // 近くの発射体を自動検索（任意）
        if (!launcher) launcher = FindObjectOfType<DragAnywhereLaunch2D>();
    }

    void OnEnable()
    {
        if (!shoulder) { Debug.LogWarning("[ArmWindupFromLauncher] Shoulder is required."); enabled = false; return; }
        sNeutral = Normalize180(shoulder.localEulerAngles.z);
        if (forearm) fNeutral = Normalize180(forearm.localEulerAngles.z);
        if (hand)   hNeutral = Normalize180(hand.localEulerAngles.z);

        if (launcher)
        {
            launcher.OnDragStart  += HandleStart;
            launcher.OnDragUpdate += HandleUpdatePose;
            launcher.OnDragRelease+= HandleRelease;
        }
        else
        {
            Debug.LogWarning("[ArmWindupFromLauncher] launcher is not assigned.");
        }
    }

    void OnDisable()
    {
        if (launcher)
        {
            launcher.OnDragStart  -= HandleStart;
            launcher.OnDragUpdate -= HandleUpdatePose;
            launcher.OnDragRelease-= HandleRelease;
        }
    }

    void HandleStart(DragAnywhereLaunch2D.DragSnapshot ss)
    {
        // 途中でスナップ中だったら止める
        if (snapCo != null) StopCoroutine(snapCo);
    }

    void HandleUpdatePose(DragAnywhereLaunch2D.DragSnapshot ss)
    {
        float dir = GetFacingSign();
        float t   = ss.t;
        float aim = ss.aimDeg;

        float sTarget = sNeutral + (-dir) * shoulderBackRange * t + dir * aimInfluence * aim;
        float fTarget = fNeutral + (-dir) * forearmBackRange  * t + dir * aimInfluence * 0.6f * aim;
        float hTarget = hNeutral + (-dir) * handBackRange     * t + dir * aimInfluence * 0.3f * aim;

        SetLocalZ(shoulder, Mathf.SmoothDampAngle(Normalize180(shoulder.localEulerAngles.z), sTarget, ref vS, windupSmoothTime));
        if (forearm) SetLocalZ(forearm, Mathf.SmoothDampAngle(Normalize180(forearm.localEulerAngles.z),  fTarget, ref vF, windupSmoothTime));
        if (hand)   SetLocalZ(hand,   Mathf.SmoothDampAngle(Normalize180(hand  .localEulerAngles.z),     hTarget, ref vH, windupSmoothTime));
    }

    void HandleRelease(DragAnywhereLaunch2D.DragSnapshot ss)
    {
        if (snapCo != null) StopCoroutine(snapCo);
        snapCo = StartCoroutine(SnapAndSettle(ss.aimDeg));
    }

    IEnumerator SnapAndSettle(float aimDeg)
    {
        float dir = GetFacingSign();

        float s0 = Normalize180(shoulder.localEulerAngles.z);
        float f0 = forearm ? Normalize180(forearm.localEulerAngles.z) : 0f;
        float h0 = hand   ? Normalize180(hand  .localEulerAngles.z) : 0f;

        float s1 = sNeutral + dir * (aimInfluence * aimDeg + snapOvershoot);
        float f1 = forearm ? fNeutral + dir * (0.6f * aimInfluence * aimDeg + snapOvershoot * 0.6f) : 0f;
        float h1 = hand   ? hNeutral + dir * (0.3f * aimInfluence * aimDeg + snapOvershoot * 0.3f) : 0f;

        // snap
        for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.0001f, snapDuration))
        {
            float k = snapEase.Evaluate(Mathf.Clamp01(t));
            SetLocalZ(shoulder, Mathf.LerpAngle(s0, s1, k));
            if (forearm) SetLocalZ(forearm, Mathf.LerpAngle(f0, f1, k));
            if (hand)   SetLocalZ(hand,   Mathf.LerpAngle(h0, h1, k));
            yield return null;
        }

        // settle
        for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.0001f, settleDuration))
        {
            float k = Mathf.SmoothStep(0,1, t);
            SetLocalZ(shoulder, Mathf.LerpAngle(s1, sNeutral, k));
            if (forearm) SetLocalZ(forearm, Mathf.LerpAngle(f1, fNeutral, k));
            if (hand)   SetLocalZ(hand,   Mathf.LerpAngle(h1, hNeutral, k));
            yield return null;
        }
        snapCo = null;
    }

    // ---- utils ----
    float GetFacingSign()
    {
        if (autoFlipByScaleX) return transform.lossyScale.x >= 0 ? 1f : -1f;
        return facingRight ? 1f : -1f;
    }
    static float Normalize180(float deg){ deg%=360f; if (deg>180f) deg-=360f; if (deg<-180f) deg+=360f; return deg; }
    static void SetLocalZ(Transform t, float deg){ var e=t.localEulerAngles; e.z=deg; t.localEulerAngles=e; }
}
