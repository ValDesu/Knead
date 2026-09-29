using UnityEngine;
using UnityEngine.Rendering;

namespace Meowarts.Knead
{
    // Compute shader path for MeshFilter targets. Falls back to the CPU (returns null) when compute shaders aren't
    // supported (WebGL, old mobile) or the mesh stores positions, normals or tangents in a non-float32 format.
    static class KneadGpu
    {
        internal sealed class Target
        {
            public GraphicsBuffer source, dest;
            public int count, stride, position, normal, tangent;
        }

        struct Source
        {
            public Vector3 position, normal;
            public Vector4 tangent;
        }

        static ComputeShader shader;
        static bool loaded;

        // Off to test the CPU path, or to force it everywhere.
        internal static bool Enabled = true;

        static ComputeShader Shader
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    if (SystemInfo.supportsComputeShaders) shader = Resources.Load<ComputeShader>("KneadDeform");
                }
                return shader;
            }
        }

        static bool Float(Mesh mesh, VertexAttribute a, int dimension, int stream, out int offset)
        {
            offset = -1;
            if (!mesh.HasVertexAttribute(a)) return true;
            if (mesh.GetVertexAttributeStream(a) != stream ||
                mesh.GetVertexAttributeFormat(a) != VertexAttributeFormat.Float32 ||
                mesh.GetVertexAttributeDimension(a) != dimension) return false;
            offset = mesh.GetVertexAttributeOffset(a);
            return true;
        }

        internal static Target Create(Mesh mesh, Vector3[] positions, Vector3[] normals, Vector4[] tangents)
        {
            if (!Enabled || !Shader || positions.Length == 0) return null;
            int stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
            if (!Float(mesh, VertexAttribute.Position, 3, stream, out int p) ||
                !Float(mesh, VertexAttribute.Normal, 3, stream, out int n) ||
                !Float(mesh, VertexAttribute.Tangent, 4, stream, out int t)) return null;

            mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            var t0 = new Target
            {
                count = positions.Length,
                stride = mesh.GetVertexBufferStride(stream),
                position = p,
                normal = normals.Length == positions.Length ? n : -1,
                tangent = tangents.Length == positions.Length ? t : -1,
                dest = mesh.GetVertexBuffer(stream),
            };
            var data = new Source[positions.Length];
            for (int i = 0; i < data.Length; i++)
            {
                data[i].position = positions[i];
                if (t0.normal >= 0) data[i].normal = normals[i];
                if (t0.tangent >= 0) data[i].tangent = tangents[i];
            }
            t0.source = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, 40);
            t0.source.SetData(data);
            return t0;
        }

        internal static void Deform(Target t, KneadSolver solver, GraphicsBuffer offsets, Matrix4x4 boxFromMesh, Matrix4x4 meshFromBox)
        {
            var cs = Shader;
            cs.SetBuffer(0, "_Source", t.source);
            cs.SetBuffer(0, "_Offsets", offsets);
            cs.SetBuffer(0, "_Dest", t.dest);
            cs.SetInt("_Count", t.count);
            cs.SetInt("_Stride", t.stride);
            cs.SetInt("_PositionOffset", t.position);
            cs.SetInt("_NormalOffset", t.normal);
            cs.SetInt("_TangentOffset", t.tangent);
            var r = solver.Resolution;
            cs.SetInts("_Resolution", r.x, r.y, r.z, 0);
            cs.SetInt("_Interpolation", (int)solver.Interpolation);
            cs.SetMatrix("_BoxFromMesh", boxFromMesh);
            cs.SetMatrix("_MeshFromBox", meshFromBox);
            cs.Dispatch(0, (t.count + 63) / 64, 1, 1);
        }

        internal static void Release(Target t)
        {
            if (t == null) return;
            t.source?.Release();
            t.dest?.Release();
        }
    }
}
