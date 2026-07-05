using System;
using System.IO;
using UnityEngine;

public class SMPLMesh {
    private GameObject gameObject;
    private Mesh mesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    public string meshTemplatePath;

    public SMPLMesh(GameObject gmObj) {
        gameObject = gmObj;
        gameObject.transform.localScale = new Vector3(1.0f, 1.0f, -1.0f);

        mesh = new Mesh();
        ApplyMesh(mesh);

        meshTemplatePath = $"{Application.streamingAssetsPath}/smpl_template.json";
        
        string json = ReadJsonFile(meshTemplatePath);
        MeshData meshData = JsonUtility.FromJson<MeshData>(json);
        UpdateMesh(meshData);
    }

    string ReadJsonFile(string path)
    {
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        else
        {
            Debug.LogError("File not found: " + path);
            return null;
        }
    }
    void ApplyMesh(Mesh newMesh) {
        meshFilter = gameObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
        }
        meshFilter.mesh = newMesh;

        meshRenderer = gameObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }
    }

    public void UpdateMesh(MeshData newMeshData) {        
        Vector3[] vertices = new Vector3[newMeshData.vertices.Length / 3];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new Vector3(newMeshData.vertices[i * 3], newMeshData.vertices[i * 3 + 1], newMeshData.vertices[i * 3 + 2]);
        }

        Vector2[] uv = null;
        if (newMeshData.uv != null && newMeshData.uv.Length == newMeshData.vertices.Length / 3 * 2)
        {
            uv = new Vector2[newMeshData.uv.Length / 2];
            for (int i = 0; i < uv.Length; i++)
            {
                uv[i] = new Vector2(newMeshData.uv[i * 2], newMeshData.uv[i * 2 + 1]);
            }
        }

        mesh.Clear();

        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.triangles = newMeshData.triangles;
        if (uv != null) mesh.uv = uv;
            
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    public void UpdateVertices(MeshData newMeshData) {
        if (mesh == null)
        {
            Debug.LogError("Mesh has not been initialized.");
            return;
        }

        if (newMeshData.vertices.Length != mesh.vertexCount * 3)
        {
            Debug.LogError("New vertices array length does not match the current mesh vertices length.");
            return;
        }

        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i].x = newMeshData.vertices[i * 3];
            vertices[i].y = newMeshData.vertices[i * 3 + 1];
            vertices[i].z = newMeshData.vertices[i * 3 + 2];
        }

        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        // Update the mesh filter
        meshFilter.mesh = mesh;
    }

}

