using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.PoseTest
{
    /// <summary>
    /// Poses a Humanoid avatar (<c>PlayerAvatar_Rig.fbx</c>) from a <see cref="PoseSpec"/> by setting bone world
    /// rotations directly (no animation clip), and reads the ground truth back from
    /// <see cref="Animator.GetBoneTransform"/>.
    /// <para>
    /// Every <see cref="Apply"/> starts from the T-pose bind, then turns each limb bone with the smallest rotation
    /// that points it at the target direction. Feet keep their bind orientation and stay on the floor
    /// (the pelvis drops in a squat).
    /// </para>
    /// Truth joints use the same layout as the pose stream (Core <see cref="PoseProtocol"/>): screen x, y (mirror
    /// applied, origin bottom-left, clamped), world x, y, z (meters, hip centered, Unity axes, mirror applied:
    /// +x = camera image left), visibility (1 inside the camera view, 0 outside). The camera must be level
    /// (no pitch / roll) so camera axes equal world axes, as MediaPipe world landmarks are camera aligned.
    /// </summary>
    public sealed class PoseRig
    {
        /// <summary>Bones whose rotation is written to the truth CSV (R4 reference), in column order.</summary>
        public static readonly HumanBodyBones[] TruthBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
        };

        /// <summary>Bone of each <see cref="PoseJoint"/> (Nose uses the head with <see cref="NoseOffset"/>).</summary>
        public static readonly HumanBodyBones[] JointBones =
        {
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };

        private static readonly HumanBodyBones[] AllPosed =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        };

        private readonly Animator _animator;
        private readonly Transform _root;
        private readonly Transform[] _bones = new Transform[(int)HumanBodyBones.LastBone];
        private readonly Quaternion[] _bindLocalRot = new Quaternion[(int)HumanBodyBones.LastBone];
        private readonly Quaternion[] _bindRootRot = new Quaternion[(int)HumanBodyBones.LastBone];
        private Vector3 _bindHipsLocalPos;
        private float _standFootY;
        private float _standFootZ;

        /// <summary>Nose tip in the head bone's local space (front-most face vertex at bind).</summary>
        public Vector3 NoseOffset { get; private set; }

        public Transform Root => _root;
        public Animator Animator => _animator;

        /// <summary>The avatar must be in its bind (T) pose at its final scale.</summary>
        public PoseRig(Animator animator)
        {
            _animator = animator;
            _root = animator.transform;
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform t = animator.GetBoneTransform((HumanBodyBones)i);
                _bones[i] = t;
                if (t == null) continue;
                _bindLocalRot[i] = t.localRotation;
                _bindRootRot[i] = Quaternion.Inverse(_root.rotation) * t.rotation;
            }

            _bindHipsLocalPos = Bone(HumanBodyBones.Hips).localPosition;
            NoseOffset = FindNoseOffset();
        }

        public Transform Bone(HumanBodyBones b) => _bones[(int)b];

        /// <summary>Bind rotation of <paramref name="b"/> relative to the avatar root (T-pose).</summary>
        public Quaternion BindRootRotation(HumanBodyBones b) => _bindRootRot[(int)b];

        /// <summary>Back to the T-pose bind at <paramref name="basePosition"/>.</summary>
        public void ResetToBind(Vector3 basePosition)
        {
            _root.position = basePosition;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _bones[i].localRotation = _bindLocalRot[i];
            Bone(HumanBodyBones.Hips).localPosition = _bindHipsLocalPos;
        }

        /// <summary>
        /// Records the standing foot height and depth. Call once after constructing, with the avatar at its base
        /// position (any pose; only the feet matter, which <see cref="Apply"/> keeps on the floor).
        /// </summary>
        public void CalibrateFloor(Vector3 basePosition)
        {
            ApplyRotations(PoseSpec.Rest, basePosition);
            Vector3 l = Bone(HumanBodyBones.LeftFoot).position;
            Vector3 r = Bone(HumanBodyBones.RightFoot).position;
            _standFootY = Mathf.Min(l.y, r.y);
            _standFootZ = (l.z + r.z) * 0.5f;
        }

        public void Apply(PoseSpec p, Vector3 basePosition)
        {
            ApplyRotations(p, basePosition);

            // Feet on the floor: lowest ankle back to the standing height, feet centered under the body in depth.
            Vector3 l = Bone(HumanBodyBones.LeftFoot).position;
            Vector3 r = Bone(HumanBodyBones.RightFoot).position;
            float dy = _standFootY - Mathf.Min(l.y, r.y);
            float dz = _standFootZ - (l.z + r.z) * 0.5f;
            Bone(HumanBodyBones.Hips).position += new Vector3(0f, dy, dz);
        }

        private void ApplyRotations(PoseSpec p, Vector3 basePosition)
        {
            ResetToBind(basePosition + p.RootOffset);

            Quaternion lean = Quaternion.AngleAxis(p.TorsoLean, Vector3.right);
            SetRootRotation(HumanBodyBones.Hips, lean * _bindRootRot[(int)HumanBodyBones.Hips]);

            // Arms are relative to the (leaning) torso. Avatar frame: its left = -x.
            Vector3 lUpper = lean * Limb(-p.LeftShoulderAbd, p.LeftShoulderFlex);
            Vector3 lFore = lean * Limb(-(p.LeftShoulderAbd + p.LeftElbowFlex), p.LeftShoulderFlex);
            Vector3 rUpper = lean * Limb(p.RightShoulderAbd, p.RightShoulderFlex);
            Vector3 rFore = lean * Limb(p.RightShoulderAbd + p.RightElbowFlex, p.RightShoulderFlex);
            Aim(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, lUpper);
            Aim(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, lFore);
            Aim(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, rUpper);
            Aim(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, rFore);

            Aim(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, Limb(-p.LeftHipAbd, p.LeftHipFlex));
            Aim(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, Limb(-p.LeftHipAbd, p.LeftHipFlex - p.LeftKneeFlex));
            Aim(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, Limb(p.RightHipAbd, p.RightHipFlex));
            Aim(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, Limb(p.RightHipAbd, p.RightHipFlex - p.RightKneeFlex));

            // Feet flat (bind orientation), hands follow the forearms.
            SetRootRotation(HumanBodyBones.LeftFoot, _bindRootRot[(int)HumanBodyBones.LeftFoot]);
            SetRootRotation(HumanBodyBones.RightFoot, _bindRootRot[(int)HumanBodyBones.RightFoot]);
        }

        /// <summary>
        /// Limb direction in the avatar frame: straight down, swung forward by <paramref name="forwardDeg"/>
        /// (sagittal), then turned by <paramref name="sideDeg"/> in the frontal plane (+ = toward +x).
        /// </summary>
        public static Vector3 Limb(float sideDeg, float forwardDeg)
        {
            Vector3 d = Quaternion.AngleAxis(-forwardDeg, Vector3.right) * Vector3.down;
            return Quaternion.AngleAxis(sideDeg, Vector3.forward) * d;
        }

        private void SetRootRotation(HumanBodyBones b, Quaternion rootLocal) =>
            Bone(b).rotation = _root.rotation * rootLocal;

        private void Aim(HumanBodyBones bone, HumanBodyBones child, Vector3 avatarDir)
        {
            Transform t = Bone(bone);
            Vector3 current = Bone(child).position - t.position;
            Vector3 target = _root.rotation * avatarDir;
            t.rotation = Quaternion.FromToRotation(current, target) * t.rotation;
        }

        /// <summary>World position of a joint (Nose = head bone + <see cref="NoseOffset"/>).</summary>
        public Vector3 JointPosition(PoseJoint j)
        {
            Transform t = Bone(JointBones[(int)j]);
            return j == PoseJoint.Nose ? t.TransformPoint(NoseOffset) : t.position;
        }

        /// <summary>
        /// Truth joints in the pose stream layout. <paramref name="joints"/>: 13 x 6 floats.
        /// Returns the count: 0 when no joint is inside the camera view (as the sensor sends for nobody), else 13.
        /// </summary>
        public int SampleJoints(Camera cam, float[] joints, bool mirror = true)
        {
            Vector3 hip = (JointPosition(PoseJoint.LeftHip) + JointPosition(PoseJoint.RightHip)) * 0.5f;
            int inView = 0;
            for (int j = 0; j < PoseProtocol.JointCount; j++)
            {
                Vector3 p = JointPosition((PoseJoint)j);
                Vector3 vp = cam.WorldToViewportPoint(p);
                bool visible = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
                if (visible) inView++;
                Vector3 w = p - hip;
                int o = j * PoseProtocol.FloatsPerJoint;
                joints[o] = Mathf.Clamp01(mirror ? 1f - vp.x : vp.x);
                joints[o + 1] = Mathf.Clamp01(vp.y);
                joints[o + 2] = mirror ? -w.x : w.x;
                joints[o + 3] = w.y;
                joints[o + 4] = w.z;
                joints[o + 5] = visible ? 1f : 0f;
            }

            return inView == 0 ? 0 : PoseProtocol.JointCount;
        }

        /// <summary>
        /// Rotation of each <see cref="TruthBones"/> bone relative to its T-pose, in the avatar root frame:
        /// <c>bone.rotation = root.rotation * delta * BindRootRotation(bone)</c>.
        /// </summary>
        public void SampleBoneDeltas(Quaternion[] deltas)
        {
            Quaternion invRoot = Quaternion.Inverse(_root.rotation);
            for (int i = 0; i < TruthBones.Length; i++)
            {
                HumanBodyBones b = TruthBones[i];
                deltas[i] = invRoot * Bone(b).rotation * Quaternion.Inverse(_bindRootRot[(int)b]);
            }
        }

        private Vector3 FindNoseOffset()
        {
            Transform head = Bone(HumanBodyBones.Head);
            SkinnedMeshRenderer smr = _root.GetComponentInChildren<SkinnedMeshRenderer>();
            if (head == null || smr == null) return new Vector3(0f, 0.04f, 0.09f);

            Transform top = head.childCount > 0 ? head.GetChild(0) : null;
            float headTop = top != null ? top.position.y : head.position.y + 0.15f;
            float headSize = headTop - head.position.y;
            float yMin = head.position.y + headSize * 0.1f;
            float yMax = head.position.y + headSize * 0.6f;
            float halfWidth = headSize * 0.12f;

            var mesh = new Mesh();
            smr.BakeMesh(mesh, true); // measured in 6000.3: true = vertices without the transform scale, so TransformPoint is right
            Vector3[] v = mesh.vertices;
            Vector3 forward = _root.forward;
            Vector3 right = _root.right;
            float best = float.NegativeInfinity;
            Vector3 nose = head.position + forward * 0.1f;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = smr.transform.TransformPoint(v[i]);
                if (w.y < yMin || w.y > yMax) continue;
                if (Mathf.Abs(Vector3.Dot(w - head.position, right)) > halfWidth) continue;
                float f = Vector3.Dot(w - head.position, forward);
                if (f > best)
                {
                    best = f;
                    nose = w;
                }
            }

            Object.DestroyImmediate(mesh);
            return head.InverseTransformPoint(nose);
        }
    }
}
