using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Press R to restart the run. Reloads the active scene behind a loading bar, the same
/// way the reference game does it.
///
/// A reload is a full reset — every car, every piece of debris, every bit of physics
/// state goes away. That is exactly what you want after a crash leaves wreckage
/// everywhere, and it costs nothing on the web because nothing is re-downloaded.
/// </summary>
public class RunRestart : MonoBehaviour
{
    [Tooltip("Key that restarts the run.")]
    public Key restartKey = Key.R;

    [Tooltip("Hold the bar on screen at least this long. Without it the reload is so fast the bar flashes for a single frame and reads as a glitch.")]
    public float minimumDisplayTime = 0.4f;

    [Tooltip("Ignore restarts for this long after a scene loads, so a held key doesn't loop.")]
    public float inputLockout = 0.3f;

    float armedAt;

    void OnEnable()
    {
        armedAt = Time.unscaledTime + inputLockout;
    }

    void Update()
    {
        if (Time.unscaledTime < armedAt) return;
        if (RestartOverlay.InProgress) return;

        // The dev tuner puts editable number boxes on the pause screen, and R would throw away
        // the run being tuned. The keypress arrives whether or not the field accepts the letter.
        if (UiKit.Typing()) return;

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb[restartKey].wasPressedThisFrame)
            Restart();
    }

    /// <summary>Restart the run. Safe to call from a UI button or a game-over state.</summary>
    public void Restart()
    {
        RestartOverlay.Begin(SceneManager.GetActiveScene().buildIndex, minimumDisplayTime);
    }
}

/// <summary>
/// Survives the scene change and draws the loading bar. Created on demand, destroys
/// itself once the new scene is running.
/// </summary>
public class RestartOverlay : MonoBehaviour
{
    public static bool InProgress { get; private set; }

    int sceneIndex;
    string sceneName;
    float minimumDisplayTime;
    float progress;
    Texture2D pixel;

    public static void Begin(int sceneIndex, float minimumDisplayTime)
    {
        Begin(sceneIndex, null, minimumDisplayTime);
    }

    /// <summary>
    /// Load a scene BY NAME behind the same bar. The menu needs this: it knows which map the
    /// player picked as a name, and a build index would have to be kept in step with the Scene
    /// List by hand, which is exactly the kind of number that goes stale silently.
    /// </summary>
    public static void Begin(string sceneName, float minimumDisplayTime)
    {
        Begin(-1, sceneName, minimumDisplayTime);
    }

    static void Begin(int sceneIndex, string sceneName, float minimumDisplayTime)
    {
        if (InProgress) return;
        InProgress = true;

        // A load started from a paused game would otherwise arrive in a frozen scene.
        Time.timeScale = 1f;

        GameObject host = new GameObject("RestartOverlay");
        DontDestroyOnLoad(host);

        RestartOverlay overlay = host.AddComponent<RestartOverlay>();
        overlay.sceneIndex = sceneIndex;
        overlay.sceneName = sceneName;
        overlay.minimumDisplayTime = minimumDisplayTime;
        overlay.StartCoroutine(overlay.Run());
    }

    System.Collections.IEnumerator Run()
    {
        // One frame so the overlay paints before the load stalls the main thread.
        yield return null;

        float startedAt = Time.unscaledTime;

        AsyncOperation load = string.IsNullOrEmpty(sceneName)
            ? SceneManager.LoadSceneAsync(sceneIndex)
            : SceneManager.LoadSceneAsync(sceneName);

        if (load == null)
        {
            // LoadSceneAsync returns null for a scene that is not in the Scene List, and
            // without this the coroutine would NullReference and leave InProgress stuck true,
            // which deadlocks every future load including restarts.
            // Unity's own message for this blames the build profile, which sends you to a list
            // that usually turns out to be correct -- because the far more common cause is a
            // MISTYPED NAME, and LoadSceneAsync cannot tell the two apart. Ask about the
            // spelling first, since that is what it usually is.
            Debug.LogError(
                $"RestartOverlay: cannot load scene '{sceneName ?? sceneIndex.ToString()}'.\n" +
                "CHECK THE SPELLING FIRST — it must match the scene asset exactly, and a typo " +
                "here looks identical to a missing Scene List entry. Then check File > Build " +
                "Profiles > Scene List actually contains it.");
            InProgress = false;
            Destroy(gameObject);
            yield break;
        }

        load.allowSceneActivation = false;

        // Unity reports 0.9 when a scene is loaded and waiting for activation.
        while (load.progress < 0.9f)
        {
            progress = load.progress / 0.9f;
            yield return null;
        }

        progress = 1f;

        while (Time.unscaledTime - startedAt < minimumDisplayTime)
            yield return null;

        load.allowSceneActivation = true;
        while (!load.isDone)
            yield return null;

        InProgress = false;
        Destroy(gameObject);
    }

    void OnGUI()
    {
        if (pixel == null)
        {
            pixel = new Texture2D(1, 1);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }

        float w = Screen.width;
        float h = Screen.height;

        // Ground
        GUI.color = new Color(0.09f, 0.07f, 0.10f, 1f);
        GUI.DrawTexture(new Rect(0f, 0f, w, h), pixel);

        float barW = Mathf.Min(320f, w * 0.5f);
        float barH = 14f;
        float x = (w - barW) * 0.5f;
        float y = h * 0.5f - barH * 0.5f;

        // Track
        GUI.color = new Color(0.22f, 0.18f, 0.24f, 1f);
        GUI.DrawTexture(new Rect(x, y, barW, barH), pixel);

        // Fill
        GUI.color = new Color(1f, 0.78f, 0.15f, 1f);
        GUI.DrawTexture(new Rect(x, y, barW * Mathf.Clamp01(progress), barH), pixel);

        GUI.color = Color.white;
    }

    void OnDestroy()
    {
        if (pixel != null) Destroy(pixel);
    }
}
