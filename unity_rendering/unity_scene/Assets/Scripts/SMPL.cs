using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SMPL {
    public readonly GameObject smplBody;
    public readonly GameObject rootJoint;
    public readonly Dictionary<int, GameObject> joints;
    public readonly LineRenderer[] lineRenderers;
    public readonly Queue<float> waySumQueue;
    public float show = 0f;
    public float indexDist;
    
    private readonly Dictionary<int, HumanBodyBones> bodyBonesDict;
    public readonly Animator animator;

    private List<Quaternion> bodyPose;

    public SMPL()
    {
        smplBody = GameObject.Find("SMPL Mesh Male");        
        animator = smplBody.GetComponent<Animator>();

        bodyBonesDict = new Dictionary<int, HumanBodyBones>
        {
            {0, HumanBodyBones.Hips},
            {1, HumanBodyBones.RightUpperLeg},
            {2, HumanBodyBones.LeftUpperLeg},
            {3, HumanBodyBones.Spine},
            {4, HumanBodyBones.RightLowerLeg},
            {5, HumanBodyBones.LeftLowerLeg},
            {6, HumanBodyBones.Chest},
            {7, HumanBodyBones.RightFoot},
            {8, HumanBodyBones.LeftFoot},
            {9, HumanBodyBones.UpperChest},
            {10, HumanBodyBones.RightToes},
            {11, HumanBodyBones.LeftToes},
            {12, HumanBodyBones.Neck},
            {13, HumanBodyBones.RightShoulder},
            {14, HumanBodyBones.LeftShoulder},
            {15, HumanBodyBones.Head},
            {16, HumanBodyBones.RightUpperArm},
            {17, HumanBodyBones.LeftUpperArm},
            {18, HumanBodyBones.RightLowerArm},
            {19, HumanBodyBones.LeftLowerArm},
            {20, HumanBodyBones.RightHand},
            {21, HumanBodyBones.LeftHand}
        };
    
        ResetPoses();
    }

    void ResetPoses() {
        foreach (var (bdx, bodyBone) in bodyBonesDict) {
            var transform = animator.GetBoneTransform(bodyBone);
            transform.localRotation = Quaternion.identity;
        }
    }

    void UpdatePoses() {
        foreach (var (bdx, bodyBone) in bodyBonesDict) {
            var transform = animator.GetBoneTransform(bodyBone);
            transform.localRotation =   bodyPose[bdx];
        }
    }

    public void Process(SMPLData data)
    {
        smplBody.transform.localPosition = data.GetTransl();

        bodyPose = data.GetBodyPose();
        var frame_index = data.frame_index;
        
        UpdatePoses();
    }
}