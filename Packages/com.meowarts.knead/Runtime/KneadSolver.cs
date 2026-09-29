using System;
using UnityEngine;

namespace Meowarts.Knead
{
    public enum KneadInterpolation
    {
        [Tooltip("Tricubic B-spline over 4×4×4 handles. Smooth, no creases.")]
        Cubic,
        [Tooltip("Trilinear with smoothstep weights. Soft but cheap.")]
        LinearSmooth,
        [Tooltip("Plain trilinear. Creases on cell edges, like a folded box.")]
        LinearSharp,
    }

    // Free-form deformation in normalized box space (0..1 inside the box). Handles are offsets from the rest
    // grid and the solver interpolates the offset, so a lattice at rest is exactly the identity whatever the
    // interpolation. Points outside the box get the offset of the nearest point on it (no tearing).
    public sealed class KneadSolver
    {
        public const int MaxResolution = 16;

        public Vector3Int Resolution { get; private set; } = new(2, 2, 2);
        public KneadInterpolation Interpolation { get; private set; }

        // Largest |offset| per axis over all handles, ghosts included. Every deformed point moves by at most this,
        // since each interpolation is a convex combination of the padded handles.
        public Vector3 MaxOffset { get; private set; }

        // (r + 2)^3 handles: the real ones plus a ring of ghosts linearly extrapolated from the edges, so cubic
        // has 4 neighbors everywhere and still reproduces linear offsets exactly (no shrinking at the edges).
        internal Vector3[] padded = Array.Empty<Vector3>();
        internal int padX, padXY;

        public static int Count(Vector3Int r) => r.x * r.y * r.z;
        public static int Index(Vector3Int r, int x, int y, int z) => (z * r.y + y) * r.x + x;

        public static Vector3Int Clamp(Vector3Int r) => new(
            Mathf.Clamp(r.x, 2, MaxResolution), Mathf.Clamp(r.y, 2, MaxResolution), Mathf.Clamp(r.z, 2, MaxResolution));

        // Rest position of handle (x, y, z), normalized.
        public static Vector3 Rest(Vector3Int r, int x, int y, int z) =>
            new(x / (r.x - 1f), y / (r.y - 1f), z / (r.z - 1f));

        public static Vector3 Rest(Vector3Int r, int i)
        {
            int x = i % r.x, y = i / r.x % r.y, z = i / (r.x * r.y);
            return Rest(r, x, y, z);
        }

        public void Set(Vector3[] offsets, Vector3Int resolution, KneadInterpolation interpolation)
        {
            Resolution = resolution;
            Interpolation = interpolation;
            if (offsets.Length != Count(resolution))
                throw new ArgumentException($"Expected {Count(resolution)} offsets for {resolution}, got {offsets.Length}");
            padX = resolution.x + 2;
            padXY = padX * (resolution.y + 2);
            int n = padXY * (resolution.z + 2);
            if (padded.Length != n) padded = new Vector3[n];

            Vector3 max = Vector3.zero;
            for (int z = -1; z <= resolution.z; z++)
            for (int y = -1; y <= resolution.y; y++)
            for (int x = -1; x <= resolution.x; x++)
            {
                var o = Extrapolated(offsets, resolution, x, y, z);
                padded[(z + 1) * padXY + (y + 1) * padX + x + 1] = o;
                max = Vector3.Max(max, new Vector3(Mathf.Abs(o.x), Mathf.Abs(o.y), Mathf.Abs(o.z)));
            }
            MaxOffset = max;
        }

        static Vector3 Extrapolated(Vector3[] o, Vector3Int r, int x, int y, int z)
        {
            if (x < 0) return 2f * Extrapolated(o, r, 0, y, z) - Extrapolated(o, r, 1, y, z);
            if (x >= r.x) return 2f * Extrapolated(o, r, r.x - 1, y, z) - Extrapolated(o, r, r.x - 2, y, z);
            if (y < 0) return 2f * Extrapolated(o, r, x, 0, z) - Extrapolated(o, r, x, 1, z);
            if (y >= r.y) return 2f * Extrapolated(o, r, x, r.y - 1, z) - Extrapolated(o, r, x, r.y - 2, z);
            if (z < 0) return 2f * Extrapolated(o, r, x, y, 0) - Extrapolated(o, r, x, y, 1);
            if (z >= r.z) return 2f * Extrapolated(o, r, x, y, r.z - 1) - Extrapolated(o, r, x, y, r.z - 2);
            return o[Index(r, x, y, z)];
        }

        // Weights and their derivatives (per normalized unit) along one axis. Returns the first node (padded index).
        internal static int Axis(float t, int r, KneadInterpolation interp, Span<float> w, Span<float> d, out int count)
        {
            float s = r - 1;
            bool outside = t < 0f || t > 1f;
            t = Mathf.Clamp01(t);
            float x = t * s;
            int c = Mathf.Min((int)x, r - 2);
            float f = x - c;
            if (outside) s = 0f; // constant beyond the box: no derivative

            switch (interp)
            {
                case KneadInterpolation.Cubic:
                {
                    float g = 1f - f, f2 = f * f, f3 = f2 * f;
                    w[0] = g * g * g / 6f;
                    w[1] = (3f * f3 - 6f * f2 + 4f) / 6f;
                    w[2] = (-3f * f3 + 3f * f2 + 3f * f + 1f) / 6f;
                    w[3] = f3 / 6f;
                    d[0] = -0.5f * g * g * s;
                    d[1] = (1.5f * f2 - 2f * f) * s;
                    d[2] = (-1.5f * f2 + f + 0.5f) * s;
                    d[3] = 0.5f * f2 * s;
                    count = 4;
                    return c; // node c - 1, +1 for the padding
                }
                case KneadInterpolation.LinearSmooth:
                {
                    float m = f * f * (3f - 2f * f), dm = 6f * f * (1f - f) * s;
                    w[0] = 1f - m; w[1] = m;
                    d[0] = -dm; d[1] = dm;
                    count = 2;
                    return c + 1;
                }
                default:
                    w[0] = 1f - f; w[1] = f;
                    d[0] = -s; d[1] = s;
                    count = 2;
                    return c + 1;
            }
        }

        public Vector3 Offset(Vector3 p) => Offset(p, out _, out _, out _);

        // Offset at p, and its partial derivatives along x, y and z (the Jacobian is I + [dx dy dz]).
        public Vector3 Offset(Vector3 p, out Vector3 dx, out Vector3 dy, out Vector3 dz)
        {
            Span<float> wx = stackalloc float[4], wy = stackalloc float[4], wz = stackalloc float[4];
            Span<float> ex = stackalloc float[4], ey = stackalloc float[4], ez = stackalloc float[4];
            int sx = Axis(p.x, Resolution.x, Interpolation, wx, ex, out int cx);
            int sy = Axis(p.y, Resolution.y, Interpolation, wy, ey, out int cy);
            int sz = Axis(p.z, Resolution.z, Interpolation, wz, ez, out int cz);

            Vector3 o = Vector3.zero;
            dx = dy = dz = Vector3.zero;
            for (int k = 0; k < cz; k++)
            for (int j = 0; j < cy; j++)
            {
                float wyz = wy[j] * wz[k], eyz = ey[j] * wz[k], wyez = wy[j] * ez[k];
                int row = (sz + k) * padXY + (sy + j) * padX + sx;
                for (int i = 0; i < cx; i++)
                {
                    var h = padded[row + i];
                    o += h * (wx[i] * wyz);
                    dx += h * (ex[i] * wyz);
                    dy += h * (wx[i] * eyz);
                    dz += h * (wx[i] * wyez);
                }
            }
            return o;
        }

        public Vector3 Deform(Vector3 p) => p + Offset(p);

        // Deforms mesh data. boxFromMesh maps mesh space to normalized box space, meshFromBox is its inverse.
        // Normals use the inverse transpose of the Jacobian, tangents the Jacobian: exact, per vertex.
        public void Deform(Matrix4x4 boxFromMesh, Matrix4x4 meshFromBox, Vector3[] positions, Vector3[] normals,
            Vector4[] tangents, Vector3[] outPositions, Vector3[] outNormals, Vector4[] outTangents)
        {
            bool hasN = normals != null && normals.Length == positions.Length;
            bool hasT = tangents != null && tangents.Length == positions.Length;
            var toBoxT = boxFromMesh.transpose;
            var toMeshT = meshFromBox.transpose;
            for (int v = 0; v < positions.Length; v++)
            {
                var p = boxFromMesh.MultiplyPoint3x4(positions[v]);
                var o = Offset(p, out var dx, out var dy, out var dz);
                outPositions[v] = meshFromBox.MultiplyPoint3x4(p + o);
                if (!hasN && !hasT) continue;
                var a = new Vector3(1f + dx.x, dx.y, dx.z);
                var b = new Vector3(dy.x, 1f + dy.y, dy.z);
                var c = new Vector3(dz.x, dz.y, 1f + dz.z);
                if (hasN)
                {
                    var n = toMeshT.MultiplyVector(normals[v]);
                    n = n.x * Vector3.Cross(b, c) + n.y * Vector3.Cross(c, a) + n.z * Vector3.Cross(a, b);
                    outNormals[v] = toBoxT.MultiplyVector(n).normalized;
                }
                if (hasT)
                {
                    var t4 = tangents[v];
                    var t = boxFromMesh.MultiplyVector(t4);
                    t = a * t.x + b * t.y + c * t.z;
                    t = meshFromBox.MultiplyVector(t).normalized;
                    outTangents[v] = new Vector4(t.x, t.y, t.z, t4.w);
                }
            }
        }

        // Trilinear weights of the real handles around p, for spreading a force over the handles that shape p.
        internal static int Trilinear(Vector3 p, Vector3Int r, Span<int> index, Span<float> weight)
        {
            Span<float> w = stackalloc float[2 * 3];
            Span<int> c = stackalloc int[3];
            for (int a = 0; a < 3; a++)
            {
                float x = Mathf.Clamp01(p[a]) * (r[a] - 1);
                c[a] = Mathf.Min((int)x, r[a] - 2);
                float f = x - c[a];
                w[a * 2] = 1f - f;
                w[a * 2 + 1] = f;
            }
            int n = 0;
            for (int k = 0; k < 2; k++)
            for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                index[n] = Index(r, c[0] + i, c[1] + j, c[2] + k);
                weight[n] = w[i] * w[2 + j] * w[4 + k];
                n++;
            }
            return n;
        }

        // Resamples handle offsets to a new resolution, keeping the shape of the cage (trilinear, not the
        // smoothed surface, so going up and back down gives the same handles).
        public static Vector3[] Resample(Vector3[] offsets, Vector3Int from, Vector3Int to)
        {
            var result = new Vector3[Count(to)];
            if (offsets == null || offsets.Length != Count(from)) return result;
            if (from == to) return (Vector3[])offsets.Clone();
            Span<int> index = stackalloc int[8];
            Span<float> weight = stackalloc float[8];
            for (int i = 0; i < result.Length; i++)
            {
                int n = Trilinear(Rest(to, i), from, index, weight);
                for (int k = 0; k < n; k++) result[i] += offsets[index[k]] * weight[k];
            }
            return result;
        }
    }
}
