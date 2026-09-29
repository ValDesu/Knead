using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Meowarts.Knead
{
    public enum KneadMaterialPreset { Jelly, Rubber, Pillow, Cardboard, Clay, Metal, Rock, Custom }

    [Serializable]
    public struct KneadMaterial
    {
        [Tooltip("How hard it pushes back (N/m per handle). Low = deep dents, high = small ones.")]
        [Min(0f)] public float Stiffness;
        [Tooltip("How fast the wobble dies. 0 = jelly, 1 = pillow.")]
        [Range(0f, 1f)] public float Damping;
        [Tooltip("Mass of the material (kg). A heavy box moves less from the same hit.")]
        [Min(0.001f)] public float Mass;
        [Tooltip("How far a hit spreads to the neighbor handles. 0 = local poke, high = the whole box moves.")]
        [Min(0f)] public float Spread;
        [Tooltip("0 = compresses like foam, 1 = keeps its volume and bulges sideways when squished.")]
        [Range(0f, 1f)] public float Volume;
        [Tooltip("How far it bends before a dent stays, as a fraction of the box size. 1 = never dents.")]
        [Range(0f, 1f)] public float Yield;
        [Tooltip("How much of the bend past the yield stays. 0 = springs back, 1 = stays.")]
        [Range(0f, 1f)] public float Plasticity;
        [Tooltip("Largest deformation, as a fraction of the box size, so nothing turns inside out.")]
        [Range(0f, 1f)] public float MaxDeformation;
        [Tooltip("How much the box jiggles from its own motion (starting, stopping, landing).")]
        [Min(0f)] public float Inertia;
        [Range(0f, 1f)] public float Bounciness;
        [Min(0f)] public float Friction;

        public static KneadMaterial Jelly => new()
        {
            Stiffness = 6f, Damping = 0.08f, Mass = 2f, Spread = 1.5f, Volume = 0.8f, Yield = 1f, Plasticity = 0f,
            MaxDeformation = 0.45f, Inertia = 1f, Bounciness = 0.4f, Friction = 0.7f,
        };

        public static KneadMaterial Rubber => new()
        {
            Stiffness = 18f, Damping = 0.15f, Mass = 1f, Spread = 2f, Volume = 0.5f, Yield = 1f, Plasticity = 0f,
            MaxDeformation = 0.3f, Inertia = 0.6f, Bounciness = 0.8f, Friction = 0.9f,
        };

        public static KneadMaterial Pillow => new()
        {
            Stiffness = 4f, Damping = 1f, Mass = 1f, Spread = 1f, Volume = 0f, Yield = 1f, Plasticity = 0f,
            MaxDeformation = 0.5f, Inertia = 0.3f, Bounciness = 0f, Friction = 0.8f,
        };

        public static KneadMaterial Cardboard => new()
        {
            Stiffness = 150f, Damping = 0.5f, Mass = 0.3f, Spread = 1f, Volume = 0f, Yield = 0.01f, Plasticity = 1f,
            MaxDeformation = 0.3f, Inertia = 0.2f, Bounciness = 0.1f, Friction = 0.6f,
        };

        public static KneadMaterial Clay => new()
        {
            Stiffness = 75f, Damping = 0.9f, Mass = 0.15f, Spread = 0.8f, Volume = 0.6f, Yield = 0.02f, Plasticity = 0.8f,
            MaxDeformation = 0.4f, Inertia = 0.5f, Bounciness = 0f, Friction = 0.9f,
        };

        public static KneadMaterial Metal => new()
        {
            Stiffness = 4000f, Damping = 0.4f, Mass = 20f, Spread = 2f, Volume = 0f, Yield = 0.01f, Plasticity = 0.9f,
            MaxDeformation = 0.2f, Inertia = 0.05f, Bounciness = 0.2f, Friction = 0.4f,
        };

        public static KneadMaterial Rock => new()
        {
            Stiffness = 20000f, Damping = 0.3f, Mass = 50f, Spread = 3f, Volume = 0f, Yield = 1f, Plasticity = 0f,
            MaxDeformation = 0.02f, Inertia = 0f, Bounciness = 0.15f, Friction = 0.7f,
        };

        public static KneadMaterial FromPreset(KneadMaterialPreset preset) => preset switch
        {
            KneadMaterialPreset.Rubber => Rubber,
            KneadMaterialPreset.Pillow => Pillow,
            KneadMaterialPreset.Cardboard => Cardboard,
            KneadMaterialPreset.Clay => Clay,
            KneadMaterialPreset.Metal => Metal,
            KneadMaterialPreset.Rock => Rock,
            _ => Jelly,
        };
    }

    // Makes a Knead Box react to physics: each handle is a point mass on a damped spring, linked to its neighbors,
    // pushed by collision impulses and resting weight, and by the box's own acceleration.
    [ExecuteAlways, RequireComponent(typeof(KneadBox)), DisallowMultipleComponent, AddComponentMenu("Knead/Knead Physics Box")]
    public sealed class KneadPhysicsBox : MonoBehaviour
    {
        [SerializeField] KneadMaterialPreset preset = KneadMaterialPreset.Jelly;
        [SerializeField] KneadMaterial material = KneadMaterial.Jelly;

        public KneadMaterialPreset Preset
        {
            get => preset;
            set
            {
                preset = value;
                if (value != KneadMaterialPreset.Custom) material = KneadMaterial.FromPreset(value);
                MaterialChanged();
            }
        }

        public KneadMaterial Material
        {
            get => material;
            set
            {
                material = value;
                preset = KneadMaterialPreset.Custom;
                for (var p = KneadMaterialPreset.Jelly; p < KneadMaterialPreset.Custom; p++)
                    if (KneadMaterial.FromPreset(p).Equals(value)) preset = p;
                MaterialChanged();
            }
        }

        public bool IsSleeping => sleeping;

        KneadBox box;
        Collider col;
        PhysicsMaterial physicsMaterial;

        // Per handle, in meters along the box axes.
        Vector3[] x = Array.Empty<Vector3>(), v, dent, force, output;
        float[] pressure;
        bool sleeping = true;

        struct Load { public Vector3 point, force; }
        readonly Dictionary<Collider, List<Load>> loads = new();

        Vector3 lastPosition, lastVelocity;
        int motionSamples;

        // Read from the physics thread by contact modification: written only between simulation steps.
        readonly KneadSolver contactSolver = new();
        Vector3[] contactOffsets = Array.Empty<Vector3>();
        Matrix4x4 normalizedFromWorld, worldFromNormalized;
        Vector3 centerWorld;
        bool contactReady;

        static readonly Dictionary<EntityId, KneadPhysicsBox> byCollider = new();
        static bool hooked;

        void Reset()
        {
            if (TryGetComponent<Collider>(out _)) return;
            var b = GetComponent<KneadBox>();
#if UNITY_EDITOR
            var c = UnityEditor.Undo.AddComponent<BoxCollider>(gameObject);
#else
            var c = gameObject.AddComponent<BoxCollider>();
#endif
            if (b) { c.center = b.Center; c.size = b.Size; }
        }

        void OnEnable()
        {
            box = GetComponent<KneadBox>();
            col = GetComponent<Collider>();
            EnsureSize();
            if (col)
            {
                col.hasModifiableContacts = true;
                byCollider[col.GetEntityId()] = this;
            }
            if (!hooked)
            {
                hooked = true;
                Physics.ContactModifyEvent += ModifyContacts;
                Physics.ContactModifyEventCCD += ModifyContacts;
            }
            MaterialChanged();
            motionSamples = 0;
        }

        void OnDisable()
        {
            if (col) byCollider.Remove(col.GetEntityId());
            contactReady = false;
            if (box) box.physics = null;
            if (physicsMaterial)
            {
                if (col && col.sharedMaterial == physicsMaterial) col.sharedMaterial = null;
                if (Application.isPlaying) Destroy(physicsMaterial);
                else DestroyImmediate(physicsMaterial);
            }
            loads.Clear();
        }

        void OnValidate()
        {
            if (preset != KneadMaterialPreset.Custom && !KneadMaterial.FromPreset(preset).Equals(material))
                material = KneadMaterial.FromPreset(preset);
            if (isActiveAndEnabled) MaterialChanged();
        }

        void MaterialChanged()
        {
            sleeping = false;
            if (!col || !Application.isPlaying) return;
            if (col.sharedMaterial && col.sharedMaterial != physicsMaterial) return; // the user's own material wins
            if (!physicsMaterial) physicsMaterial = new PhysicsMaterial("Knead") { hideFlags = HideFlags.DontSave };
            physicsMaterial.bounciness = material.Bounciness;
            physicsMaterial.dynamicFriction = material.Friction;
            physicsMaterial.staticFriction = material.Friction;
            col.sharedMaterial = physicsMaterial;
        }

        void EnsureSize()
        {
            if (!box) return;
            int n = box.HandleCount;
            if (x.Length == n) return;
            x = new Vector3[n];
            v = new Vector3[n];
            dent = new Vector3[n];
            force = new Vector3[n];
            output = new Vector3[n];
            pressure = new float[n];
            contactOffsets = new Vector3[n];
        }

        // Clears every dent and wobble.
        public void ResetState()
        {
            EnsureSize();
            Array.Clear(x, 0, x.Length);
            Array.Clear(v, 0, v.Length);
            Array.Clear(dent, 0, dent.Length);
            loads.Clear();
            motionSamples = 0;
            WriteOutput();
            sleeping = true;
        }

        // Clears the permanent dents; the box springs back.
        public void ResetDents()
        {
            Array.Clear(dent, 0, dent.Length);
            sleeping = false;
        }

        Vector3 WorldSize => Vector3.Scale(box.SafeSize, Abs(transform.lossyScale));
        static Vector3 Abs(Vector3 a) => new(Mathf.Abs(a.x), Mathf.Abs(a.y), Mathf.Abs(a.z));

        // A hit without a rigidbody (a bullet, a punch). Impulse in N·s, world space.
        public void AddImpulse(Vector3 worldPoint, Vector3 worldImpulse)
        {
            if (!box) return;
            EnsureSize();
            float mass = Mathf.Max(material.Mass, 1e-3f) / x.Length;
            var local = transform.InverseTransformDirection(worldImpulse) / mass;
            Spread(worldPoint, local, v);
            sleeping = false;
        }

        // Adds value * weight to the handles around a world point.
        void Spread(Vector3 worldPoint, Vector3 value, Vector3[] into)
        {
            Span<int> index = stackalloc int[8];
            Span<float> weight = stackalloc float[8];
            var n = box.NormalizedFromLocalPoint(transform.InverseTransformPoint(worldPoint));
            int count = KneadSolver.Trilinear(n, box.Resolution, index, weight);
            for (int i = 0; i < count; i++) into[index[i]] += value * weight[i];
        }

        Vector3 Inward(Vector3 point, Vector3 normal)
        {
            var toCenter = transform.TransformPoint(box.Center) - point;
            return Vector3.Dot(normal, toCenter) < 0f ? -normal : normal;
        }

        void OnCollisionEnter(Collision c)
        {
            int n = c.contactCount;
            if (n == 0) return;
            float j = c.impulse.magnitude / n;
            for (int i = 0; i < n; i++)
            {
                var contact = c.GetContact(i);
                AddImpulse(contact.point, Inward(contact.point, contact.normal) * j);
            }
            OnCollisionStay(c);
        }

        // The support force of whatever rests on the box, kept until it leaves: sleeping bodies stop sending Stay.
        void OnCollisionStay(Collision c)
        {
            int n = c.contactCount;
            if (n == 0) return;
            float f = c.impulse.magnitude / Mathf.Max(Time.fixedDeltaTime, 1e-4f) / n;
            var list = new List<Load>(n); // ponytail: allocates per contact per step, pool it if GC shows up
            for (int i = 0; i < n; i++)
            {
                var contact = c.GetContact(i);
                list.Add(new Load { point = contact.point, force = Inward(contact.point, contact.normal) * f });
            }
            ReplaceLoads(c.collider, list);
        }

        // A force (N, world space) that keeps pushing until the collider leaves (or SetLoad with zero force).
        internal void SetLoad(Collider key, Vector3 worldPoint, Vector3 worldForce) =>
            ReplaceLoads(key, new List<Load> { new() { point = worldPoint, force = worldForce } });

        void ReplaceLoads(Collider key, List<Load> list)
        {
            float before = 0f, after = 0f;
            if (loads.TryGetValue(key, out var old))
                foreach (var l in old) before += l.force.magnitude;
            foreach (var l in list) after += l.force.magnitude;
            loads[key] = list;
            if (Mathf.Abs(after - before) > 0.02f * Mathf.Max(before, 0.1f)) sleeping = false;
        }

        void OnCollisionExit(Collision c)
        {
            if (loads.Remove(c.collider)) sleeping = false;
        }

        void FixedUpdate() => Tick(Time.fixedDeltaTime);

        // One simulation step. Called from FixedUpdate, and by the editor's Drop Test.
        internal void Tick(float dt)
        {
            if (!box) return;
            EnsureSize();
            TrackMotion(dt);
            if (!sleeping) Simulate(dt);
            WriteOutput();
            Snapshot();
        }

        void TrackMotion(float dt)
        {
            var p = transform.TransformPoint(box.Center);
            var vel = (p - lastPosition) / Mathf.Max(dt, 1e-4f);
            if (motionSamples < 2 || vel.magnitude > 100f) // first frames, or a teleport
            {
                motionSamples++;
                lastPosition = p;
                lastVelocity = motionSamples < 2 ? Vector3.zero : vel;
                Array.Clear(force, 0, force.Length);
                return;
            }
            var a = Vector3.ClampMagnitude((vel - lastVelocity) / Mathf.Max(dt, 1e-4f), 200f);
            lastPosition = p;
            lastVelocity = vel;
            var inertial = -transform.InverseTransformDirection(a) * (material.Inertia * Mathf.Max(material.Mass, 1e-3f) / x.Length);
            for (int i = 0; i < force.Length; i++) force[i] = inertial;
            if (material.Inertia > 0f && a.sqrMagnitude > 0.05f) sleeping = false;
        }

        void Simulate(float dt)
        {
            var m = material;
            var r = box.Resolution;
            int n = x.Length;
            float mass = Mathf.Max(m.Mass, 1e-3f) / n;
            float k = m.Stiffness;
            float c = 2f * m.Damping * Mathf.Sqrt(k * mass);
            float kn = m.Spread * k;
            var ws = WorldSize;
            var h = new Vector3(ws.x / (r.x - 1), ws.y / (r.y - 1), ws.z / (r.z - 1));
            float kv = m.Volume * k * (h.x + h.y + h.z) / 3f;

            foreach (var list in loads.Values)
                foreach (var l in list)
                    Spread(l.point, transform.InverseTransformDirection(l.force), force);

            // Dents are the new rest shape: every elastic force below acts on x - dent.
            // Pressure = compression (negative divergence of the displacement): pushes the material sideways.
            if (m.Volume > 0f)
                for (int i = 0; i < n; i++)
                {
                    int ix = i % r.x, iy = i / r.x % r.y, iz = i / (r.x * r.y);
                    float div = 0f;
                    for (int a = 0; a < 3; a++)
                    {
                        int ia = a == 0 ? ix : a == 1 ? iy : iz;
                        int lo = Neighbor(r, i, a, ia, -1), hi = Neighbor(r, i, a, ia, 1);
                        int span = (hi != i ? 1 : 0) + (lo != i ? 1 : 0);
                        if (span > 0) div += (x[hi][a] - dent[hi][a] - x[lo][a] + dent[lo][a]) / (span * h[a]);
                    }
                    pressure[i] = -div;
                }

            float maxV = 0f, limit = m.MaxDeformation * Mathf.Min(ws.x, Mathf.Min(ws.y, ws.z));
            float yieldDistance = m.Yield * Mathf.Min(ws.x, Mathf.Min(ws.y, ws.z));
            for (int i = 0; i < n; i++)
            {
                int ix = i % r.x, iy = i / r.x % r.y, iz = i / (r.x * r.y);
                var f = force[i] - k * (x[i] - dent[i]);
                int neighbors = 0;
                for (int a = 0; a < 3; a++)
                {
                    int ia = a == 0 ? ix : a == 1 ? iy : iz;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        int j = Neighbor(r, i, a, ia, s);
                        if (j == i) continue;
                        f += kn * (x[j] - dent[j] - x[i] + dent[i]);
                        neighbors++;
                        if (m.Volume > 0f) f[a] -= kv * 0.5f * s * (pressure[j] - pressure[i]);
                    }
                }
                // Implicit Euler on the handle's own spring and damper (neighbors counted at their full stiffness):
                // stable for any stiffness at any time step, and stiff materials settle instead of ringing.
                float denominator = mass + dt * c + dt * dt * (k + 2f * kn * neighbors + 4f * m.Volume * k);
                v[i] = (v[i] * mass + f * dt) / denominator;
                x[i] += v[i] * dt;

                // Past the yield, part of the bend becomes a dent.
                var bend = x[i] - dent[i];
                float len = bend.magnitude;
                if (len > yieldDistance && m.Plasticity > 0f)
                    dent[i] = Vector3.ClampMagnitude(dent[i] + bend * ((1f - yieldDistance / len) * m.Plasticity), limit);
                if (x[i].magnitude > limit)
                {
                    x[i] = Vector3.ClampMagnitude(x[i], limit);
                    v[i] -= Vector3.Project(v[i], x[i]) * (Vector3.Dot(v[i], x[i]) > 0f ? 1f : 0f);
                }
                maxV = Mathf.Max(maxV, v[i].sqrMagnitude);
                force[i] = Vector3.zero;
            }

            float still = 1e-3f * Mathf.Min(ws.x, Mathf.Min(ws.y, ws.z));
            if (maxV < still * still)
            {
                sleeping = true;
                Array.Clear(v, 0, n);
            }
        }

        static int Neighbor(Vector3Int r, int i, int axis, int coordinate, int step)
        {
            int c = coordinate + step;
            if (c < 0 || c >= r[axis]) return i;
            return i + step * (axis == 0 ? 1 : axis == 1 ? r.x : r.x * r.y);
        }

        void WriteOutput()
        {
            if (!box || output == null) return;
            if (box.physics != output) box.physics = output;
            var ws = WorldSize;
            for (int i = 0; i < x.Length; i++) output[i] = new Vector3(x[i].x / ws.x, x[i].y / ws.y, x[i].z / ws.z);
        }

        void Snapshot()
        {
            if (!col) return;
            box.ComputeFinal(contactOffsets);
            contactSolver.Set(contactOffsets, box.Resolution, box.Interpolation);
            var local = transform.localToWorldMatrix;
            normalizedFromWorld = box.NormalizedFromLocal * transform.worldToLocalMatrix;
            worldFromNormalized = local * Matrix4x4.Scale(box.SafeSize); // vectors only
            centerWorld = transform.TransformPoint(box.Center);
            contactReady = true;
        }

        // How far the surface moved inward at a world point (for sinking bodies into dents).
        internal float Depth(Vector3 worldPoint, Vector3 normal)
        {
            if (!contactReady) return 0f;
            var n = normalizedFromWorld.MultiplyPoint3x4(worldPoint);
            var offset = worldFromNormalized.MultiplyVector(contactSolver.Offset(n));
            if (Vector3.Dot(normal, centerWorld - worldPoint) < 0f) normal = -normal;
            return Mathf.Max(0f, Vector3.Dot(offset, normal));
        }

        // Physics thread. Reports the contact as farther apart by the dent depth, so the body sinks into the dent.
        static void ModifyContacts(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            for (int p = 0; p < pairs.Length; p++)
            {
                var pair = pairs[p];
                byCollider.TryGetValue(pair.colliderEntityId, out var a);
                byCollider.TryGetValue(pair.otherColliderEntityId, out var b);
                if (a == null && b == null) continue;
                for (int i = 0; i < pair.contactCount; i++)
                {
                    var point = pair.GetPoint(i);
                    var normal = pair.GetNormal(i);
                    float depth = (a != null ? a.Depth(point, normal) : 0f) + (b != null ? b.Depth(point, normal) : 0f);
                    if (depth > 0f) pair.SetSeparation(i, pair.GetSeparation(i) + depth);
                }
            }
        }
    }
}
