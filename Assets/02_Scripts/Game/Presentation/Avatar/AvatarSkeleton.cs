using System.Collections.Generic;
using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// Bones of the rigged avatar (<c>PlayerAvatar_Rig.fbx</c>, Mixamo, Humanoid) driven by an
    /// <see cref="AvatarPoseSolver"/>. The rest pose is the mesh's own bind pose (from the bind poses, arms down),
    /// turned by <c>meshToRoot</c> so it matches the static point figure exactly; the FBX hierarchy's default
    /// T-pose is not used.
    /// <para>
    /// Order inside <see cref="Apply"/>: rest pose, then the torso (split over Spine, Chest, UpperChest; hips stay
    /// upright so the figure keeps its place), then each limb bone is turned with the smallest rotation that points
    /// it at its direction (same method as the RT truth rig, <c>PoseRig.Aim</c>). Directions are in the root frame,
    /// so the facing turn on the root (toward the boss) is applied first and the pose rides on it.
    /// </para>
    /// No allocation after construction.
    /// </summary>
    public sealed class AvatarSkeleton
    {
        public const float MaxTorsoDeg = 40f;

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
        private static readonly HumanBodyBones[] SpineBones =
        {
            HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
        };

        private readonly Transform _root;
        private readonly Transform[] _bones;
        private readonly Quaternion[] _restLocal;
        private readonly Transform _hips;
        private readonly Vector3 _hipsRestLocalPos;
        private readonly Transform[] _limb = new Transform[AvatarPoseSolver.LimbCount];
        private readonly Transform[] _limbChild = new Transform[AvatarPoseSolver.LimbCount];
        private readonly Transform[] _spine;
        private readonly Quaternion _restTorso;

        /// <param name="root">Avatar root; the solver's frame is this transform's frame.</param>
        /// <param name="meshToRoot">Rotation from mesh space to the root frame (the static figure's mesh rotation).</param>
        public AvatarSkeleton(Transform root, Animator animator, SkinnedMeshRenderer skin, Quaternion meshToRoot)
        {
            _root = root;
            _bones = skin.bones;
            Matrix4x4[] bind = skin.sharedMesh.bindposes;
            int count = _bones.Length;
            var restRoot = new Quaternion[count];
            var restPos = new Vector3[count];
            var index = new Dictionary<Transform, int>(count);
            for (int i = 0; i < count; i++)
            {
                Matrix4x4 boneToMesh = bind[i].inverse;
                restRoot[i] = meshToRoot * boneToMesh.rotation;
                restPos[i] = meshToRoot * (Vector3)boneToMesh.GetColumn(3);
                index[_bones[i]] = i;
            }

            _restLocal = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                Transform parent = _bones[i].parent;
                Quaternion parentRoot = parent != null && index.TryGetValue(parent, out int p)
                    ? restRoot[p]
                    : Quaternion.Inverse(root.rotation) * (parent != null ? parent.rotation : Quaternion.identity);
                _restLocal[i] = Quaternion.Inverse(parentRoot) * restRoot[i];
            }

            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            int hipsIndex = index[_hips];
            Transform hipsParent = _hips.parent;
            Vector3 hipsRoot = restPos[hipsIndex];
            _hipsRestLocalPos = hipsParent == null || hipsParent == root
                ? hipsRoot
                : hipsParent.InverseTransformPoint(root.TransformPoint(hipsRoot));

            for (int i = 0; i < LimbBones.Length; i++)
            {
                _limb[i] = animator.GetBoneTransform(LimbBones[i]);
                _limbChild[i] = animator.GetBoneTransform(LimbChildren[i]);
            }

            var spine = new List<Transform>(SpineBones.Length);
            foreach (HumanBodyBones b in SpineBones)
            {
                Transform t = animator.GetBoneTransform(b);
                if (t != null) spine.Add(t);
            }
            _spine = spine.ToArray();

            ApplyRest();
            _restTorso = TorsoFrame(RestTorsoUp, RestTorsoRight);
        }

        /// <summary>Rest pose (bind) torso directions in the root frame.</summary>
        public Vector3 RestTorsoUp { get; private set; }
        public Vector3 RestTorsoRight { get; private set; }

        public Transform Bone(AvatarLimb limb) => _limb[(int)limb];

        /// <summary>Unit direction of a limb bone (to its child) in the root frame, as currently posed.</summary>
        public Vector3 LimbDirection(AvatarLimb limb)
        {
            Vector3 d = _limbChild[(int)limb].position - _limb[(int)limb].position;
            return (Quaternion.Inverse(_root.rotation) * d).normalized;
        }

        public void ApplyRest()
        {
            for (int i = 0; i < _bones.Length; i++) _bones[i].localRotation = _restLocal[i];
            _hips.localPosition = _hipsRestLocalPos;

            Vector3 lShoulder = RootPoint(_limb[(int)AvatarLimb.LeftUpperArm]);
            Vector3 rShoulder = RootPoint(_limb[(int)AvatarLimb.RightUpperArm]);
            Vector3 lHip = RootPoint(_limb[(int)AvatarLimb.LeftUpperLeg]);
            Vector3 rHip = RootPoint(_limb[(int)AvatarLimb.RightUpperLeg]);
            RestTorsoUp = ((lShoulder + rShoulder) * 0.5f - (lHip + rHip) * 0.5f).normalized;
            RestTorsoRight = (rShoulder - lShoulder).normalized;
        }

        public void Apply(AvatarPoseSolver pose)
        {
            ApplyRest();

            if (pose.TorsoWeight > 0f && _spine.Length > 0)
            {
                // The solver's torso has (almost) no depth; keep the model's own rest lean on top of it.
                Vector3 up = pose.TorsoUp;
                if (Mathf.Abs(RestTorsoUp.y) > 1e-3f) up.z += RestTorsoUp.z * up.y / RestTorsoUp.y;
                Quaternion turn = TorsoFrame(up.normalized, pose.TorsoRight) * Quaternion.Inverse(_restTorso);
                turn.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                angle = Mathf.Clamp(angle, -MaxTorsoDeg, MaxTorsoDeg) * pose.TorsoWeight / _spine.Length;
                if (axis.sqrMagnitude > 1e-8f && !float.IsInfinity(axis.x))
                {
                    Quaternion step = _root.rotation * Quaternion.AngleAxis(angle, axis) * Quaternion.Inverse(_root.rotation);
                    for (int i = 0; i < _spine.Length; i++) _spine[i].rotation = step * _spine[i].rotation;
                }
            }

            for (int i = 0; i < AvatarPoseSolver.LimbCount; i++)
            {
                float w = pose.Weight((AvatarLimb)i);
                if (w <= 0f) continue;
                Transform bone = _limb[i];
                Vector3 current = _limbChild[i].position - bone.position;
                Vector3 target = _root.rotation * pose.Direction((AvatarLimb)i);
                if (w < 1f) target = Vector3.Slerp(current.normalized, target, w);
                bone.rotation = Quaternion.FromToRotation(current, target) * bone.rotation;
            }
        }

        private Vector3 RootPoint(Transform t) => Quaternion.Inverse(_root.rotation) * (t.position - _root.position);

        private static Quaternion TorsoFrame(Vector3 up, Vector3 right)
        {
            Vector3 forward = Vector3.Cross(right, up);
            if (forward.sqrMagnitude < 1e-8f) forward = Vector3.forward;
            return Quaternion.LookRotation(forward, up);
        }
    }
}
