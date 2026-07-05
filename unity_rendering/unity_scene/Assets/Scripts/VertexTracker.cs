using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class VertexTracker : MonoBehaviour
{
    [Header("Target Mesh (assign ONE of these)")]
    public MeshFilter meshFilter;                       // Static mesh
    public SkinnedMeshRenderer skinnedMeshRenderer;     // Skinned/animated mesh

    [Header("Vertex Settings")]
    public int vertexIndex = 0;                    // Index of the vertex to track
    public bool drawGizmoAtVertex = true;          // Small sphere at the tracked vertex
    public float gizmoSize = 0.005f;

    [Header("Path Settings")]
    public float minDistanceToAdd = 0.0025f;       // Add a new point if moved this far (meters)
    public int maxPoints = 20000;                  // Safety cap
    public bool worldSpace = true;                 // Keep path in world space

    [Header("Sampling")]
    public int sampleEveryNFrames = 1;             // Skip frames if you need performance

    [Header("Click-to-Pick (optional)")]
    public Camera pickCamera;                      // If null, uses Camera.main
    public KeyCode pickModifier = KeyCode.LeftControl;

    private LineRenderer lr;
    private readonly List<Vector3> points = new();
    private Mesh baked; // reused for skinned
    private int frameCounter = 0;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = 0;
        lr.useWorldSpace = worldSpace;

        if (skinnedMeshRenderer != null && baked == null)
            baked = new Mesh();

        ValidateVertexIndex();
        AddPoint(GetVertexWorldPosition()); // seed first point
    }

    void Update()
    {
        // Optional click-to-pick
        HandlePicking();

        if ((frameCounter++ % Mathf.Max(1, sampleEveryNFrames)) != 0)
            return;

        Vector3 p = GetVertexWorldPosition();
        if (points.Count == 0 || (p - points[^1]).sqrMagnitude >= minDistanceToAdd * minDistanceToAdd)
            AddPoint(p);
    }

    private void AddPoint(Vector3 p)
    {
        if (points.Count >= maxPoints) return;
        points.Add(p);
        lr.positionCount = points.Count;
        lr.SetPositions(points.ToArray());
    }

    private void ValidateVertexIndex()
    {
        int count = 0;
        if (meshFilter && meshFilter.sharedMesh) count = meshFilter.sharedMesh.vertexCount;
        else if (skinnedMeshRenderer && skinnedMeshRenderer.sharedMesh) count = skinnedMeshRenderer.sharedMesh.vertexCount;

        if (count == 0) return;
        vertexIndex = Mathf.Clamp(vertexIndex, 0, count - 1);
    }

    private Vector3 GetVertexWorldPosition()
    {
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            var v = meshFilter.sharedMesh.vertices[vertexIndex];
            return meshFilter.transform.TransformPoint(v);
        }
        else if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
        {
            // Bake current deformed vertices
            skinnedMeshRenderer.BakeMesh(baked);
            var v = baked.vertices[vertexIndex]; // baked mesh is in skinned local space
            return skinnedMeshRenderer.transform.TransformPoint(v);
        }
        else
        {
            return transform.position;
        }
    }

    private void HandlePicking()
    {
        if (!Input.GetMouseButtonDown(0) || !Input.GetKey(pickModifier))
            return;

        var cam = pickCamera != null ? pickCamera : Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 1000f))
        {
            // We require a MeshCollider on the hit object to access triangle data
            var mc = hit.collider as MeshCollider;
            if (mc == null || mc.sharedMesh == null) return;

            var mesh = mc.sharedMesh;
            int triIndex = hit.triangleIndex * 3;
            if (triIndex < 0 || triIndex + 2 >= mesh.triangles.Length) return;

            int i0 = mesh.triangles[triIndex + 0];
            int i1 = mesh.triangles[triIndex + 1];
            int i2 = mesh.triangles[triIndex + 2];

            // Pick the closest of the triangle's three vertices to the hit point
            var t = mc.transform;
            Vector3 p = hit.point;
            Vector3 v0 = t.TransformPoint(mesh.vertices[i0]);
            Vector3 v1 = t.TransformPoint(mesh.vertices[i1]);
            Vector3 v2 = t.TransformPoint(mesh.vertices[i2]);

            float d0 = (p - v0).sqrMagnitude;
            float d1 = (p - v1).sqrMagnitude;
            float d2 = (p - v2).sqrMagnitude;

            int newIndex = i0;
            float best = d0;
            if (d1 < best) { best = d1; newIndex = i1; }
            if (d2 < best) { best = d2; newIndex = i2; }

            vertexIndex = newIndex;
            // Clear and seed new path
            points.Clear();
            lr.positionCount = 0;
            AddPoint(GetVertexWorldPosition());
        }
    }

    void OnDrawGizmos()
    {
        if (!drawGizmoAtVertex) return;

        Vector3 p = Application.isPlaying ? GetVertexWorldPosition() : PreviewVertexPositionInEditor();
        Gizmos.DrawWireSphere(p, gizmoSize);
    }

    private Vector3 PreviewVertexPositionInEditor()
    {
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            var v = meshFilter.sharedMesh.vertices[Mathf.Clamp(vertexIndex, 0, meshFilter.sharedMesh.vertexCount - 1)];
            return meshFilter.transform.TransformPoint(v);
        }
        if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
        {
            // In edit mode we don't have baked deformation; show bind pose vertex
            var v = skinnedMeshRenderer.sharedMesh.vertices[Mathf.Clamp(vertexIndex, 0, skinnedMeshRenderer.sharedMesh.vertexCount - 1)];
            return skinnedMeshRenderer.transform.TransformPoint(v);
        }
        return transform.position;
    }
}
