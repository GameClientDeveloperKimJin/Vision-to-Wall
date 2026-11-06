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

            Mesh mesh = PlayerAvatarView.BuildPointMesh(source, 100);
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
        public void PointMesh_StaysOnSourceSurface_AndIsRepeatable()
        {
            _object = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Mesh source = _object.GetComponent<MeshFilter>().sharedMesh;

            Mesh a = PlayerAvatarView.BuildPointMesh(source, 200);
            Mesh b = PlayerAvatarView.BuildPointMesh(source, 200);
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
