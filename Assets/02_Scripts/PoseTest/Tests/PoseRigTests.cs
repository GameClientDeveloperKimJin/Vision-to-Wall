using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VisionToWall.Core;

namespace VisionToWall.PoseTest.Tests
{
    /// <summary>Code poses on the real PlayerAvatar_Rig: the truth angles are the angles the clip asked for.</summary>
    public sealed class PoseRigTests
    {
        private const string ModelPath = "Assets/07_Models/Avatar/PlayerAvatar_Rig.fbx";
        private Scene _scene;
        private Camera _cam;
        private PoseRig _rig;
        private readonly Vector3 _base = new Vector3(0f, 0f, 3f);
        private readonly float[] _joints = new float[PoseProtocol.JointCount * PoseProtocol.FloatsPerJoint];

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.IsNotNull(model, ModelPath);
            GameObject avatar = Object.Instantiate(model);
            SceneManager.MoveGameObjectToScene(avatar, _scene);
            avatar.transform.SetPositionAndRotation(_base, Quaternion.Euler(0f, 180f, 0f));
            avatar.transform.localScale = Vector3.one * 1.6f;
            var animator = avatar.GetComponent<Animator>();
            Assert.IsTrue(animator != null && animator.avatar != null && animator.avatar.isHuman, "Humanoid import");

            var camGo = new GameObject("cam");
            SceneManager.MoveGameObjectToScene(camGo, _scene);
            camGo.transform.position = new Vector3(0f, 1.05f, 0f);
            _cam = camGo.AddComponent<Camera>();
            _cam.scene = _scene;
            _cam.fieldOfView = 43f;
            _cam.aspect = 4f / 3f;
            _cam.enabled = false;

            _rig = new PoseRig(animator);
            _rig.CalibrateFloor(_base);
        }

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(_scene);

        private static float Angle(Vector3 a, Vector3 b, Vector3 c) => Vector3.Angle(a - b, c - b);

        private float JointAngle(PoseJoint a, PoseJoint b, PoseJoint c) =>
            Angle(_rig.JointPosition(a), _rig.JointPosition(b), _rig.JointPosition(c));

        [Test]
        public void ImportedAsHumanoidWithEveryTruthBone()
        {
            foreach (HumanBodyBones b in PoseRig.TruthBones) Assert.IsNotNull(_rig.Bone(b), b.ToString());
            foreach (HumanBodyBones b in PoseRig.JointBones) Assert.IsNotNull(_rig.Bone(b), b.ToString());
        }

        [Test]
        public void ElbowAndKneeAnglesMatchSpec()
        {
            PoseSpec p = PoseSpec.Rest;
            p.LeftShoulderAbd = 90f;
            p.LeftElbowFlex = 40f;
            p.RightShoulderAbd = 120f;
            p.RightElbowFlex = 75f;
            p.LeftHipFlex = 30f;
            p.LeftKneeFlex = 60f;
            p.RightHipFlex = 70f;
            p.RightKneeFlex = 90f;
            _rig.Apply(p, _base);

            Assert.AreEqual(140f, JointAngle(PoseJoint.LeftShoulder, PoseJoint.LeftElbow, PoseJoint.LeftWrist), 1f);
            Assert.AreEqual(105f, JointAngle(PoseJoint.RightShoulder, PoseJoint.RightElbow, PoseJoint.RightWrist), 1f);
            Assert.AreEqual(120f, JointAngle(PoseJoint.LeftHip, PoseJoint.LeftKnee, PoseJoint.LeftAnkle), 1f);
            Assert.AreEqual(90f, JointAngle(PoseJoint.RightHip, PoseJoint.RightKnee, PoseJoint.RightAnkle), 1f);
        }

        [Test]
        public void UpperArmDirectionMatchesAbduction()
        {
            Transform root = _rig.Root;
            foreach (float abd in new[] { 10f, 90f, 180f })
            {
                PoseSpec p = PoseSpec.Rest;
                p.LeftShoulderAbd = abd;
                _rig.Apply(p, _base);
                Vector3 dir = root.InverseTransformDirection(_rig.JointPosition(PoseJoint.LeftElbow) - _rig.JointPosition(PoseJoint.LeftShoulder));
                Assert.Less(Vector3.Angle(dir, PoseRig.Limb(-abd, 0f)), 1f, "abd " + abd);
            }
        }

        [Test]
        public void LeftArmRaise_IsLeftLabelOnScreenLeft_WithMirror()
        {
            PoseSpec p = PoseSpec.Rest;
            p.LeftShoulderAbd = 180f;
            _rig.Apply(p, _base);
            Assert.AreEqual(PoseProtocol.JointCount, _rig.SampleJoints(_cam, _joints));

            int lw = (int)PoseJoint.LeftWrist * PoseProtocol.FloatsPerJoint;
            int rw = (int)PoseJoint.RightWrist * PoseProtocol.FloatsPerJoint;
            int nose = (int)PoseJoint.Nose * PoseProtocol.FloatsPerJoint;
            Assert.Less(_joints[lw], 0.5f, "player's left = mirrored screen left");
            Assert.Greater(_joints[lw + 1], _joints[nose + 1], "raised wrist above the nose");
            Assert.Less(_joints[lw + 2], 0f, "mirrored world x of the left side is negative");
            Assert.Greater(_joints[lw + 3], _joints[rw + 3] + 0.8f);
            Assert.AreEqual(1f, _joints[lw + 5]);
        }

        [Test]
        public void Squat_KeepsFeetOnTheFloor_AndLowersHips()
        {
            _rig.Apply(PoseSpec.Rest, _base);
            float footY = _rig.JointPosition(PoseJoint.LeftAnkle).y;
            float hipY = _rig.JointPosition(PoseJoint.LeftHip).y;

            PoseSpec p = PoseTestClips.Find("squat").Evaluate(2.5f);
            Assert.AreEqual(85f, p.LeftHipFlex, 1e-3f);
            _rig.Apply(p, _base);
            Assert.AreEqual(footY, Mathf.Min(_rig.JointPosition(PoseJoint.LeftAnkle).y, _rig.JointPosition(PoseJoint.RightAnkle).y), 1e-3f);
            Assert.Less(_rig.JointPosition(PoseJoint.LeftHip).y, hipY - 0.2f);
        }

        [Test]
        public void BoneDeltas_ReproduceThePose()
        {
            // R4 contract: bone.rotation = root.rotation * delta * BindRootRotation(bone).
            PoseSpec p = PoseTestClips.Find("wave_right").Evaluate(2f);
            p.LeftHipFlex = 40f;
            p.LeftKneeFlex = 50f;
            p.TorsoLean = 15f;
            _rig.Apply(p, _base);
            var deltas = new Quaternion[PoseRig.TruthBones.Length];
            _rig.SampleBoneDeltas(deltas);
            Vector3 wrist = _rig.JointPosition(PoseJoint.RightWrist) - _rig.Bone(HumanBodyBones.Hips).position;
            Vector3 ankle = _rig.JointPosition(PoseJoint.LeftAnkle) - _rig.Bone(HumanBodyBones.Hips).position;

            _rig.ResetToBind(_base);
            for (int i = 0; i < PoseRig.TruthBones.Length; i++)
            {
                HumanBodyBones b = PoseRig.TruthBones[i];
                _rig.Bone(b).rotation = _rig.Root.rotation * deltas[i] * _rig.BindRootRotation(b);
            }

            Vector3 wrist2 = _rig.JointPosition(PoseJoint.RightWrist) - _rig.Bone(HumanBodyBones.Hips).position;
            Vector3 ankle2 = _rig.JointPosition(PoseJoint.LeftAnkle) - _rig.Bone(HumanBodyBones.Hips).position;
            Assert.Less(Vector3.Distance(wrist, wrist2), 0.005f);
            Assert.Less(Vector3.Distance(ankle, ankle2), 0.005f);
        }

        [Test]
        public void OutOfView_GivesCountZero()
        {
            PoseSpec p = PoseSpec.Rest;
            p.RootOffset = new Vector3(5f, 0f, 0f);
            _rig.Apply(p, _base);
            Assert.AreEqual(0, _rig.SampleJoints(_cam, _joints));
        }

        [Test]
        public void NoseIsInFrontOfTheHead()
        {
            _rig.Apply(PoseSpec.Rest, _base);
            Transform head = _rig.Bone(HumanBodyBones.Head);
            Vector3 nose = _rig.JointPosition(PoseJoint.Nose);
            float forward = Vector3.Dot(nose - head.position, _rig.Root.forward);
            Assert.That(forward, Is.InRange(0.05f, 0.2f));
            Assert.Less(Mathf.Abs(Vector3.Dot(nose - head.position, _rig.Root.right)), 0.03f);
        }
    }
}
