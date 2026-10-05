using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Meowarts.Knead.Tests
{
    public class SolverTests
    {
        static readonly KneadInterpolation[] All =
            { KneadInterpolation.Cubic, KneadInterpolation.LinearSmooth, KneadInterpolation.LinearSharp };

        static Vector3[] Field(Vector3Int r, System.Func<Vector3, Vector3> f) =>
            Enumerable.Range(0, KneadSolver.Count(r)).Select(i => f(KneadSolver.Rest(r, i))).ToArray();

        static Vector3 RandomPoint(System.Random rng, float lo = 0f, float hi = 1f) => new(
            lo + (float)rng.NextDouble() * (hi - lo), lo + (float)rng.NextDouble() * (hi - lo), lo + (float)rng.NextDouble() * (hi - lo));

        static void Near(Vector3 expected, Vector3 actual, float tolerance, string message = null) =>
            Assert.Less((expected - actual).magnitude, tolerance, $"{message} expected {expected:F5} got {actual:F5}");

        [Test]
        public void RestLatticeIsIdentityEverywhere()
        {
            var rng = new System.Random(1);
            foreach (var interp in All)
            foreach (var r in new[] { new Vector3Int(2, 2, 2), new Vector3Int(3, 4, 5), new Vector3Int(16, 16, 16) })
            {
                var s = new KneadSolver();
                s.Set(new Vector3[KneadSolver.Count(r)], r, interp);
                for (int i = 0; i < 200; i++)
                {
                    var p = RandomPoint(rng, -0.5f, 1.5f);
                    Assert.AreEqual(p, s.Deform(p), $"{interp} {r}");
                }
            }
        }

        [Test]
        public void LinearOffsetsAreReproducedExactlyIncludingEdges()
        {
            // Linear precision: cubic with extrapolated ghosts must not shrink the mesh near the box edges.
            var rng = new System.Random(2);
            var a = new Matrix4x4(new Vector4(0.2f, 0.1f, -0.1f, 0), new Vector4(-0.3f, 0.05f, 0.2f, 0),
                new Vector4(0.1f, 0.4f, -0.2f, 0), new Vector4(0.05f, -0.1f, 0.3f, 1));
            foreach (var interp in All)
            foreach (var r in new[] { new Vector3Int(2, 2, 2), new Vector3Int(3, 3, 3), new Vector3Int(4, 2, 5) })
            {
                var s = new KneadSolver();
                s.Set(Field(r, p => a.MultiplyPoint3x4(p)), r, interp);
                for (int i = 0; i < 200; i++)
                {
                    var p = RandomPoint(rng);
                    // Smoothstep weights aren't linear inside a cell, except at resolution 2 on the nodes; skip it.
                    if (interp == KneadInterpolation.LinearSmooth) continue;
                    Near(a.MultiplyPoint3x4(p), s.Offset(p), 1e-5f, $"{interp} {r} at {p}");
                }
            }
        }

        [Test]
        public void TranslatingAllHandlesMovesEverythingTheSame()
        {
            var rng = new System.Random(3);
            var t = new Vector3(0.3f, -0.2f, 0.1f);
            foreach (var interp in All)
            {
                var r = new Vector3Int(3, 3, 3);
                var s = new KneadSolver();
                s.Set(Field(r, _ => t), r, interp);
                for (int i = 0; i < 100; i++) Near(t, s.Offset(RandomPoint(rng, -0.2f, 1.2f)), 1e-5f, interp.ToString());
            }
        }

        [Test]
        public void LinearSharpPassesThroughHandles()
        {
            var r = new Vector3Int(3, 3, 3);
            var rng = new System.Random(4);
            var offsets = Field(r, _ => RandomPoint(rng, -0.2f, 0.2f));
            var s = new KneadSolver();
            s.Set(offsets, r, KneadInterpolation.LinearSharp);
            for (int i = 0; i < offsets.Length; i++) Near(offsets[i], s.Offset(KneadSolver.Rest(r, i)), 1e-5f);
        }

        [Test]
        public void JacobianMatchesFiniteDifferences()
        {
            var rng = new System.Random(5);
            foreach (var interp in All)
            {
                var r = new Vector3Int(4, 3, 5);
                var s = new KneadSolver();
                s.Set(Field(r, _ => RandomPoint(rng, -0.1f, 0.1f)), r, interp);
                const float e = 1e-3f;
                for (int i = 0; i < 50; i++)
                {
                    var p = RandomPoint(rng, 0.01f, 0.99f);
                    s.Offset(p, out var dx, out var dy, out var dz);
                    Near((s.Offset(p + Vector3.right * e) - s.Offset(p - Vector3.right * e)) / (2 * e), dx, 5e-3f, $"{interp} dx");
                    Near((s.Offset(p + Vector3.up * e) - s.Offset(p - Vector3.up * e)) / (2 * e), dy, 5e-3f, $"{interp} dy");
                    Near((s.Offset(p + Vector3.forward * e) - s.Offset(p - Vector3.forward * e)) / (2 * e), dz, 5e-3f, $"{interp} dz");
                }
            }
        }

        [Test]
        public void NormalsAndTangentsFollowTheDeformation()
        {
            // Stretch x by 2 (offset = x): a 45° slope in xy becomes shallower, its normal steeper.
            var r = new Vector3Int(2, 2, 2);
            var s = new KneadSolver();
            s.Set(Field(r, p => new Vector3(p.x, 0, 0)), r, KneadInterpolation.LinearSharp);
            var n = new Vector3(1, 1, 0).normalized;
            var t = new Vector4(1, -1, 0, 0).normalized;
            t.w = -1;
            var pos = new[] { new Vector3(0.5f, 0.5f, 0.5f) };
            var outP = new Vector3[1];
            var outN = new Vector3[1];
            var outT = new Vector4[1];
            s.Deform(Matrix4x4.identity, Matrix4x4.identity, pos, new[] { n }, new[] { t }, outP, outN, outT);
            Near(new Vector3(1f, 0.5f, 0.5f), outP[0], 1e-5f);
            Near(new Vector3(1, 2, 0).normalized, outN[0], 1e-5f);
            Near(new Vector3(2, -1, 0).normalized, outT[0], 1e-5f);
            Assert.AreEqual(-1f, outT[0].w);
            Assert.AreEqual(0f, Vector3.Dot(outN[0], outT[0]), 1e-5f, "normal stays perpendicular to the tangent");
        }

        [Test]
        public void ResampleKeepsTheCage()
        {
            var a = new Vector3Int(3, 3, 3);
            var b = new Vector3Int(5, 2, 4);
            var linear = Field(a, p => new Vector3(p.y * 0.2f, -p.x * 0.1f, p.z * 0.3f));
            var up = KneadSolver.Resample(linear, a, b);
            for (int i = 0; i < up.Length; i++)
            {
                var p = KneadSolver.Rest(b, i);
                Near(new Vector3(p.y * 0.2f, -p.x * 0.1f, p.z * 0.3f), up[i], 1e-5f);
            }
            Near(linear[13], KneadSolver.Resample(KneadSolver.Resample(linear, a, new Vector3Int(5, 5, 5)), new Vector3Int(5, 5, 5), a)[13], 1e-5f);
        }
    }

    public class TweenTests
    {
        [Test]
        public void EnvelopeStartsAndEndsAtZeroAndPeaksAtOne()
        {
            foreach (float e in new[] { 0f, 0.35f, 1f })
            {
                float peak = 0f, low = 0f;
                for (float u = 0; u <= 1f; u += 0.001f)
                {
                    peak = Mathf.Max(peak, KneadTween.Envelope(u, e));
                    low = Mathf.Min(low, KneadTween.Envelope(u, e));
                }
                Assert.AreEqual(0f, KneadTween.Envelope(0f, e));
                Assert.AreEqual(0f, KneadTween.Envelope(1f, e));
                Assert.AreEqual(0f, KneadTween.Envelope(0.999f, e), 1e-4f);
                Assert.That(peak, Is.InRange(0.999f, 1.001f), $"elasticity {e}");
                if (e == 0f) Assert.AreEqual(0f, low, 1e-6f, "no swing back without elasticity");
                else Assert.Less(low, -0.1f, "a squish is followed by a stretch");
            }
        }

        [Test]
        public void EasesStartAtZeroAndLandOnOne()
        {
            foreach (KneadEase ease in System.Enum.GetValues(typeof(KneadEase)))
            {
                Assert.AreEqual(0f, KneadTween.Ease(0f, ease), 1e-5f, ease.ToString());
                Assert.AreEqual(1f, KneadTween.Ease(1f, ease), 1e-5f, ease.ToString());
                Assert.AreEqual(1f, KneadTween.Ease(0.9999f, ease), 0.01f, ease.ToString());
            }
            Assert.Greater(Enumerable.Range(0, 100).Max(i => KneadTween.Ease(i / 100f, KneadEase.Spring)), 1.1f, "spring overshoots");
        }

        [Test]
        public void SquishKeepsTheBottomAndTheVolume()
        {
            var size = new Vector3(2f, 1f, 1f);
            var top = KneadTween.EffectOffset(KneadEffect.Squish, new Vector3(1f, 1f, 0.5f), 0.3f, KneadAxis.Y, size);
            var bottom = KneadTween.EffectOffset(KneadEffect.Squish, new Vector3(1f, 0f, 0.5f), 0.3f, KneadAxis.Y, size);
            Assert.AreEqual(-0.3f, top.y, 1e-5f, "top comes down 30%");
            Assert.AreEqual(0f, bottom.y, 1e-6f, "bottom stays planted");
            float width = 1f + 2f * top.x; // the x = 1 face moves out by top.x, the x = 0 face mirrors it
            Assert.AreEqual(1f, 0.7f * width * width, 1e-4f, "volume kept");
            var stretch = KneadTween.EffectOffset(KneadEffect.Stretch, new Vector3(1f, 1f, 0.5f), 0.3f, KneadAxis.Y, size);
            Assert.AreEqual(0.3f, stretch.y, 1e-5f);
            Assert.Less(stretch.x, 0f);
        }

        [Test]
        public void TwistAndBendAreRoundOnAnyBox()
        {
            var size = new Vector3(2f, 4f, 1f);
            // Twist 90°: the top +x corner of a square cross-section goes to -z (right-handed about +y).
            var t = KneadTween.EffectOffset(KneadEffect.Twist, new Vector3(1f, 1f, 0.5f), 90f, KneadAxis.Y, size);
            var moved = Vector3.Scale(new Vector3(0.5f, 0f, 0f) + t, size);
            Assert.AreEqual(1f, new Vector2(moved.x, moved.z).magnitude, 1e-4f, "stays on the circle");
            Assert.AreEqual(0f, KneadTween.EffectOffset(KneadEffect.Twist, new Vector3(1f, 0f, 0.5f), 90f, KneadAxis.Y, size).magnitude, 1e-6f);
            // Bend 90° along y: the top center ends up level with the arc's center, bent toward +x.
            var b = Vector3.Scale(KneadTween.EffectOffset(KneadEffect.Bend, new Vector3(0.5f, 1f, 0.5f), 90f, KneadAxis.Y, size), size);
            float radius = 4f / (Mathf.PI / 2f);
            Assert.AreEqual(radius, b.x, 1e-3f);
            Assert.AreEqual(radius - 4f, b.y, 1e-3f);
        }
    }

    public class BoxTests
    {
        GameObject go;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            KneadGpu.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (go) Object.DestroyImmediate(go);
            KneadGpu.Enabled = true;
        }

        static void Pinch(KneadBox box)
        {
            var r = box.Resolution;
            for (int i = 0; i < box.HandleCount; i++)
                if (KneadSolver.Rest(r, i).y > 0.9f) box.SetHandleOffset(i, new Vector3(0, -0.25f, 0));
        }

        [Test]
        public void AddingABoxFitsTheMeshAndDoesNothingAtRest()
        {
            var box = go.AddComponent<KneadBox>();
            Assert.AreEqual(Vector3.one, box.Size);
            Assert.AreEqual(Vector3.zero, box.Center);
            Assert.AreEqual(1, box.Targets.Count);
            var filter = go.GetComponent<MeshFilter>();
            Assert.AreNotEqual("Cube", filter.sharedMesh.name, "deforms a copy");
            CollectionAssert.AreEqual(box.SourceMesh(go.GetComponent<Renderer>()).vertices, filter.sharedMesh.vertices);
        }

        [Test]
        public void MovingHandlesDeformsTheMeshAndDisablingRestoresIt()
        {
            var box = go.AddComponent<KneadBox>();
            var filter = go.GetComponent<MeshFilter>();
            var source = box.SourceMesh(go.GetComponent<Renderer>());
            Pinch(box);
            box.Apply();
            float top = filter.sharedMesh.vertices.Max(v => v.y);
            Assert.AreEqual(0.25f, top, 1e-4f, "top comes down a quarter of the box");
            Assert.AreEqual(-0.5f, filter.sharedMesh.vertices.Min(v => v.y), 1e-4f, "bottom stays");
            Assert.AreEqual(box.Deform(go.transform.TransformPoint(new Vector3(0.5f, 0.5f, 0.5f))).y, 0.25f, 1e-4f);

            box.enabled = false;
            Assert.AreSame(source, filter.sharedMesh);
            box.enabled = true;
            Assert.AreEqual(0.25f, filter.sharedMesh.vertices.Max(v => v.y), 1e-4f);
        }

        [Test]
        public void BoxOnAParentDeformsTheChildInItsOwnSpace()
        {
            var parent = new GameObject("Parent");
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = new Vector3(3, 0, 0);
            go.transform.localRotation = Quaternion.Euler(0, 90, 0);
            go.transform.localScale = new Vector3(2, 1, 1);
            var box = parent.AddComponent<KneadBox>();
            Assert.AreEqual(new Vector3(3, 0, 0), box.Center);
            Assert.AreEqual(1f, box.Size.x, 1e-5f);
            Assert.AreEqual(2f, box.Size.z, 1e-5f, "the child's x scale is the parent's z");
            Pinch(box);
            box.Apply();
            var world = go.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => go.transform.TransformPoint(v));
            Assert.AreEqual(0.25f, world.Max(v => v.y), 1e-4f);
            Object.DestroyImmediate(parent);
        }

        [Test]
        public void SavingTheSceneNeverSavesTheTemporaryMesh()
        {
            var box = go.AddComponent<KneadBox>();
            Pinch(box);
            box.Apply();
            const string path = "Assets/KneadTestScene.unity";
            try
            {
                EditorSceneManager.SaveScene(go.scene, path);
                Assert.IsTrue(go.GetComponent<MeshFilter>().sharedMesh.name.EndsWith("(Knead)"), "deformed again after saving");
                Assert.IsFalse(System.IO.File.ReadAllText(path).Contains("m_Mesh: {fileID: 0}"), "saved with its real mesh");
                EditorSceneManager.OpenScene(path);
                var reopened = Object.FindAnyObjectByType<KneadBox>();
                Assert.AreEqual(0.25f, reopened.GetComponent<MeshFilter>().sharedMesh.vertices.Max(v => v.y), 1e-4f);
                go = reopened.gameObject;
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void ResolutionChangesKeepHandlesAndPoses()
        {
            var box = go.AddComponent<KneadBox>();
            Pinch(box);
            box.SavePose("Pinched");
            box.Resolution = new Vector3Int(5, 5, 5);
            Assert.AreEqual(125, box.HandleCount);
            Assert.AreEqual(125, box.FindPose("Pinched").Offsets.Count);
            box.Apply();
            Assert.AreEqual(0.25f, go.GetComponent<MeshFilter>().sharedMesh.vertices.Max(v => v.y), 1e-3f);
        }

        [Test]
        public void TweenToPoseGetsThereAndStays()
        {
            var box = go.AddComponent<KneadBox>();
            Pinch(box);
            box.SavePose("Pinched");
            box.ResetHandles();
            bool done = false;
            var h = box.TweenToPose("Pinched", 0.3f, KneadEase.InOut).OnComplete(() => done = true);
            Assert.IsTrue(h.IsPlaying);
            box.Step(0.15f, 0.15f);
            box.Apply();
            float mid = go.GetComponent<MeshFilter>().sharedMesh.vertices.Max(v => v.y);
            Assert.That(mid, Is.InRange(0.26f, 0.49f), "halfway");
            box.Step(0.2f, 0.2f);
            box.Apply();
            Assert.IsTrue(done);
            Assert.IsFalse(h.IsPlaying);
            Assert.AreEqual(0.25f, go.GetComponent<MeshFilter>().sharedMesh.vertices.Max(v => v.y), 1e-4f);

            box.TweenToRest(0.1f);
            box.Step(0.2f, 0.2f);
            box.Apply();
            Assert.AreEqual(0.5f, go.GetComponent<MeshFilter>().sharedMesh.vertices.Max(v => v.y), 1e-4f);
        }

        [Test]
        public void UnknownPoseLogsTheNamesInsteadOfThrowing()
        {
            var box = go.AddComponent<KneadBox>();
            box.SavePose("Happy");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no pose named \"Hapy\".*\"Happy\""));
            Assert.IsFalse(box.TweenToPose("Hapy").IsPlaying);
        }

        [Test]
        public void BounceSquishesThenReturnsToRest()
        {
            var box = go.AddComponent<KneadBox>();
            var filter = go.GetComponent<MeshFilter>();
            var h = box.Bounce(KneadTween.Squish.With(amount: 0.4f, duration: 0.5f, elasticity: 0f));
            float lowest = 1f;
            for (int i = 0; i < 30; i++)
            {
                box.Step(1f / 60f, 1f / 60f);
                box.Apply();
                lowest = Mathf.Min(lowest, filter.sharedMesh.vertices.Max(v => v.y));
            }
            Assert.AreEqual(0.5f - 0.4f, lowest, 0.02f, "squished by 40% of the box at the peak");
            Assert.IsFalse(h.IsPlaying);
            box.Apply();
            Assert.AreEqual(0.5f, filter.sharedMesh.vertices.Max(v => v.y), 1e-4f, "back at rest");
        }

        [Test]
        public void ANewBounceOfTheSameEffectReplacesTheOld()
        {
            var box = go.AddComponent<KneadBox>();
            var first = box.Bounce(KneadTween.Squish);
            var twist = box.Bounce(KneadTween.Twist);
            var second = box.Bounce(KneadTween.Squish);
            Assert.IsFalse(first.IsPlaying);
            Assert.IsTrue(twist.IsPlaying);
            Assert.IsTrue(second.IsPlaying);
        }

        [Test]
        public void BakeMeshUsesTheHandlesOnly()
        {
            var box = go.AddComponent<KneadBox>();
            Pinch(box);
            box.Bounce(KneadTween.Stretch);
            box.Step(0.1f, 0.1f);
            var baked = box.BakeMesh(go.GetComponent<Renderer>());
            Assert.AreEqual(0.25f, baked.vertices.Max(v => v.y), 1e-4f);
            Assert.AreEqual(0.25f, baked.bounds.max.y, 1e-4f);
            Object.DestroyImmediate(baked);
        }

        [Test]
        public void GpuMatchesCpu()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shaders on this device.");
            var cpuGo = go;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                var rng = new System.Random(7);
                var offsets = Enumerable.Range(0, 64).Select(_ => new Vector3((float)rng.NextDouble() - 0.5f,
                    (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.3f).ToArray();
                foreach (var interp in new[] { KneadInterpolation.Cubic, KneadInterpolation.LinearSmooth, KneadInterpolation.LinearSharp })
                {
                    Object.DestroyImmediate(sphere.GetComponent<KneadBox>());
                    Object.DestroyImmediate(cpuGo.GetComponent<KneadBox>());
                    cpuGo.GetComponent<MeshFilter>().sharedMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
                    KneadGpu.Enabled = false;
                    var cpu = cpuGo.AddComponent<KneadBox>();
                    KneadGpu.Enabled = true;
                    var gpu = sphere.AddComponent<KneadBox>();
                    var r = sphere.GetComponent<Renderer>();
                    Assert.IsTrue(gpu.UsesGpu(r));
                    foreach (var b in new[] { cpu, gpu })
                    {
                        b.Resolution = new Vector3Int(4, 4, 4);
                        b.Interpolation = interp;
                        for (int i = 0; i < 64; i++) b.SetHandleOffset(i, offsets[i]);
                        b.Apply();
                    }

                    var t = gpu.GpuTarget(r);
                    var bytes = new byte[t.count * t.stride];
                    t.dest.GetData(bytes);
                    var expected = cpuGo.GetComponent<MeshFilter>().sharedMesh;
                    var ep = expected.vertices;
                    var en = expected.normals;
                    var et = expected.tangents;
                    for (int v = 0; v < t.count; v++)
                    {
                        Near(ep[v], Read3(bytes, v * t.stride + t.position), 1e-4f, $"{interp} position {v}");
                        Near(en[v], Read3(bytes, v * t.stride + t.normal), 1e-3f, $"{interp} normal {v}");
                        Near(et[v], Read3(bytes, v * t.stride + t.tangent), 1e-3f, $"{interp} tangent {v}");
                    }
                    Assert.IsTrue(sphere.GetComponent<MeshFilter>().sharedMesh.bounds.Contains(ep.OrderBy(p => -p.y).First()));
                }
            }
            finally
            {
                Object.DestroyImmediate(sphere);
            }
        }

        [Test]
        public void SkinnedMeshesDeformBeforeSkinningAndKeepBlendShapes()
        {
            var cube = go.GetComponent<MeshFilter>().sharedMesh;
            var mesh = new Mesh { name = "Skinned" };
            mesh.SetVertices(cube.vertices);
            mesh.SetNormals(cube.normals);
            mesh.SetTriangles(cube.triangles, 0);
            mesh.boneWeights = cube.vertices.Select(_ => new BoneWeight { boneIndex0 = 0, weight0 = 1f }).ToArray();
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.AddBlendShapeFrame("Grow", 100f, cube.vertices.Select(v => v * 0.1f).ToArray(), null, null);
            Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(go.GetComponent<MeshFilter>());
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { go.transform };

            var box = go.AddComponent<KneadBox>();
            Pinch(box);
            box.Apply();
            Assert.AreEqual("Skinned (Knead)", skin.sharedMesh.name);
            Assert.AreEqual(1, skin.sharedMesh.blendShapeCount);
            var baked = new Mesh();
            skin.BakeMesh(baked);
            Assert.AreEqual(0.25f, baked.vertices.Max(v => v.y), 1e-4f, "skinning uses the deformed bind pose");
            box.enabled = false;
            Assert.AreSame(mesh, skin.sharedMesh);
            Object.DestroyImmediate(baked);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void StaticBakeReplacesTheBoxWithItsMesh()
        {
            var box = go.AddComponent<KneadBox>();
            Pinch(box);
            box.Apply();
            Meowarts.Knead.Editor.KneadBakeProcessor.Bake(box);
            Assert.IsFalse(go.GetComponent<KneadBox>());
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual("Cube (Knead Baked)", mesh.name);
            Assert.AreEqual(0.25f, mesh.vertices.Max(v => v.y), 1e-4f);
        }

        static Vector3 Read3(byte[] b, int offset) =>
            new(System.BitConverter.ToSingle(b, offset), System.BitConverter.ToSingle(b, offset + 4), System.BitConverter.ToSingle(b, offset + 8));

        static void Near(Vector3 expected, Vector3 actual, float tolerance, string message) =>
            Assert.Less((expected - actual).magnitude, tolerance, $"{message}: expected {expected:F5} got {actual:F5}");
    }

    public class PhysicsTests
    {
        GameObject go, weight;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            weight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            KneadGpu.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(weight);
            KneadGpu.Enabled = true;
        }

        (KneadBox, KneadPhysicsBox) Make(KneadMaterialPreset preset)
        {
            var p = go.AddComponent<KneadPhysicsBox>();
            p.Preset = preset;
            var box = go.GetComponent<KneadBox>();
            box.Resolution = new Vector3Int(4, 4, 4);
            p.ResetState();
            return (box, p);
        }

        // How far the top center has come down, as a fraction of the box.
        static float Sink(KneadBox box) => 0.5f - box.Deform(new Vector3(0f, 0.5f, 0f)).y;

        static float Run(KneadPhysicsBox p, KneadBox box, float seconds, System.Action<float> watch = null)
        {
            float peak = 0f;
            for (float t = 0; t < seconds; t += 0.02f)
            {
                p.Tick(0.02f);
                box.Apply();
                peak = Mathf.Max(peak, Sink(box));
                watch?.Invoke(t);
            }
            return peak;
        }

        [Test]
        public void PresetsFeelDifferent()
        {
            var report = new System.Text.StringBuilder();
            var results = new System.Collections.Generic.Dictionary<KneadMaterialPreset, (float rest, float peak, float after)>();
            bool sleeps = true;
            for (var preset = KneadMaterialPreset.Jelly; preset < KneadMaterialPreset.Custom; preset++)
            {
                if (go.GetComponent<KneadPhysicsBox>()) Object.DestroyImmediate(go.GetComponent<KneadPhysicsBox>());
                var (box, p) = Make(preset);
                // A 1 kg weight resting on top.
                p.SetLoad(weight.GetComponent<Collider>(), new Vector3(0, 0.5f, 0), Vector3.down * 9.81f);
                Run(p, box, 4f);
                float rest = Sink(box);
                p.SetLoad(weight.GetComponent<Collider>(), new Vector3(0, 0.5f, 0), Vector3.zero);
                Run(p, box, 4f);
                p.ResetState();
                // 1 kg dropped from 1 m.
                p.AddImpulse(new Vector3(0, 0.5f, 0), Vector3.down * Mathf.Sqrt(2f * 9.81f));
                float peak = Run(p, box, 6f);
                float after = Sink(box);
                results[preset] = (rest, peak, after);
                report.AppendLine($"{preset,-10} resting {rest:P1}  drop peak {peak:P1}  dent after {after:P1}  sleeping {p.IsSleeping}");
                sleeps &= p.IsSleeping;
            }
            Debug.Log("Knead presets (1 m box, 1 kg):\n" + report);
            Assert.IsTrue(sleeps, "every preset settles and sleeps");

            Assert.That(results[KneadMaterialPreset.Jelly].rest, Is.InRange(0.05f, 0.3f), "jelly sags under 1 kg");
            Assert.That(results[KneadMaterialPreset.Clay].rest, Is.LessThan(0.05f), "clay holds a resting kilo");
            Assert.That(results[KneadMaterialPreset.Jelly].after, Is.LessThan(0.005f), "jelly springs back");
            Assert.That(results[KneadMaterialPreset.Pillow].rest, Is.GreaterThan(results[KneadMaterialPreset.Rubber].rest));
            Assert.That(results[KneadMaterialPreset.Clay].after, Is.GreaterThan(0.02f), "clay keeps the mark");
            Assert.That(results[KneadMaterialPreset.Cardboard].after, Is.GreaterThan(0.01f), "a drop dents cardboard");
            Assert.That(results[KneadMaterialPreset.Cardboard].rest, Is.LessThan(0.02f), "a resting kilo doesn't");
            Assert.That(results[KneadMaterialPreset.Metal].after, Is.LessThan(0.01f), "a kilo doesn't dent metal");
            Assert.That(results[KneadMaterialPreset.Rock].peak, Is.LessThan(0.01f), "rock barely moves");
        }

        [Test]
        public void JellyBulgesSidewaysWhenSquished()
        {
            var (box, p) = Make(KneadMaterialPreset.Jelly);
            p.SetLoad(weight.GetComponent<Collider>(), new Vector3(0, 0.5f, 0), Vector3.down * 30f);
            Run(p, box, 3f);
            Assert.Greater(box.Deform(new Vector3(0.5f, 0.3f, 0f)).x, 0.5f, "the side pushes out");
        }

        [Test]
        public void MovingTheBoxMakesItJiggle()
        {
            var (box, p) = Make(KneadMaterialPreset.Jelly);
            float moved = 0f;
            Run(p, box, 1f, t =>
            {
                go.transform.position = new Vector3(t < 0.3f ? t * 10f : 3f, 0, 0); // slide, then stop dead
                moved = Mathf.Max(moved, Mathf.Abs(box.Deform(go.transform.TransformPoint(new Vector3(0.5f, 0.5f, 0.5f))).x -
                                                    go.transform.TransformPoint(new Vector3(0.5f, 0.5f, 0.5f)).x));
            });
            Assert.Greater(moved, 0.02f);
        }

        [Test]
        public void DentsReformOverTime()
        {
            var (box, p) = Make(KneadMaterialPreset.Clay);
            p.AddImpulse(new Vector3(0, 0.5f, 0), Vector3.down * Mathf.Sqrt(2f * 9.81f));
            Run(p, box, 4f);
            float dent = Sink(box);
            Assert.Greater(dent, 0.02f, "clay keeps the mark");
            p.ResetDents(2f);
            Run(p, box, 1f);
            Assert.That(Sink(box), Is.InRange(0.002f, dent * 0.8f), "halfway, the dent is partly back");
            Run(p, box, 3f);
            Assert.Less(Sink(box), 0.002f, "gone after the duration");
            Assert.IsTrue(p.IsSleeping);
        }

        [Test]
        public void ARigidbodySinksIntoTheDent()
        {
            var (box, p) = Make(KneadMaterialPreset.Pillow);
            weight.transform.position = new Vector3(0, 1.2f, 0);
            weight.transform.localScale = Vector3.one * 0.5f;
            var rb = weight.AddComponent<Rigidbody>();
            rb.mass = 5f;
            var mode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                for (int i = 0; i < 250; i++)
                {
                    p.Tick(0.02f);
                    Physics.Simulate(0.02f);
                    // Edit mode doesn't send collision messages: feed the weight's support force like OnCollisionStay would.
                    if (weight.transform.position.y < 0.8f)
                        p.SetLoad(weight.GetComponent<Collider>(), new Vector3(0, 0.5f, 0), Vector3.down * rb.mass * 9.81f);
                    box.Apply();
                }
                float dent = Sink(box);
                float bottom = weight.transform.position.y - 0.25f;
                Assert.Greater(dent, 0.05f, "the pillow dents");
                Assert.AreEqual(0.5f - dent, bottom, 0.03f, "the ball sits in the dent, not on the flat collider");
            }
            finally
            {
                Physics.simulationMode = mode;
            }
        }
    }
}
