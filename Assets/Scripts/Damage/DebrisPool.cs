using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Recycles detached parts instead of instantiating and destroying them.
///
/// This exists because debris is the first system in this game capable of destroying the
/// frame rate. Three guards, all of them deliberate:
///   1. A hard cap on live pieces. Past the cap the oldest piece is stolen back, so the
///      count can never grow no matter how spectacular the crash.
///   2. A lifetime, after which a piece is returned.
///   3. Pieces that have come to rest are put to sleep and returned early, so a pile of
///      settled wreckage costs nothing.
/// </summary>
public class DebrisPool : MonoBehaviour
{
    public static DebrisPool Instance { get; private set; }

    [Tooltip("Hard cap on debris in the world at once. The oldest piece is recycled past this.")]
    public int maxLive = 24;

    [Tooltip("Seconds before a piece is returned to the pool.")]
    public float lifetime = 12f;

    [Tooltip("A piece resting below this speed for a moment is returned early.")]
    public float sleepSpeed = 0.35f;

    [Tooltip("Seconds a piece must stay below sleepSpeed before it is recycled.")]
    public float sleepDelay = 1.5f;

    class Piece
    {
        public GameObject go;
        public Rigidbody body;
        public GameObject prefab;
        public float releaseAt;
        public float restingSince;
    }

    readonly Dictionary<GameObject, Stack<GameObject>> idle = new Dictionary<GameObject, Stack<GameObject>>();
    readonly List<Piece> live = new List<Piece>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Throw a piece of debris into the world. Inherits the car's velocity so it trails
    /// behind convincingly instead of dropping straight down.
    /// </summary>
    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
                            Vector3 velocity, Vector3 angularVelocity)
    {
        if (prefab == null) return null;

        while (live.Count >= maxLive && live.Count > 0)
            Recycle(0);

        GameObject go = Take(prefab);
        if (go == null) return null;

        go.transform.SetPositionAndRotation(position, rotation);
        go.SetActive(true);

        Rigidbody body = go.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = false;
            body.linearVelocity = velocity;
            body.angularVelocity = angularVelocity;
            body.WakeUp();
        }

        live.Add(new Piece
        {
            go = go,
            body = body,
            prefab = prefab,
            releaseAt = Time.time + lifetime,
            restingSince = -1f
        });

        return go;
    }

    /// <summary>
    /// Adopt an object that is already in the world and manage its lifetime as debris.
    ///
    /// Real detached panels cannot be pooled: each one is a unique child of a specific car,
    /// not an instance of a prefab, and <see cref="CarDamage.Repair"/> needs to bolt that
    /// exact object back on. Adopted pieces still count against <see cref="maxLive"/> and
    /// still expire and sleep, so a car shedding eight panels cannot escape the budget --
    /// they are simply deactivated on recycle rather than pushed onto an idle stack.
    /// </summary>
    public void Track(GameObject go, Rigidbody body)
    {
        if (go == null) return;

        while (live.Count >= maxLive && live.Count > 0)
            Recycle(0);

        live.Add(new Piece
        {
            go = go,
            body = body,
            prefab = null,
            releaseAt = Time.time + lifetime,
            restingSince = -1f
        });
    }

    /// <summary>
    /// Stop managing an adopted object. Call this when a panel is bolted back on, otherwise
    /// the stale live entry expires later and deactivates a panel that is now part of a car.
    /// </summary>
    public void Forget(GameObject go)
    {
        if (go == null) return;

        for (int i = live.Count - 1; i >= 0; i--)
            if (live[i].go == go) live.RemoveAt(i);
    }

    void Update()
    {
        float now = Time.time;

        for (int i = live.Count - 1; i >= 0; i--)
        {
            Piece piece = live[i];

            if (piece.go == null)
            {
                live.RemoveAt(i);
                continue;
            }

            if (now >= piece.releaseAt)
            {
                Recycle(i);
                continue;
            }

            if (piece.body == null) continue;

            if (piece.body.linearVelocity.sqrMagnitude < sleepSpeed * sleepSpeed)
            {
                if (piece.restingSince < 0f) piece.restingSince = now;
                else if (now - piece.restingSince > sleepDelay) Recycle(i);
            }
            else
            {
                piece.restingSince = -1f;
            }
        }
    }

    void Recycle(int index)
    {
        Piece piece = live[index];
        live.RemoveAt(index);

        if (piece.go == null) return;

        if (piece.body != null)
        {
            piece.body.linearVelocity = Vector3.zero;
            piece.body.angularVelocity = Vector3.zero;
        }

        piece.go.SetActive(false);

        // An adopted piece (prefab == null) belongs to a car, not to the pool. Leave its
        // transform alone so Repair can find it and put it back where it came from.
        if (piece.prefab == null) return;

        piece.go.transform.SetParent(transform, false);
        Give(piece.prefab, piece.go);
    }

    GameObject Take(GameObject prefab)
    {
        if (idle.TryGetValue(prefab, out Stack<GameObject> stack) && stack.Count > 0)
            return stack.Pop();

        GameObject go = Instantiate(prefab, transform);
        go.SetActive(false);
        return go;
    }

    void Give(GameObject prefab, GameObject go)
    {
        if (!idle.TryGetValue(prefab, out Stack<GameObject> stack))
        {
            stack = new Stack<GameObject>();
            idle[prefab] = stack;
        }

        stack.Push(go);
    }
}
