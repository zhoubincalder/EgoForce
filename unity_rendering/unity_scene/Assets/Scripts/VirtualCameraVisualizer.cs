using UnityEngine;

// Attach this to your "VirtualCamera" GameObject.
// It will draw four lines in a pyramid shape, pointing forward from the camera,
// so you can see a simple “view frustum” pointing at the hands.

[RequireComponent(typeof(LineRenderer))]
public class VirtualCameraVisualizer : MonoBehaviour
{
    [Tooltip("Distance from camera to the near‐plane of the frustum.")]
    public float frustumDepth = 2.0f;

    [Tooltip("Half‐width (in world units) of the frustum at that depth.")]
    public float frustumHalfWidth = 0.5f;

    [Tooltip("Half‐height (in world units) of the frustum at that depth.")]
    public float frustumHalfHeight = 0.3f;

    private LineRenderer _lr;

    void Awake()
    {
        // Ensure there's a LineRenderer on this GameObject
        _lr = GetComponent<LineRenderer>();
        _lr.positionCount = 8;          // 4 lines × 2 points each = 8
        _lr.loop = false;
        _lr.widthMultiplier = 0.01f;     // thin lines
        _lr.material = new Material(Shader.Find("Sprites/Default")); 
        _lr.startColor = Color.green;
        _lr.endColor = Color.green;
    }

    void Update()
    {
        // Define the 4 “corners” of a rectangle at distance = frustumDepth in front of camera
        Vector3 forward = transform.forward * frustumDepth;
        Vector3 center = transform.position + forward;

        Vector3 topLeft     = center + (-transform.right * frustumHalfWidth) + (transform.up * frustumHalfHeight);
        Vector3 topRight    = center + ( transform.right * frustumHalfWidth) + (transform.up * frustumHalfHeight);
        Vector3 bottomLeft  = center + (-transform.right * frustumHalfWidth) + (-transform.up * frustumHalfHeight);
        Vector3 bottomRight = center + ( transform.right * frustumHalfWidth) + (-transform.up * frustumHalfHeight);

        // Now set up the 4 line segments: from cam → each corner
        // LineRenderer uses an array of positions, connecting them in order.
        // We'll do it as pairs: [cam, topLeft, cam, topRight, cam, bottomRight, cam, bottomLeft]
        Vector3 camPos = transform.position;

        _lr.positionCount = 8;
        _lr.SetPosition(0, camPos);
        _lr.SetPosition(1, topLeft);

        _lr.SetPosition(2, camPos);
        _lr.SetPosition(3, topRight);

        _lr.SetPosition(4, camPos);
        _lr.SetPosition(5, bottomRight);

        _lr.SetPosition(6, camPos);
        _lr.SetPosition(7, bottomLeft);
    }
}
