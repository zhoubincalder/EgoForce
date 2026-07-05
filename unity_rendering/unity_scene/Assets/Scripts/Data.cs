using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SMPLData { 
    public int frame_index;       
    public List<float> global_orient;
    public List<float> body_pose;
    public List<float> betas;
    public List<float> transl;
    public List<float> verts;

    public Quaternion GetGlobalOrientation() {
        return new Quaternion(global_orient[0], global_orient[1], global_orient[2], global_orient[3]);
    }
    public List<Quaternion> GetBodyPose() {
        List<Quaternion> body_pose_list = new List<Quaternion>();
        
        for (int i = 0; i < body_pose.Count; i += 4) {
            body_pose_list.Add(new Quaternion(body_pose[i], body_pose[i + 1], body_pose[i + 2], body_pose[i + 3]));
        }
        
        return body_pose_list;
    }
    public List<float> GetBetas() {
        return betas;
    }
    public Vector3 GetTransl() {
        return new Vector3(transl[0], transl[1], transl[2]);
    }
    public List<Vector3> GetVerts() {
        List<Vector3> verts_list = new List<Vector3>();
        
        for (int i = 0; i < verts.Count; i += 3) {
            verts_list.Add(new Vector3(verts[i], verts[i + 1], verts[i + 2]));
        }
        
        return verts_list;
    }
}   


[System.Serializable]
public class FrameData {
    public byte[] frame0;
    public byte[] frame1;
    public int frameWidth;
    public int frameHeight;

}

[System.Serializable]
public class JointData {
    public float[] j3D;
}


[System.Serializable]
public class JointMeshData {
    public int nKeypoints;
    public string[] JointNames;
    public int[] kinematicTree;
}


[System.Serializable]
public class MeshData
{
    public float[] vertices;
    public int[] triangles;
    public float[] normals;
    public float[] uv;
}

[System.Serializable]
public class Data
{
    public Texture2D LatestFrameTex;
    public MeshData  LeftHand;
    public MeshData  RightHand;
    public bool      LeftHandVisible;
    public bool      RightHandVisible;
    public int       frameIndex;
}


