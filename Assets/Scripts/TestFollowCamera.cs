using UnityEngine;

// Placeholder chase cam for the pipeline smoke test.
// The real rig (pitches with slope, pulls back with speed) replaces this.
public class TestFollowCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 4f, -8f);
    public float followSharpness = 6f;

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = target.position + target.TransformDirection(offset);

        transform.position = Vector3.Lerp(
            transform.position,
            desired,
            1f - Mathf.Exp(-followSharpness * Time.deltaTime));

        transform.LookAt(target.position + Vector3.up * 1f);
    }
}
