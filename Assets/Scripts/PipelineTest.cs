using UnityEngine;
using UnityEngine.InputSystem;

// Throwaway smoke test. Proves input, physics, rendering and the Web deploy
// pipeline all work on a target Chromebook. Delete once the real car exists.
public class PipelineTest : MonoBehaviour
{
    public float acceleration = 30f;
    public float turnSpeed = 140f;
    public float maxSpeed = 25f;

    Rigidbody rb;
    GUIStyle style;
    string readout = "";
    float accum;
    int frames;
    float sampleTimer;
    float worstFrame;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        frames++;
        accum += Time.unscaledDeltaTime;
        sampleTimer += Time.unscaledDeltaTime;

        if (Time.unscaledDeltaTime > worstFrame)
            worstFrame = Time.unscaledDeltaTime;

        if (sampleTimer >= 0.5f)
        {
            float fps = frames / accum;
            float worstMs = worstFrame * 1000f;

            readout =
                $"FPS {fps:F0}   worst frame {worstMs:F1} ms\n" +
                $"{Screen.width}x{Screen.height}\n" +
                $"{SystemInfo.graphicsDeviceType}\n" +
                $"{SystemInfo.graphicsDeviceName}\n" +
                $"{SystemInfo.processorCount} cores / {SystemInfo.systemMemorySize} MB\n" +
                $"speed {rb.linearVelocity.magnitude:F1}";

            frames = 0;
            accum = 0f;
            sampleTimer = 0f;
            worstFrame = 0f;
        }
    }

    void FixedUpdate()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        float throttle = 0f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1f;

        float steer = 0f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;

        if (rb.linearVelocity.magnitude < maxSpeed)
            rb.AddForce(transform.forward * (throttle * acceleration), ForceMode.Acceleration);

        Quaternion turn = Quaternion.Euler(0f, steer * turnSpeed * Time.fixedDeltaTime, 0f);
        rb.MoveRotation(rb.rotation * turn);
    }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.fontSize = 16;
            style.normal.textColor = Color.white;
        }

        GUI.Box(new Rect(8, 8, 300, 130), GUIContent.none);
        GUI.Label(new Rect(16, 12, 300, 130), readout, style);
    }
}
