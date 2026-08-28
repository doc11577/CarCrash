using UnityEngine;

/// <summary>
/// On-screen frame rate and device readout. Exists because a school Chromebook gives you
/// no console — this is the only way to get real numbers off the target hardware.
///
/// Uses IMGUI, which costs a few percent of the frame it is measuring. Fine for
/// development. Delete it, or untick it, before shipping.
/// </summary>
public class PerfReadout : MonoBehaviour
{
    [Tooltip("How often the numbers refresh, in seconds.")]
    public float sampleInterval = 0.5f;

    [Tooltip("Optional. Shows the car's speed alongside the frame rate.")]
    public CarController car;

    GUIStyle style;
    string readout = "";
    float accum;
    int frames;
    float timer;
    float worstFrame;

    void Update()
    {
        frames++;
        accum += Time.unscaledDeltaTime;
        timer += Time.unscaledDeltaTime;

        if (Time.unscaledDeltaTime > worstFrame)
            worstFrame = Time.unscaledDeltaTime;

        if (timer < sampleInterval) return;

        float fps = frames / accum;

        readout =
            $"FPS {fps:F0}   worst {worstFrame * 1000f:F1} ms\n" +
            $"{Screen.width}x{Screen.height}\n" +
            $"{SystemInfo.graphicsDeviceType}\n" +
            $"{SystemInfo.graphicsDeviceName}\n" +
            $"{SystemInfo.processorCount} cores / {SystemInfo.systemMemorySize} MB";

        if (car != null)
            readout += $"\n{car.Speed * 3.6f:F0} km/h   {(car.Grounded ? "grounded" : "airborne")}";

        frames = 0;
        accum = 0f;
        timer = 0f;
        worstFrame = 0f;
    }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.fontSize = 16;
            style.normal.textColor = Color.white;
        }

        GUI.Box(new Rect(8, 8, 300, car != null ? 152 : 130), GUIContent.none);
        GUI.Label(new Rect(16, 12, 300, 160), readout, style);
    }
}
