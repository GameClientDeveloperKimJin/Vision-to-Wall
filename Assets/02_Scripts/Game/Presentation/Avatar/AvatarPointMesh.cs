using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VisionToWall.Game
{
    /// <summary>
    /// Glowing point mesh for <c>AvatarPoints.shader</c>: area-weighted surface samples, four vertices (one quad)
    /// per point. uv0 = quad corner (xy) and a per-point random (z); uv1 = the point's rest position in the figure
    /// frame, which the shader uses for the back creases (spine, shoulder blades) so they stay on the body when the
    /// mesh is skinned. With <c>skinned</c>, each point also gets the source's bone weights interpolated at the
    /// sample (top four, summing to 1) and the source bind poses, for a <see cref="SkinnedMeshRenderer"/>.
    /// </summary>
    public static class AvatarPointMesh
    {
        public const int Seed = 20261106; // same figure every run

        /// <param name="figureRotation">Mesh space to the figure frame (+y up, front +z); used for uv1 always, and
        /// for the positions only when not skinned (skinned positions stay in mesh space for the bind poses).</param>
        public static Mesh Build(Mesh source, int points, Quaternion figureRotation, bool skinned)
        {
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            int[] triangles = source.triangles;
            BoneWeight[] weights = skinned ? source.boneWeights : null;
            if (skinned && (weights == null || weights.Length != vertices.Length))
                throw new System.ArgumentException($"{source.name} has no bone weights.", nameof(source));
            int triangleCount = triangles.Length / 3;

            var cumulative = new float[triangleCount];
            float total = 0f;
            for (int t = 0; t < triangleCount; t++)
            {
                Vector3 a = vertices[triangles[t * 3]];
                Vector3 b = vertices[triangles[t * 3 + 1]];
                Vector3 c = vertices[triangles[t * 3 + 2]];
                total += Vector3.Cross(b - a, c - a).magnitude;
                cumulative[t] = total;
            }

            var positions = new List<Vector3>(points * 4);
            var pointNormals = new List<Vector3>(points * 4);
            var corners = new List<Vector3>(points * 4);
            var rest = new List<Vector3>(points * 4);
            BoneWeight[] pointWeights = skinned ? new BoneWeight[points * 4] : null;
            var indices = new int[points * 6];
            var random = new System.Random(Seed);
            bool hasNormals = normals != null && normals.Length == vertices.Length;
            Quaternion positionRotation = skinned ? Quaternion.identity : figureRotation;

            for (int p = 0; p < points; p++)
            {
                int t = System.Array.BinarySearch(cumulative, (float)random.NextDouble() * total);
                if (t < 0) t = ~t;
                t = Mathf.Min(t, triangleCount - 1);

                int ia = triangles[t * 3];
                int ib = triangles[t * 3 + 1];
                int ic = triangles[t * 3 + 2];
                float u = Mathf.Sqrt((float)random.NextDouble());
                float v = (float)random.NextDouble();
                float wa = 1f - u;
                float wb = u * (1f - v);
                float wc = u * v;

                Vector3 local = vertices[ia] * wa + vertices[ib] * wb + vertices[ic] * wc;
                Vector3 position = positionRotation * local;
                Vector3 restPosition = figureRotation * local;
                Vector3 normal = hasNormals
                    ? positionRotation * (normals[ia] * wa + normals[ib] * wb + normals[ic] * wc).normalized
                    : positionRotation * Quaternion.Inverse(figureRotation) * Vector3.up;
                float seed = (float)random.NextDouble();
                BoneWeight weight = skinned ? Blend(weights[ia], weights[ib], weights[ic], wa, wb, wc) : default;

                int first = p * 4;
                for (int corner = 0; corner < 4; corner++)
                {
                    positions.Add(position);
                    pointNormals.Add(normal);
                    rest.Add(restPosition);
                    if (skinned) pointWeights[first + corner] = weight;
                }
                corners.Add(new Vector3(-1f, -1f, seed));
                corners.Add(new Vector3(1f, -1f, seed));
                corners.Add(new Vector3(1f, 1f, seed));
                corners.Add(new Vector3(-1f, 1f, seed));

                int index = p * 6;
                indices[index] = first;
                indices[index + 1] = first + 1;
                indices[index + 2] = first + 2;
                indices[index + 3] = first;
                indices[index + 4] = first + 2;
                indices[index + 5] = first + 3;
            }

            var mesh = new Mesh { name = source.name + " Points" };
            if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(pointNormals);
            mesh.SetUVs(0, corners);
            mesh.SetUVs(1, rest);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            if (skinned)
            {
                mesh.boneWeights = pointWeights;
                mesh.bindposes = source.bindposes;
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Bone weights at a point inside a triangle: the corners' weights scaled by the barycentric weights, the same
        /// bones merged, the four largest kept (largest first), renormalized to sum 1. Setup only (allocates).
        /// </summary>
        public static BoneWeight Blend(BoneWeight a, BoneWeight b, BoneWeight c, float wa, float wb, float wc)
        {
            var sums = new Dictionary<int, float>(12);
            Add(sums, a, wa);
            Add(sums, b, wb);
            Add(sums, c, wc);

            var sorted = new List<KeyValuePair<int, float>>(sums);
            sorted.Sort((x, y) => y.Value.CompareTo(x.Value));
            var bones = new int[4];
            var w = new float[4];
            float sum = 0f;
            for (int k = 0; k < 4 && k < sorted.Count; k++)
            {
                bones[k] = sorted[k].Key;
                w[k] = sorted[k].Value;
                sum += w[k];
            }

            if (sum <= 0f) return new BoneWeight { weight0 = 1f }; // unweighted source: bind to bone 0
            return new BoneWeight
            {
                boneIndex0 = bones[0], weight0 = w[0] / sum,
                boneIndex1 = bones[1], weight1 = w[1] / sum,
                boneIndex2 = bones[2], weight2 = w[2] / sum,
                boneIndex3 = bones[3], weight3 = w[3] / sum,
            };
        }

        private static void Add(Dictionary<int, float> sums, BoneWeight bw, float scale)
        {
            Add(sums, bw.boneIndex0, bw.weight0 * scale);
            Add(sums, bw.boneIndex1, bw.weight1 * scale);
            Add(sums, bw.boneIndex2, bw.weight2 * scale);
            Add(sums, bw.boneIndex3, bw.weight3 * scale);
        }

        private static void Add(Dictionary<int, float> sums, int bone, float weight)
        {
            if (weight <= 0f) return;
            sums.TryGetValue(bone, out float current);
            sums[bone] = current + weight;
        }
    }
}
