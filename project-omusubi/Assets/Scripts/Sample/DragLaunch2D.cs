using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class DragAnywhereLaunch2D : MonoBehaviour
{
    // ====== 外部へ配るドラッグ情報 ======
    public struct DragSnapshot
    {
        public Vector3 startWorld;
        public Vector3 currentWorld;
        public Vector2 dragWorld;        // 生のドラッグ(start→current)
        public Vector2 aimVectorWorld;   // 反転/Clamp後(発射ベクトル)
        public float t;                  // 0..1 (距離/最大距離)
        public float aimDeg;             // 角度[deg]
        public Vector2 impulse;          // 付与インパルス
    }

    // ====== イベント ======
    public event Action<DragSnapshot> OnDragStart;
    public event Action<DragSnapshot> OnDragUpdate;
    public event Action<DragSnapshot> OnDragRelease;

    [Header("Launch Tuning")]
    public float powerPerUnit = 8f;
    public float maxDragDistance = 3f;
    public float minLaunchImpulse = 0.6f;
    public float readySpeedThreshold = 0.15f;

    [Header("Visual (optional)")]
    public LineRenderer aimLine;
    public LineRenderer trajectoryLine;
    public bool showTrajectory = true;
    public int trajectoryPoints = 24;
    public float trajectoryTimeStep = 0.05f;

    [Header("Options")]
    [Tooltip("true: ドラッグ方向に発射 / false: スリングショット(逆向き)")]
    public bool sameDirectionAsDrag = true;

    // ====== 保持/解放 ======
    [Header("Hold / Release")]
    [Tooltip("投げるまで親にする(手のソケットTransform)")]
    public Transform holdParent;
    [Tooltip("手の中でのローカル位置")]
    public Vector3 holdLocalPosition = Vector3.zero;
    [Tooltip("手の中でのZ回転角(度)")]
    public float holdLocalZAngle = 0f;
    [Tooltip("保持中はColliderを無効化する（誤当たり防止）")]
    public bool disableColliderWhileHeld = true;

    Rigidbody2D rb;
    Collider2D[] cols;
    Camera cam;

    bool dragging;
    bool isHeld = false;
    Vector3 dragStartWorld;
    Vector2 aimVectorWorld;
    float sqrReady;

    // --- Unity 6 互換 ---
    Vector2 CurrentLinearVelocity
    {
        get
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }
        set
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = value;
#else
            rb.velocity = value;
#endif
        }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        cols = GetComponents<Collider2D>();
        cam = Camera.main;
        sqrReady = readySpeedThreshold * readySpeedThreshold;

        if (!aimLine)        aimLine = CreateLine("AimLine", 0.05f);
        if (!trajectoryLine) trajectoryLine = CreateLine("TrajectoryLine", 0.03f);
        aimLine.enabled = false;
        trajectoryLine.enabled = false;

        // 手が指定されていれば開始時に“保持”
        if (holdParent) AttachToHand();
    }

    // --- 公開: 投げ終わったあとに再装填したい時などに使える ---
    public void AttachToHand()
    {
        isHeld = true;
        // 物理停止
        rb.simulated = false;            // 2D物理から完全除外
        CurrentLinearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        // 当たり停止（必要なら）
        if (disableColliderWhileHeld && cols != null)
            foreach (var c in cols) if (c) c.enabled = false;

        // 手に保持（ローカル位置・角度で合わせる）
        transform.SetParent(holdParent, worldPositionStays: true);
        transform.localPosition = holdLocalPosition;

        var e = transform.localEulerAngles; e.z = holdLocalZAngle; transform.localEulerAngles = e;
    }

    LineRenderer CreateLine(string name, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 0;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startWidth = lr.endWidth = width;
        lr.numCapVertices = 4;
        lr.useWorldSpace = true;
        lr.sortingOrder = 10;
        return lr;
    }

    void Update()
    {
        // 入力開始（画面内 & ほぼ停止時）
        if (!dragging && Input.GetMouseButtonDown(0) && IsPointerInScreen() && CurrentLinearVelocity.sqrMagnitude <= sqrReady)
        {
            dragging = true;
            dragStartWorld = GetMouseWorldOnZ(transform.position.z);

            var ss0 = BuildSnapshot(GetMouseWorldOnZ(transform.position.z));
            OnDragStart?.Invoke(ss0);

            aimLine.enabled = true;
            if (showTrajectory) trajectoryLine.enabled = true;
        }

        if (!dragging) return;

        // ドラッグ更新
        var now = GetMouseWorldOnZ(transform.position.z);
        var ss = BuildSnapshot(now);

        // 見た目（任意）
        aimLine.positionCount = 2;
        aimLine.SetPosition(0, transform.position);
        aimLine.SetPosition(1, transform.position + (Vector3)aimVectorWorld);
        if (showTrajectory)
            DrawTrajectory(transform.position, InitialVelocityFromImpulse(ss.impulse));

        OnDragUpdate?.Invoke(ss);

        // リリース
        if (Input.GetMouseButtonUp(0))
        {
            dragging = false;

            // まず“手から離す”→ 物理ON
            if (isHeld) DetachFromHand();

            if (ss.impulse.magnitude >= minLaunchImpulse)
                rb.AddForce(ss.impulse, ForceMode2D.Impulse);

            OnDragRelease?.Invoke(ss);

            aimLine.enabled = false;
            trajectoryLine.enabled = false;
            trajectoryLine.positionCount = 0;
        }
    }

    void DetachFromHand()
    {
        // 親を外す（ワールド座標を維持）
        transform.SetParent(null, worldPositionStays: true);

        // 当たりを戻す
        if (disableColliderWhileHeld && cols != null)
            foreach (var c in cols) if (c) c.enabled = true;

        // 物理起動
        rb.simulated = true;
        isHeld = false;
    }

    DragSnapshot BuildSnapshot(Vector3 currentWorld)
    {
        Vector2 raw = (Vector2)(currentWorld - dragStartWorld);
        Vector2 vec = sameDirectionAsDrag ? raw : -raw;

        aimVectorWorld = Vector2.ClampMagnitude(vec, maxDragDistance);

        float t = Mathf.InverseLerp(0f, maxDragDistance, aimVectorWorld.magnitude);
        float aimDeg = Mathf.Atan2(aimVectorWorld.y, aimVectorWorld.x) * Mathf.Rad2Deg;
        Vector2 impulse = aimVectorWorld * powerPerUnit;

        return new DragSnapshot
        {
            startWorld     = dragStartWorld,
            currentWorld   = currentWorld,
            dragWorld      = raw,
            aimVectorWorld = aimVectorWorld,
            t              = t,
            aimDeg         = aimDeg,
            impulse        = impulse
        };
    }

    bool IsPointerInScreen()
    {
        var p = Input.mousePosition;
        return (p.x >= 0 && p.x <= Screen.width && p.y >= 0 && p.y <= Screen.height);
    }

    Vector3 GetMouseWorldOnZ(float z)
    {
        float screenZ = cam.WorldToScreenPoint(new Vector3(0,0,z)).z;
        var mp = Input.mousePosition; mp.z = screenZ;
        return cam.ScreenToWorldPoint(mp);
    }

    Vector2 InitialVelocityFromImpulse(Vector2 impulse)
    {
        float m = rb.mass;
        return impulse / (m > 0f ? m : 1f);
    }

    void DrawTrajectory(Vector3 startPos, Vector2 v0)
    {
        Vector2 g = Physics2D.gravity * rb.gravityScale;
        trajectoryLine.positionCount = trajectoryPoints;
        for (int i = 0; i < trajectoryPoints; i++)
        {
            float t = (i + 1) * trajectoryTimeStep;
            Vector2 pos = (Vector2)startPos + v0 * t + 0.5f * g * t * t;
            trajectoryLine.SetPosition(i, pos);
        }
    }
}
