using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class InvertMeshVertices : MonoBehaviour
{
    // Quaternion for rotation (editable in the inspector)
    public Quaternion Q = new Quaternion(0, 0, 0, 1);

    void Start()
    {
        // Get the MeshFilter component
        MeshFilter meshFilter = GetComponent<MeshFilter>();

        if (meshFilter != null)
        {
            // Get the original mesh
            Mesh mesh = meshFilter.mesh;

            // Get the vertices of the mesh
            Vector3[] vertices = mesh.vertices;

            Vector3 mean = new Vector3(0, 0, 0);
            // Invert each vertex position
            for (int i = 0; i < vertices.Length; i++) {
                mean += vertices[i];
                Debug.Log($"{i}" + vertices[i]);
            }

            mean /= vertices.Length;            
            Debug.Log("mean: " + mean);
        
            // Apply the inverted vertices back to the mesh
            // mesh.vertices = vertices;

            // Recalculate normals and bounds for proper rendering
            // mesh.RecalculateNormals();
            // mesh.RecalculateBounds();

            // Apply the rotation to the object
            // transform.rotation = new Quaternion(-Q.x, Q.y, -Q.z, Q.w);
        }
        else
        {
            Debug.LogError("MeshFilter component not found on this GameObject.");
        }
    }

    void Update()
    {
    //     Mesh mesh = GetComponent<MeshFilter>().mesh;
    //     Vector3[] vertices = mesh.vertices;
    //     Vector3[] normals = mesh.normals;

    //    for (var i = 0; i < vertices.Length; i++)
    //     {
    //         vertices[i] = new Vector3(vertices[i].x, vertices[i].y, -vertices[i].z);
    //     }
    //     Debug.Log("vertices: Written");
    //    mesh.vertices = vertices;
    }

}
