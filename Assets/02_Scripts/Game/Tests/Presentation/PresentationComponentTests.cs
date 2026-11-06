using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace VisionToWall.Game.Tests
{
    public sealed class PresentationComponentTests
    {
        private GameObject _object;

        [TearDown]
        public void TearDown()
        {
            if (_object != null) Object.DestroyImmediate(_object);
        }

        [Test]
        public void AudioDirector_WithNoClips_SkipsEveryCue()
        {
            _object = new GameObject("Audio");
            var audio = _object.AddComponent<AudioDirector>();

            foreach (AudioCue cue in System.Enum.GetValues(typeof(AudioCue))) audio.Play(cue);
            audio.PlayBgm();
            audio.StopBgm();

            LogAssert.NoUnexpectedReceived();
            Assert.That(audio.BgmPlaying, Is.False);
        }

        [Test]
        public void PointMesh_HasFourVerticesPerPoint_WithSeedInUv()
        {
            _object = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Mesh source = _object.GetComponent<MeshFilter>().sharedMesh;

            Mesh mesh = PlayerAvatarView.BuildPointMesh(source, 100, Quaternion.identity);
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(400));
                Assert.That(mesh.GetIndexCount(0), Is.EqualTo(600u));

                var uv = new System.Collections.Generic.List<Vector3>();
                mesh.GetUVs(0, uv);
                Assert.That(uv[0].z, Is.EqualTo(uv[3].z), "All corners of a point share one seed.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PointMesh_BakesRotation_IntoPositionsAndBounds()
        {
            _object = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Mesh source = _object.GetComponent<MeshFilter>().sharedMesh; // tall along Y

            Mesh mesh = PlayerAvatarView.BuildPointMesh(source, 500, Quaternion.Euler(90f, 0f, 0f));
            try
            {
                Assert.That(mesh.bounds.size.z, Is.GreaterThan(mesh.bounds.size.y * 1.5f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void FacingYaw_TurnsTowardScreenCenter()
        {
            Assert.That(PlayerAvatarView.FacingYawDeg(0f, 35f), Is.EqualTo(35f).Within(0.001f), "left of the boss: turn right");
            Assert.That(PlayerAvatarView.FacingYawDeg(1f, 35f), Is.EqualTo(-35f).Within(0.001f), "right of the boss: turn left");
            Assert.That(PlayerAvatarView.FacingYawDeg(0.5f, 35f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(PlayerAvatarView.FacingYawDeg(-1f, 35f), Is.EqualTo(35f).Within(0.001f), "clamped");
        }

        [Test]
        public void PointMesh_StaysOnSourceSurface_AndIsRepeatable()
        {
            _object = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Mesh source = _object.GetComponent<MeshFilter>().sharedMesh;

            Mesh a = PlayerAvatarView.BuildPointMesh(source, 200, Quaternion.identity);
            Mesh b = PlayerAvatarView.BuildPointMesh(source, 200, Quaternion.identity);
            try
            {
                Bounds bounds = source.bounds;
                bounds.Expand(0.001f);
                Vector3[] va = a.vertices;
                Vector3[] vb = b.vertices;
                for (int i = 0; i < va.Length; i++)
                {
                    Assert.That(bounds.Contains(va[i]), Is.True);
                    Assert.That(va[i], Is.EqualTo(vb[i]));
                }
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }
    }
}
