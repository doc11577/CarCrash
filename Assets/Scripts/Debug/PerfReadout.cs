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

    [Tooltip("Optional. Shows the car's speed alongside the frame rate. Leave empty and it " +
             "picks up whatever car the garage spawned.")]
    public CarController car;

    GUIStyle style;
    string readout = "";
    float accum;
    int frames;
    float timer;
    float worstFrame;

    void Update()
    {
        if (car == null && PlayerCar.Current != null) car = PlayerCar.Current.Controller;

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

        // Live rigidbody count, because that is the thing most likely to cost the frame budget
        // and it cannot be read from the Inspector on the device the budget is about.
        int boulders = FallingBoulders.Live;
        if (boulders > 0) readout += $"\n{boulders} boulders live";

        // The AI field, for the same reason. A race is measured with eight cars or it is not a
        // measurement of a race — and a car that has fallen out of the world takes its cost with
        // it, which would otherwise look like the frame budget being comfortable.
        int ai = TrafficDriver.LiveCount;
        if (ai > 0) readout += $"\n{ai} AI cars";

        if (car != null)
            readout += $"\n{car.Speed * 3.6f:F0} km/h   {(car.Grounded ? "grounded" : "airborne")}";

        frames = 0;
        accum = 0f;
        timer = 0f;
        worstFrame = 0f;
    }

    void OnGUI()
    {
        // Hidden unless dev mode. It is a development tool with an IMGUI cost, and it has no
        // business sitting in the corner of a shipped build.
        if (!DevMode.Enabled) return;
        Draw();
    }

    void Draw()
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
