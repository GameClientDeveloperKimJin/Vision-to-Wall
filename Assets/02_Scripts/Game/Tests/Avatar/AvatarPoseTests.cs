using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game.Tests
{
    /// <summary>R4 pose avatar: weights, joint -> bone directions, left/right, return to rest, skinned rest look.</summary>
    public sealed class AvatarPoseTests
    {
        private const float Dt = 1f / 60f;

        // ---------- bone weights ----------

        [Test]
        public void Blend_MergesBones_KeepsTopFour_SumsToOne()
        {
            var a = new BoneWeight { boneIndex0 = 1, weight0 = 0.6f, boneIndex1 = 2, weight1 = 0.4f };
            var b = new BoneWeight { boneIndex0 = 2, weight0 = 0.5f, boneIndex1 = 3, weight1 = 0.3f, boneIndex2 = 4, weight2 = 0.2f };
            var c = new BoneWeight { boneIndex0 = 5, weight0 = 0.7f, boneIndex1 = 6, weight1 = 0.2f, boneIndex2 = 1, weight2 = 0.1f };

            BoneWeight w = AvatarPointMesh.Blend(a, b, c, 0.5f, 0.3f, 0.2f);

            Assert.That(w.weight0 + w.weight1 + w.weight2 + w.weight3, Is.EqualTo(1f).Within(1e-5f));
            // bone 1: 0.30 + 0.02 = 0.32, bone 2: 0.20 + 0.15 = 0.35, bone 5: 0.14, bone 3: 0.09, 4: 0.06, 6: 0.04
            Assert.That(new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 }, Is.EqualTo(new[] { 2, 1, 5, 3 }));
            Assert.That(w.weight0, Is.GreaterThanOrEqualTo(w.weight1));
            Assert.That(w.weight0, Is.EqualTo(0.35f / 0.9f).Within(1e-4f));
        }

        [Test]
        public void Blend_AtACorner_IsThatCornersWeights()
        {
            var a = new BoneWeight { boneIndex0 = 7, weight0 = 0.8f, boneIndex1 = 9, weight1 = 0.2f };
            var b = new BoneWeight { boneIndex0 = 1, weight0 = 1f };
            BoneWeight w = AvatarPointMesh.Blend(a, b, b, 1f, 0f, 0f);
            Assert.That(w.boneIndex0, Is.EqualTo(7));
            Assert.That(w.weight0, Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(w.boneIndex1, Is.EqualTo(9));
        }

        [Test]
        public void SkinnedPointMesh_FromRig_WeightsSumToOne_AndRestMatchesStaticFigure()
        {
            var rig = new AvatarRigFixture();
            Mesh skinned = null, flat = null, baked = null;
            try
            {
                Mesh source = rig.Skin.sharedMesh;
                skinned = AvatarPointMesh.Build(source, 2000, AvatarRigFixture.MeshToRoot, skinned: true);
                flat = PlayerAvatarView.BuildPointMesh(source, 2000, AvatarRigFixture.MeshToRoot);

                BoneWeight[] weights = skinned.boneWeights;
                Assert.That(weights.Length, Is.EqualTo(skinned.vertexCount));
                Assert.That(skinned.bindposes.Length, Is.EqualTo(source.bindposes.Length));
                foreach (BoneWeight w in weights)
                    Assert.That(w.weight0 + w.weight1 + w.weight2 + w.weight3, Is.EqualTo(1f).Within(1e-4f));

                // Rest pose skinned = the static figure, point for point (so back creases and lighting are unchanged).
                rig.Skin.sharedMesh = skinned;
                rig.Skeleton.ApplyRest();
                baked = new Mesh();
                rig.Skin.BakeMesh(baked, true);
                Vector3[] posed = baked.vertices;
                Vector3[] expected = flat.vertices;
                float worst = 0f;
                for (int i = 0; i < posed.Length; i++)
                {
                    Vector3 root = rig.Root.transform.InverseTransformPoint(rig.Skin.transform.TransformPoint(posed[i]));
                    worst = Mathf.Max(worst, (root - expected[i]).magnitude);
                }
                Assert.That(worst, Is.LessThan(1e-4f), "rest skinned vs static point");

                var restA = new List<Vector3>();
                var restB = new List<Vector3>();
                skinned.GetUVs(1, restA);
                flat.GetUVs(1, restB);
                Assert.That(restA, Is.EqualTo(restB), "uv1 crease coordinates identical");
            }
            finally
            {
                if (skinned != null) Object.DestroyImmediate(skinned);
                if (flat != null) Object.DestroyImmediate(flat);
                if (baked != null) Object.DestroyImmediate(baked);
                rig.Close();
            }
        }

        // ---------- solver ----------

        private static PoseFrame Standing()
        {
            var f = new PoseFrame { HasPerson = true };
            void Set(PoseJoint j, float x, float y, float z = 0f)
            {
                f.World[(int)j] = new Vector3(x, y, z);
                f.Visibility[(int)j] = 1f;
            }

            Set(PoseJoint.Nose, 0f, 0.65f, -0.1f);
            Set(PoseJoint.LeftShoulder, -0.18f, 0.5f);
            Set(PoseJoint.RightShoulder, 0.18f, 0.5f);
            Set(PoseJoint.LeftElbow, -0.2f, 0.22f);
            Set(PoseJoint.RightElbow, 0.2f, 0.22f);
            Set(PoseJoint.LeftWrist, -0.21f, -0.03f);
            Set(PoseJoint.RightWrist, 0.21f, -0.03f);
            Set(PoseJoint.LeftHip, -0.1f, 0f);
            Set(PoseJoint.RightHip, 0.1f, 0f);
            Set(PoseJoint.LeftKnee, -0.1f, -0.42f);
            Set(PoseJoint.RightKnee, 0.1f, -0.42f);
            Set(PoseJoint.LeftAnkle, -0.1f, -0.84f);
            Set(PoseJoint.RightAnkle, 0.1f, -0.84f);
            return f;
        }

        private static void RaiseRightArmSideways(PoseFrame f)
        {
            // Player's right = world +x (mirror applied) = screen right.
            f.World[(int)PoseJoint.RightElbow] = new Vector3(0.46f, 0.5f, 0f);
            f.World[(int)PoseJoint.RightWrist] = new Vector3(0.71f, 0.5f, 0f);
        }

        private static void Run(AvatarPoseSolver s, PoseFrame f, ref float now, float sec, float age = 0f)
        {
            for (float t = 0f; t < sec; t += Dt)
            {
                now += Dt;
                s.Update(f, age, now, Dt);
            }
        }

        [Test]
        public void ToAvatar_KeepsSides_AndFlipsDepth()
        {
            // Player faces the camera, the avatar faces away: toward the camera (-z world) is the avatar's front (+z).
            Assert.That(AvatarPoseSolver.ToAvatar(new Vector3(0.3f, 0.2f, -0.5f)), Is.EqualTo(new Vector3(0.3f, 0.2f, 0.5f)));
        }

        [Test]
        public void RightArmRaised_DrivesAvatarRightArm_OnScreenRight()
        {
            var s = new AvatarPoseSolver();
            PoseFrame f = Standing();
            RaiseRightArmSideways(f);
            float now = 0f;
            Run(s, f, ref now, 1f);

            Vector3 right = s.Direction(AvatarLimb.RightUpperArm);
            Vector3 left = s.Direction(AvatarLimb.LeftUpperArm);
            Assert.That(right.x, Is.GreaterThan(0.95f), "right upper arm points to the avatar's right (+x)");
            Assert.That(left.y, Is.LessThan(-0.9f), "left arm still down");
            Assert.That(s.Weight(AvatarLimb.RightUpperArm), Is.EqualTo(1f));

            // On the real rig, seen from behind (camera at -z looking +z): the right hand ends on screen right.
            var rig = new AvatarRigFixture();
            try
            {
                rig.Skeleton.Apply(s);
                Vector3 hand = rig.Animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                Vector3 shoulder = rig.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
                Assert.That(hand.x - shoulder.x, Is.GreaterThan(0.2f), "screen right from behind = +x");
                Assert.That(Mathf.Abs(hand.y - shoulder.y), Is.LessThan(0.05f), "arm horizontal");
                Vector3 leftHand = rig.Animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
                Assert.That(leftHand.y, Is.LessThan(shoulder.y - 0.2f), "left hand stays down");
            }
            finally
            {
                rig.Close();
            }
        }

        [Test]
        public void Skeleton_PointsEveryLimbAtItsDirection()
        {
            var s = new AvatarPoseSolver { LimbDepthGain = 1f };
            PoseFrame f = Standing();
            RaiseRightArmSideways(f);
            f.World[(int)PoseJoint.LeftElbow] = new Vector3(-0.3f, 0.35f, -0.15f); // reaching toward the camera
            f.World[(int)PoseJoint.LeftWrist] = new Vector3(-0.35f, 0.45f, -0.35f);
            f.World[(int)PoseJoint.RightKnee] = new Vector3(0.18f, -0.4f, 0f);
            float now = 0f;
            Run(s, f, ref now, 1f);

            var rig = new AvatarRigFixture();
            try
            {
                rig.Skeleton.Apply(s);
                for (int i = 0; i < AvatarPoseSolver.LimbCount; i++)
                {
                    var limb = (AvatarLimb)i;
                    float error = Vector3.Angle(rig.Skeleton.LimbDirection(limb), s.Direction(limb));
                    Assert.That(error, Is.LessThan(1f), limb.ToString());
                }
                Assert.That(s.Direction(AvatarLimb.LeftLowerArm).z, Is.GreaterThan(0.3f), "toward the camera = avatar forward");
            }
            finally
            {
                rig.Close();
            }
        }

        [Test]
        public void NoPackets_ReturnsToRestWithinReturnTime()
        {
            var s = new AvatarPoseSolver();
            PoseFrame f = Standing();
            RaiseRightArmSideways(f);
            float now = 0f;
            Run(s, f, ref now, 1f);
            Assert.That(s.Weight(AvatarLimb.RightUpperArm), Is.EqualTo(1f));

            // Packets stop: still tracked until the timeout, then back to rest in ReturnSec.
            Run(s, f, ref now, AvatarPoseSolver.LostTimeoutSec - 0.05f, age: 0.4f);
            Assert.That(s.Weight(AvatarLimb.RightUpperArm), Is.EqualTo(1f));
            Run(s, f, ref now, AvatarPoseSolver.ReturnSec * 0.5f, age: 0.6f);
            Assert.That(s.Weight(AvatarLimb.RightUpperArm), Is.InRange(0.3f, 0.7f), "halfway");
            Run(s, f, ref now, AvatarPoseSolver.ReturnSec * 0.5f + 0.02f, age: 0.9f);
            Assert.That(s.Weight(AvatarLimb.RightUpperArm), Is.EqualTo(0f));
            Assert.That(s.TorsoWeight, Is.EqualTo(0f));
            Assert.That(s.Present, Is.False);
        }

        [Test]
        public void NobodyOrNullSource_IsRest()
        {
            var s = new AvatarPoseSolver();
            float now = 0f;
            Run(s, Standing(), ref now, 1f);
            Run(s, new PoseFrame(), ref now, 0.4f); // count 0 packet: HasPerson false
            Assert.That(s.Weight(AvatarLimb.LeftUpperLeg), Is.EqualTo(0f));

            var t = new AvatarPoseSolver();
            Run(t, null, ref now, 0.5f);
            Assert.That(t.Weight(AvatarLimb.LeftUpperArm), Is.EqualTo(0f));
            Assert.That(t.Present, Is.False);
        }

        [Test]
        public void HiddenJoint_HoldsThenReturnsThatLimbOnly()
        {
            var s = new AvatarPoseSolver();
            PoseFrame f = Standing();
            float now = 0f;
            Run(s, f, ref now, 1f);

            // Seated or framed from the waist up: knees and ankles out of view.
            foreach (PoseJoint j in new[] { PoseJoint.LeftKnee, PoseJoint.RightKnee, PoseJoint.LeftAnkle, PoseJoint.RightAnkle })
                f.Visibility[(int)j] = 0.1f;
            Run(s, f, ref now, AvatarPoseSolver.HoldSec - 0.05f);
            Assert.That(s.Weight(AvatarLimb.LeftLowerLeg), Is.EqualTo(1f), "held");
            Run(s, f, ref now, AvatarPoseSolver.ReturnSec + 0.1f);
            Assert.That(s.Weight(AvatarLimb.LeftLowerLeg), Is.EqualTo(0f));
            Assert.That(s.Weight(AvatarLimb.RightUpperLeg), Is.EqualTo(0f));
            Assert.That(s.Weight(AvatarLimb.RightUpperArm), Is.EqualTo(1f), "arms still tracked");
            Assert.That(s.TorsoWeight, Is.EqualTo(1f));
        }

        [Test]
        public void Depth_IsDamped_AndClamped()
        {
            var s = new AvatarPoseSolver();
            PoseFrame f = Standing();
            // Wrist 40 cm behind the elbow (away from the camera = behind the avatar's back).
            f.World[(int)PoseJoint.LeftWrist] = new Vector3(-0.2f, 0.22f, 0.4f);
            float now = 0f;
            Run(s, f, ref now, 1f);
            Vector3 d = s.Direction(AvatarLimb.LeftLowerArm);
            float back = -Mathf.Asin(d.z) * Mathf.Rad2Deg;
            Assert.That(back, Is.LessThanOrEqualTo(s.MaxBackwardArmDeg + 0.01f), "no forearm through the back");

            Vector3 c = AvatarPoseSolver.ClampElevation(new Vector3(0f, -0.6f, 0.8f), 30f, 30f);
            Assert.That(Mathf.Asin(c.z) * Mathf.Rad2Deg, Is.EqualTo(30f).Within(0.01f));
            Assert.That(c.magnitude, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(c.y, Is.LessThan(0f), "keeps its frontal heading");
        }

        [Test]
        public void Squat_ThighDepthRebuiltFromForeshortening()
        {
            var s = new AvatarPoseSolver();
            PoseFrame f = Standing();
            float now = 0f;
            Run(s, f, ref now, 1f); // learns the standing thigh length (0.42 m)

            // Thigh swung 70 degrees forward: frontal length 0.42 cos 70 = 0.14, MediaPipe depth almost missing.
            f.World[(int)PoseJoint.LeftKnee] = new Vector3(-0.1f, -0.144f, -0.05f);
            Run(s, f, ref now, 1f);
            Vector3 d = s.Direction(AvatarLimb.LeftUpperLeg);
            Assert.That(Mathf.Asin(d.z) * Mathf.Rad2Deg, Is.GreaterThan(55f), "thigh points forward");
        }
    }
}
