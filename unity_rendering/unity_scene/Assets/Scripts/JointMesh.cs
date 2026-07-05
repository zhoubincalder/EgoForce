using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class JointMesh
{
    public string JointTemplateJsonFile = "body16_template.json";

    public readonly GameObject spheresParent;
    public readonly GameObject[] spheres;
    public readonly LineRenderer[] lineRenderers;
    public float sphereScale = 5;
    public float lineWidth = 1;
    private int[] connectorsIndices;
    private int JointsNum;

    private string readJsonFile() {
        string path = $"{Application.streamingAssetsPath}/{JointTemplateJsonFile}";

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

    private void LoadJointTemplate() {
        string json = readJsonFile();
        JointMeshData jointMeshData = JsonUtility.FromJson<JointMeshData>(json);

        JointsNum = jointMeshData.nKeypoints;
        connectorsIndices = jointMeshData.kinematicTree;
    }
    public JointMesh(GameObject gmObj)
    {
        LoadJointTemplate();

        spheresParent = gmObj;
        spheresParent.transform.localScale = new Vector3(1.0f, 1.0f, -1.0f);

        spheres = new GameObject[JointsNum];
        lineRenderers = new LineRenderer[JointsNum];
        
        for (int i = 0; i < connectorsIndices.Length / 2; i++)
        {
            GameObject lnObject = new GameObject();
            lnObject.name = "Bone_" + i;
            lnObject.transform.parent = spheresParent.transform;

            lineRenderers[i] = lnObject.AddComponent<LineRenderer>();
            lineRenderers[i].startWidth = lineWidth / 100;
            lineRenderers[i].endWidth = lineWidth / 100;

            lineRenderers[i].positionCount = 2;
            lineRenderers[i].material = new Material(Shader.Find("Sprites/Default"));
            lineRenderers[i].startColor = Color.green;
            lineRenderers[i].endColor = Color.green;
        }

        for (int i = 0; i < JointsNum; i++)
        {
            spheres[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spheres[i].name = "Joint_" + i;
            spheres[i].transform.parent = spheresParent.transform;
            spheres[i].transform.localScale = new Vector3(sphereScale / 100, sphereScale / 100, sphereScale / 100);

            Material material = new Material(Shader.Find("Sprites/Default"));
            material.color = Color.blue;
            spheres[i].GetComponent<Renderer>().material = material;
        }

    }

    private List<Vector3> GetJoints(JointData jointData) {
        List<Vector3> jointList = new List<Vector3>();
        
        var length = jointData.j3D.Length;
        if (jointData.j3D == null || length == 0) {
            return jointList;
        }

        for (int i = 0; i < length; i += 3) {
            jointList.Add(new Vector3(jointData.j3D[i], jointData.j3D[i + 1], -jointData.j3D[i + 2]));
        }

        return jointList;
    }

    public void UpdateJoints(JointData jointData)
    {
        List<Vector3> joints = GetJoints(jointData);
        
        if (joints.Count != JointsNum)
        {
            Debug.LogWarning("Joints count does not match the expected number of joints.");
            return;
        }

        for (int i = 0; i < joints.Count; i++)
        {
            spheres[i].transform.position = joints[i];
        }

        
        for (int i = 0, j = 0; i < connectorsIndices.Length; i += 2, j++)
        {
            int index1 = connectorsIndices[i];
            int index2 = connectorsIndices[i + 1];

            lineRenderers[j].SetPosition(0, spheres[index1].transform.position);
            lineRenderers[j].SetPosition(1, spheres[index2].transform.position);
        }
    }
}
