using UnityEngine;

public class LimbAttachPointFollower : MonoBehaviour
{
    public LimbMesh limb;   // assign at runtime
    public int vertexIndex; // e.g., thumb tip index

    void OnEnable()
    {
        if (limb != null) limb.VerticesUpdated += OnVertsUpdated;
    }
    void OnDisable()
    {
        if (limb != null) limb.VerticesUpdated -= OnVertsUpdated;
    }

    private void OnVertsUpdated()
    {
        if (limb == null) return;
        if (vertexIndex < 0 || vertexIndex >= limb.VertexCount) return;

        // LimbMesh vertices are in the limb GameObject's local space
        transform.localPosition = limb.GetLocalVertex(vertexIndex);
        // Optional: orient the attach point to face outward; skip unless you compute normals/tangents.
        // transform.localRotation = Quaternion.identity;
    }
}
