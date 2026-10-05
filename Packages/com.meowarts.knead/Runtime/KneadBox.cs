using System;
using System.Collections.Generic;
using UnityEngine;

namespace Meowarts.Knead
{
    [Serializable]
    public sealed class KneadPose
    {
        [SerializeField] internal string name;
        [SerializeField] internal Vector3[] offsets;

        public string Name => name;
        public IReadOnlyList<Vector3> Offsets => offsets;
    }

    // A lattice box that deforms the renderers on its GameObject and children. Handles are offsets from the rest
    // grid in normalized box space (0..1), so poses survive resizing the box.
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("Knead/Knead Box"), Icon(IconPath)]
    public sealed class KneadBox : MonoBehaviour
    {
        internal const string IconPath = "Packages/com.meowarts.knead/Editor/Icons/KneadIcon.png";

        [SerializeField] Vector3 center;
        [SerializeField] Vector3 size = Vector3.one;
        [SerializeField] Vector3Int resolution = new(3, 3, 3);
        [SerializeField] KneadInterpolation interpolation;
        [Tooltip("The axis effects act along: Squish shortens it, Twist turns around it.")]
        [SerializeField] KneadAxis axis = KneadAxis.Y;
        [SerializeField] Vector3[] handles = new Vector3[27];
        [SerializeField] List<KneadPose> poses = new();
        [SerializeField] List<Renderer> targets = new();
        [SerializeField, HideInInspector] List<Mesh> sources = new(); // backup of the original meshes
        [Tooltip("Bake the deformed mesh at build time and remove the box: zero runtime cost. Always on for static-batched objects.")]
        [SerializeField] bool bake;

        internal static readonly List<KneadBox> Active = new();

        sealed class Live
        {
            public Renderer renderer;
            public Mesh source, mesh;
            public Vector3[] positions, normals, outPositions, outNormals;
            public Vector4[] tangents, outTangents;
            public Matrix4x4 last;
            public bool dirty = true;
            public KneadGpu.Target gpu;
        }

        sealed class Bouncing
        {
            public KneadTweenParams p;
            public float time;
            public KneadTweenHandle handle;
        }

        readonly List<Live> live = new();
        readonly List<Bouncing> bounces = new();
        readonly KneadSolver solver = new();
        Vector3[] final = Array.Empty<Vector3>(), applied;
        bool solverDirty = true;
        GraphicsBuffer gpuOffsets;

        // Tween toward a set of handles (a pose, or rest).
        Vector3[] tweenFrom, tweenTo;
        float tweenTime, tweenDuration;
        KneadEase tweenEase;
        bool tweenUnscaled;
        KneadTweenHandle tweenHandle;

        // Offsets written by a KneadPhysicsBox, normalized. Null without one.
        internal Vector3[] physics;

        public Vector3 Center { get => center; set { center = value; } }
        public Vector3 Size { get => size; set { size = value; } }
        public KneadInterpolation Interpolation { get => interpolation; set { interpolation = value; solverDirty = true; } }
        public KneadAxis Axis { get => axis; set => axis = value; }
        public bool Bake { get => bake; set => bake = value; }
        public IReadOnlyList<KneadPose> Poses => poses;
        public IReadOnlyList<Renderer> Targets => targets;
        public int HandleCount => handles.Length;
        public bool IsTweening => tweenHandle != null || bounces.Count > 0;

        public Vector3Int Resolution
        {
            get => resolution;
            set
            {
                value = KneadSolver.Clamp(value);
                if (value == resolution) return;
                StopPoseTween();
                handles = KneadSolver.Resample(handles, resolution, value);
                foreach (var pose in poses) pose.offsets = KneadSolver.Resample(pose.offsets, resolution, value);
                resolution = value;
                solverDirty = true;
            }
        }

        // ---------- Lifecycle ----------

        void Reset()
        {
            FindRenderers();
            handles = new Vector3[KneadSolver.Count(resolution)];
            FitToRenderers();
        }

        void OnEnable()
        {
            Validate();
            Active.Add(this);
            Attach();
            solverDirty = true;
            Apply();
        }

        void OnDisable()
        {
            Active.Remove(this);
            StopAllTweens();
            Detach();
            gpuOffsets?.Release();
            gpuOffsets = null;
        }

        void Validate()
        {
            resolution = KneadSolver.Clamp(resolution);
            int n = KneadSolver.Count(resolution);
            if (handles == null || handles.Length != n) handles = new Vector3[n];
            poses ??= new List<KneadPose>();
            foreach (var pose in poses)
                if (pose.offsets == null || pose.offsets.Length != n) pose.offsets = new Vector3[n];
            targets ??= new List<Renderer>();
            sources ??= new List<Mesh>();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            Validate();
            solverDirty = true;
            if (!isActiveAndEnabled) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (!this || !isActiveAndEnabled) return;
                if (!SameTargets()) { Detach(); Attach(); }
                Apply();
            };
        }
#endif

        void Update()
        {
            if (Application.isPlaying) Step(Time.deltaTime, Time.unscaledDeltaTime);
        }

        void LateUpdate() => Apply();

        // ---------- Targets ----------

        // Every renderer on this GameObject and its children, except the ones under another Knead Box.
        public void FindRenderers()
        {
            targets.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer || (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out _)))) continue;
                var owner = r.GetComponentInParent<KneadBox>(true);
                if (owner == this || owner == null) targets.Add(r);
            }
            if (isActiveAndEnabled) { Detach(); Attach(); Apply(); }
        }

        public void SetTargets(IEnumerable<Renderer> renderers)
        {
            Detach();
            targets.Clear();
            targets.AddRange(renderers);
            if (isActiveAndEnabled) { Attach(); Apply(); }
        }

        bool SameTargets()
        {
            int n = 0;
            foreach (var r in targets) if (r) n++;
            if (n != live.Count) return false;
            foreach (var l in live) if (!targets.Contains(l.renderer)) return false;
            return true;
        }

        static Mesh GetMesh(Renderer r) => r switch
        {
            SkinnedMeshRenderer s => s.sharedMesh,
            MeshRenderer m when m.TryGetComponent<MeshFilter>(out var f) => f.sharedMesh,
            _ => null,
        };

        static void SetMesh(Renderer r, Mesh mesh)
        {
            if (r is SkinnedMeshRenderer s) s.sharedMesh = mesh;
            else if (r && r.TryGetComponent<MeshFilter>(out var f)) f.sharedMesh = mesh;
        }

        static bool IsKneadMesh(Mesh m) => m && (m.hideFlags & HideFlags.DontSave) != 0 && m.name.EndsWith(" (Knead)");

        // Why a target can't be deformed, or null.
        internal string Problem(Renderer r)
        {
            if (!r) return "Missing renderer.";
            var mesh = GetMesh(r);
            if (live.Exists(l => l.renderer == r)) return null;
            if (!mesh) return $"{r.name} has no mesh.";
            if (IsKneadMesh(mesh)) return $"{r.name} is already deformed by another Knead Box.";
            if (!mesh.isReadable) return $"{mesh.name} needs Read/Write enabled in its import settings.";
            return null;
        }

        void Attach()
        {
            if (live.Count > 0) return;
            while (sources.Count < targets.Count) sources.Add(null);
            for (int i = 0; i < targets.Count; i++)
            {
                var r = targets[i];
                if (!r) continue;
                var source = GetMesh(r);
                // A scene saved while deformed (the save hook missed it) lost its mesh: restore the backup.
                if (!source && sources[i]) { source = sources[i]; SetMesh(r, source); }
                if (!source || IsKneadMesh(source) || !source.isReadable) continue;
                sources[i] = source;

                var l = new Live { renderer = r, source = source };
                l.mesh = Instantiate(source);
                l.mesh.name = source.name + " (Knead)";
                l.mesh.hideFlags = HideFlags.DontSave;
                l.mesh.MarkDynamic();
                l.positions = source.vertices;
                l.normals = source.normals;
                l.tangents = source.tangents;
                l.outPositions = (Vector3[])l.positions.Clone();
                l.outNormals = (Vector3[])l.normals.Clone();
                l.outTangents = (Vector4[])l.tangents.Clone();
                if (r is MeshRenderer) l.gpu = KneadGpu.Create(l.mesh, l.positions, l.normals, l.tangents);
                SetMesh(r, l.mesh);
                live.Add(l);
            }
        }

        // Puts the original meshes back (before saving a scene, when disabled).
        internal void Detach()
        {
            foreach (var l in live)
            {
                if (l.renderer && GetMesh(l.renderer) == l.mesh) SetMesh(l.renderer, l.source);
                KneadGpu.Release(l.gpu);
                if (Application.isPlaying) Destroy(l.mesh);
                else DestroyImmediate(l.mesh);
            }
            live.Clear();
        }

        internal void Reattach()
        {
            if (!isActiveAndEnabled) return;
            Detach();
            Attach();
            Apply();
        }

        // ---------- Deformation ----------

        internal Vector3 SafeSize => new(Mathf.Max(Mathf.Abs(size.x), 1e-4f), Mathf.Max(Mathf.Abs(size.y), 1e-4f),
            Mathf.Max(Mathf.Abs(size.z), 1e-4f));

        // Local space of this transform → normalized box space.
        internal Matrix4x4 NormalizedFromLocal
        {
            get
            {
                var s = SafeSize;
                return Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0.5f)) *
                       Matrix4x4.Scale(new Vector3(1f / s.x, 1f / s.y, 1f / s.z)) * Matrix4x4.Translate(-center);
            }
        }

        internal Vector3 LocalFromNormalized(Vector3 n) => center + Vector3.Scale(n - new Vector3(0.5f, 0.5f, 0.5f), SafeSize);
        internal Vector3 NormalizedFromLocalPoint(Vector3 p) => NormalizedFromLocal.MultiplyPoint3x4(p);

        Matrix4x4 BoxFromMesh(Transform t) => NormalizedFromLocal * transform.worldToLocalMatrix * t.localToWorldMatrix;

        // handles + bounces + physics, normalized.
        internal void ComputeFinal(Vector3[] into)
        {
            Array.Copy(handles, into, handles.Length);
            if (physics != null && physics.Length == into.Length)
                for (int i = 0; i < into.Length; i++) into[i] += physics[i];
            if (bounces.Count == 0) return;
            var s = SafeSize;
            for (int b = 0; b < bounces.Count; b++)
            {
                var p = bounces[b].p;
                float u = (bounces[b].time - p.Delay) / Mathf.Max(p.Duration, 1e-4f);
                float elasticity = p.Effect == KneadEffect.Wobble ? Mathf.Max(p.Elasticity, 0.8f) : p.Elasticity;
                float amount = p.Amount * KneadTween.Envelope(u, elasticity);
                if (amount == 0f) continue;
                for (int i = 0; i < into.Length; i++)
                    into[i] += KneadTween.EffectOffset(p.Effect, KneadSolver.Rest(resolution, i), amount, axis, s);
            }
        }

        // Deforms the targets if anything changed since the last call. Costs nothing at rest.
        public void Apply()
        {
            if (final.Length != handles.Length) { final = new Vector3[handles.Length]; solverDirty = true; }
            ComputeFinal(final);
            bool changed = solverDirty || applied == null || applied.Length != final.Length;
            if (!changed)
                for (int i = 0; i < final.Length && !changed; i++) changed = final[i] != applied[i];
            if (changed)
            {
                solver.Set(final, resolution, interpolation);
                applied = (Vector3[])final.Clone();
                solverDirty = false;
                if (gpuOffsets != null && gpuOffsets.count != solver.padded.Length) { gpuOffsets.Release(); gpuOffsets = null; }
            }

            bool uploaded = false;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var l = live[i];
                if (!l.renderer || GetMesh(l.renderer) != l.mesh)
                {
                    // The mesh was swapped from outside (a new mesh in the MeshFilter): start over on the next frame.
                    Detach();
                    Attach();
                    return;
                }
                var m = BoxFromMesh(l.renderer.transform);
                if (!changed && !l.dirty && m == l.last) continue;
                l.last = m;
                l.dirty = false;
                var inverse = m.inverse;
                if (l.gpu != null)
                {
                    if (!uploaded)
                    {
                        gpuOffsets ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, solver.padded.Length, 12);
                        gpuOffsets.SetData(solver.padded);
                        uploaded = true;
                    }
                    KneadGpu.Deform(l.gpu, solver, gpuOffsets, m, inverse);
                    l.mesh.bounds = ExpandedBounds(l.source.bounds, inverse, solver.MaxOffset);
                }
                else
                {
                    solver.Deform(m, inverse, l.positions, l.normals, l.tangents, l.outPositions, l.outNormals, l.outTangents);
                    l.mesh.SetVertices(l.outPositions);
                    if (l.normals.Length > 0) l.mesh.SetNormals(l.outNormals);
                    if (l.tangents.Length > 0) l.mesh.SetTangents(l.outTangents);
                }
            }
        }

        // The source bounds grown by the largest possible offset, for culling when the GPU deforms.
        static Bounds ExpandedBounds(Bounds b, Matrix4x4 meshFromBox, Vector3 max)
        {
            var e = Vector3.zero;
            for (int a = 0; a < 3; a++)
            {
                var col = (Vector3)meshFromBox.GetColumn(a) * max[a];
                e += new Vector3(Mathf.Abs(col.x), Mathf.Abs(col.y), Mathf.Abs(col.z));
            }
            b.Expand(2f * e);
            return b;
        }

        // Where a world point ends up after the deformation (for colliders, snapping props to the surface).
        public Vector3 Deform(Vector3 worldPoint)
        {
            if (solverDirty || applied == null) Apply();
            var n = NormalizedFromLocalPoint(transform.InverseTransformPoint(worldPoint));
            return transform.TransformPoint(LocalFromNormalized(solver.Deform(n)));
        }

        // The deformed mesh of a target from the handles alone (no tweens, no physics), as a new mesh.
        internal Mesh BakeMesh(Renderer r)
        {
            int i = targets.IndexOf(r);
            var l = live.Find(x => x.renderer == r);
            var source = l?.source ?? (i >= 0 && i < sources.Count ? sources[i] : null) ?? GetMesh(r);
            if (!source || IsKneadMesh(source) || !source.isReadable) return null;
            var s = new KneadSolver();
            s.Set(handles, resolution, interpolation);
            var m = BoxFromMesh(r.transform);
            var pos = source.vertices;
            var nrm = source.normals;
            var tan = source.tangents;
            var outPos = new Vector3[pos.Length];
            var outNrm = new Vector3[nrm.Length];
            var outTan = new Vector4[tan.Length];
            s.Deform(m, m.inverse, pos, nrm, tan, outPos, outNrm, outTan);
            var mesh = Instantiate(source);
            mesh.name = source.name + " (Knead Baked)";
            mesh.SetVertices(outPos);
            if (nrm.Length > 0) mesh.SetNormals(outNrm);
            if (tan.Length > 0) mesh.SetTangents(outTan);
            mesh.RecalculateBounds();
            return mesh;
        }

        internal Mesh LiveMesh(Renderer r) => live.Find(x => x.renderer == r)?.mesh;
        internal Mesh SourceMesh(Renderer r) => live.Find(x => x.renderer == r)?.source;
        internal bool UsesGpu(Renderer r) => live.Find(x => x.renderer == r)?.gpu != null;
        internal KneadGpu.Target GpuTarget(Renderer r) => live.Find(x => x.renderer == r)?.gpu;

        // ---------- Box ----------

        // Resizes the box to the targets' bounds. Handles and poses are normalized, so they keep their shape.
        public void FitToRenderers()
        {
            var b = new Bounds();
            bool any = false;
            for (int i = 0; i < targets.Count; i++)
            {
                var r = targets[i];
                if (!r) continue;
                var mesh = live.Find(l => l.renderer == r)?.source ?? GetMesh(r);
                if (!mesh) continue;
                var mb = mesh.bounds;
                var toLocal = transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var p = toLocal.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            if (!any) b = new Bounds(Vector3.zero, Vector3.one);
            var s = b.size;
            float min = Mathf.Max(s.x, s.y, s.z) * 0.02f + 1e-3f; // flat meshes still get a thin box
            center = b.center;
            size = new Vector3(Mathf.Max(s.x, min), Mathf.Max(s.y, min), Mathf.Max(s.z, min));
        }

        // ---------- Handles ----------

        public Vector3 GetHandlePosition(int index) =>
            transform.TransformPoint(LocalFromNormalized(KneadSolver.Rest(resolution, index) + handles[index]));

        public Vector3 GetHandleRestPosition(int index) =>
            transform.TransformPoint(LocalFromNormalized(KneadSolver.Rest(resolution, index)));

        public void SetHandlePosition(int index, Vector3 worldPosition) =>
            handles[index] = NormalizedFromLocalPoint(transform.InverseTransformPoint(worldPosition)) - KneadSolver.Rest(resolution, index);

        // Offset from rest, in normalized box space (0.1 = 10% of the box size).
        public Vector3 GetHandleOffset(int index) => handles[index];
        public void SetHandleOffset(int index, Vector3 offset) => handles[index] = offset;

        public void ResetHandles()
        {
            StopPoseTween();
            Array.Clear(handles, 0, handles.Length);
        }

        internal Vector3[] RawHandles { get => handles; set => handles = value; }

        // ---------- Poses ----------

        public KneadPose FindPose(string poseName) => poses.Find(p => p.name == poseName);

        // Saves the current handles as a pose (replaces a pose with the same name).
        public KneadPose SavePose(string poseName)
        {
            var pose = FindPose(poseName);
            if (pose == null) poses.Add(pose = new KneadPose { name = poseName });
            pose.offsets = (Vector3[])handles.Clone();
            return pose;
        }

        public bool DeletePose(string poseName) => poses.RemoveAll(p => p.name == poseName) > 0;

        public bool RenamePose(string from, string to)
        {
            var pose = FindPose(from);
            if (pose == null || string.IsNullOrEmpty(to) || (from != to && FindPose(to) != null)) return false;
            pose.name = to;
            return true;
        }

        KneadPose PoseOrError(string poseName)
        {
            var pose = FindPose(poseName);
            if (pose == null)
                Debug.LogError($"Knead: no pose named \"{poseName}\" on {name}. Poses: " +
                               (poses.Count == 0 ? "none" : string.Join(", ", poses.ConvertAll(p => $"\"{p.name}\""))), this);
            return pose;
        }

        // Sets the handles to a pose, scaled by weight (0 = rest, 1 = the pose), right away.
        public void SetPose(string poseName, float weight = 1f)
        {
            var pose = PoseOrError(poseName);
            if (pose == null) return;
            StopPoseTween();
            for (int i = 0; i < handles.Length; i++) handles[i] = pose.offsets[i] * weight;
        }

        // ---------- Tweens ----------

        // Goes to the effect and springs back. Bounces add up; a new one of the same effect replaces the old one.
        public KneadTweenHandle Bounce(KneadTweenParams tween)
        {
            for (int i = bounces.Count - 1; i >= 0; i--)
            {
                if (bounces[i].p.Effect != tween.Effect) continue;
                var old = bounces[i].handle;
                bounces.RemoveAt(i);
                old.Complete();
            }
            var b = new Bouncing { p = tween, handle = new KneadTweenHandle(this) };
            bounces.Add(b);
            return b.handle;
        }

        public KneadTweenHandle Bounce(KneadEffect effect) => Bounce(KneadTween.Preset(effect));

        // Goes to a pose made in the editor (Save Pose) and stays there.
        public KneadTweenHandle TweenToPose(string poseName, float duration = 0.3f, KneadEase ease = KneadEase.OutBack,
            bool unscaledTime = false)
        {
            var pose = PoseOrError(poseName);
            return pose == null ? KneadTweenHandle.Done(this) : TweenHandles(pose.offsets, duration, ease, unscaledTime);
        }

        public KneadTweenHandle TweenToRest(float duration = 0.3f, KneadEase ease = KneadEase.OutBack, bool unscaledTime = false) =>
            TweenHandles(new Vector3[handles.Length], duration, ease, unscaledTime);

        internal KneadTweenHandle TweenHandles(Vector3[] to, float duration, KneadEase ease, bool unscaledTime)
        {
            StopPoseTween();
            tweenFrom = (Vector3[])handles.Clone();
            tweenTo = (Vector3[])to.Clone();
            tweenTime = 0f;
            tweenDuration = duration;
            tweenEase = ease;
            tweenUnscaled = unscaledTime;
            tweenHandle = new KneadTweenHandle(this);
            if (duration <= 0f) Step(0f, 0f);
            return tweenHandle ?? KneadTweenHandle.Done(this);
        }

        internal void StopTween(KneadTweenHandle handle)
        {
            if (handle == tweenHandle) { tweenHandle = null; tweenFrom = tweenTo = null; }
            bounces.RemoveAll(b => b.handle == handle);
        }

        void StopPoseTween()
        {
            var h = tweenHandle;
            tweenHandle = null;
            tweenFrom = tweenTo = null;
            h?.Complete();
        }

        public void StopAllTweens()
        {
            StopPoseTween();
            var list = bounces.ConvertAll(b => b.handle);
            bounces.Clear();
            foreach (var h in list) h.Complete();
        }

        // Advances the tweens. Called from Update in play mode, and by the editor preview.
        internal void Step(float deltaTime, float unscaledDeltaTime)
        {
            if (tweenHandle != null)
            {
                tweenTime += tweenUnscaled ? unscaledDeltaTime : deltaTime;
                float u = tweenDuration <= 0f ? 1f : tweenTime / tweenDuration;
                float e = KneadTween.Ease(u, tweenEase);
                if (tweenTo.Length == handles.Length)
                    for (int i = 0; i < handles.Length; i++) handles[i] = Vector3.LerpUnclamped(tweenFrom[i], tweenTo[i], e);
                if (u >= 1f) StopPoseTween();
            }
            for (int i = bounces.Count - 1; i >= 0; i--)
            {
                var b = bounces[i];
                b.time += b.p.UnscaledTime ? unscaledDeltaTime : deltaTime;
                if (b.time < b.p.Delay + b.p.Duration) continue;
                bounces.RemoveAt(i);
                b.handle.Complete();
            }
        }
    }
}
