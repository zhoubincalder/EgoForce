using System.IO;
using UnityEngine;
using System.Collections;

// https://github.com/JustDoIt9/UnityProjectionMapping/blob/master/Assets/Calibration.cs#L38C54-L38C55

class SetCamera : MonoBehaviour {
    Camera cam;

    void Start() {
        cam = GetComponent<Camera>();
        
        // Matrix4x4 projectionMatrix = new Matrix4x4();
        // projectionMatrix.SetRow(0, new Vector4(1.1572303780000002f, 0.0f, 0.008640646000000002f, 0.0f));
        // projectionMatrix.SetRow(1, new Vector4(0.0f, 1.5838344121484471f, -1.4525595744568332e-05f, 0.0f));
        // projectionMatrix.SetRow(2, new Vector4(0.0f, 0.0f, -1.001000500250125f, -0.10005002501250625f));
        // projectionMatrix.SetRow(3, new Vector4(0.0f, 0.0f, -1.0f, 0.0f));
        // camera.projectionMatrix = projectionMatrix;

        // Matrix4x4 worldToCam = new Matrix4x4();
        // worldToCam.SetRow(0, new Vector4(-0.8524553358593767f, -0.05568925473825542f, -0.5198255546542274f, 2.722772460938f));       
        // worldToCam.SetRow(1, new Vector4(-0.027493027796016586f, 0.9977098338427277f, -0.06179984527589137f, -0.21575099182100002f));
        // worldToCam.SetRow(2, new Vector4(-0.5220766550876268f, 0.03839002943752909f, 0.8520341377270639f, -1.954629150391f));        
        // worldToCam.SetRow(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
        // camera.worldToCameraMatrix = worldToCam;

    }

}