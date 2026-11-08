using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisionToWall.Game.Tests
{
    /// <summary>The real <c>PlayerAvatar_Rig.fbx</c> in a preview scene, root at the origin facing +z.</summary>
    internal sealed class AvatarRigFixture
    {
        public const string ModelPath = "Assets/07_Models/Avatar/PlayerAvatar_Rig.fbx";
        public static readonly Quaternion MeshToRoot = Quaternion.Euler(270f, 0f, 0f); // prefab meshEuler

        private static readonly HumanBodyBones[] LimbBones =
        {
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
        };
        private static readonly HumanBodyBones[] LimbChildren =
        {
            HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        };

        public readonly Scene Scene;
        public readonly GameObject Root;
        public readonly Animator Animator;
        public readonly SkinnedMeshRenderer Skin;
        public readonly AvatarSkeleton Skeleton;

        /// <summary>Limb directions (bone to child) of the FBX default T-pose, root frame: the RT truth bind.</summary>
        public readonly Vector3[] TPoseDirection = new Vector3[AvatarPoseSolver.LimbCount];

        public AvatarRigFixture()
        {
            Scene = EditorSceneManager.NewPreviewScene();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.IsNotNull(model, ModelPath);
            Root = Object.Instantiate(model);
            SceneManager.MoveGameObjectToScene(Root, Scene);
            Animator = Root.GetComponent<Animator>();
            Skin = Root.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsTrue(Animator != null && Animator.isHuman, "Humanoid import");

            for (int i = 0; i < LimbBones.Length; i++)
            {
                Vector3 a = Animator.GetBoneTransform(LimbBones[i]).position;
                Vector3 b = Animator.GetBoneTransform(LimbChildren[i]).position;
                TPoseDirection[i] = (b - a).normalized; // root is identity
            }

            Skeleton = new AvatarSkeleton(Root.transform, Animator, Skin, MeshToRoot);
        }

        public static HumanBodyBones Bone(AvatarLimb limb) => LimbBones[(int)limb];

        public void Close() => EditorSceneManager.ClosePreviewScene(Scene);
    }
}
