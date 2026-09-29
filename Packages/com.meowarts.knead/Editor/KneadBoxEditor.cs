using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meowarts.Knead.Editor
{
    [CustomEditor(typeof(KneadBox)), CanEditMultipleObjects]
    sealed class KneadBoxEditor : UnityEditor.Editor
    {
        static readonly GUIContent[] ModeLabels = { new("Move (W)"), new("Rotate (E)"), new("Scale (R)") };
        string newPoseName = "Pose 1";
        KneadEffect effect;
        string renaming;
        string renameText;

        KneadBox Box => (KneadBox)target;

        public override void OnInspectorGUI()
        {
            var box = Box;
            serializedObject.Update();

            if (targets.Length > 1)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("interpolation"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("axis"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("bake"));
                serializedObject.ApplyModifiedProperties();
                return;
            }

            // Editing
            bool editing = KneadBoxTool.IsEditing(box);
            using (new EditorGUILayout.HorizontalScope())
            {
                var content = EditorGUIUtility.TrTextContentWithIcon(editing ? "Stop Editing" : "Edit Handles", "EditCollider");
                if (GUILayout.Toggle(editing, content, "Button", GUILayout.Height(24)) != editing) KneadBoxTool.Toggle();
                if (GUILayout.Button("Fit to Renderers", GUILayout.Height(24)))
                {
                    Undo.RecordObject(box, "Fit Knead Box");
                    box.FitToRenderers();
                    box.Apply();
                }
                if (GUILayout.Button("Reset", GUILayout.Height(24)))
                {
                    Undo.RecordObject(box, "Reset Knead Handles");
                    box.ResetHandles();
                    box.Apply();
                }
            }
            if (editing) DrawSelection(box);

            // Box
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("center"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("size"));
            EditorGUI.BeginChangeCheck();
            var res = EditorGUILayout.Vector3IntField("Resolution", box.Resolution);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(box, "Change Knead Resolution");
                box.Resolution = res;
                box.Apply();
                serializedObject.Update();
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("interpolation"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("axis"));

            // Targets
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targets"));
            if (GUILayout.Button("Find Renderers"))
            {
                Undo.RecordObject(box, "Find Knead Renderers");
                box.FindRenderers();
                serializedObject.Update();
            }
            foreach (var r in box.Targets)
            {
                string problem = box.Problem(r);
                if (problem == null) continue;
                var mesh = r ? (r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh) : null;
                var importer = mesh && !mesh.isReadable ? AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) as ModelImporter : null;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox(problem, MessageType.Warning);
                    if (importer && GUILayout.Button("Fix", GUILayout.Width(50), GUILayout.Height(38)))
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                        box.Reattach();
                    }
                }
            }
            if (box.Bake || GameObjectUtility.AreStaticEditorFlagsSet(box.gameObject, StaticEditorFlags.BatchingStatic))
                EditorGUILayout.HelpBox("Baked at build time: the deformed mesh is saved in the build and the box is removed, " +
                                        "so tweens and physics don't run in builds.", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bake"));
            serializedObject.ApplyModifiedProperties();

            DrawPoses(box);
            DrawTryIt(box);
        }

        static void DrawSelection(KneadBox box)
        {
            int mode = GUILayout.Toolbar((int)KneadBoxTool.mode, ModeLabels);
            if (mode != (int)KneadBoxTool.mode) KneadBoxTool.SetMode((KneadBoxTool.Mode)mode);
            var sel = KneadBoxTool.Selection;
            if (sel.Count == 0)
            {
                EditorGUILayout.HelpBox("Click handles in the Scene view (Shift to add, drag a rectangle, Ctrl/Cmd+A for all). " +
                                        "Right-click for Mirror, Reset Selected, Expand Selection.", MessageType.None);
                return;
            }
            if (sel.Count > 1)
            {
                EditorGUILayout.LabelField($"{sel.Count} handles selected");
                return;
            }
            int i = sel.First();
            var r = box.Resolution;
            var c = new Vector3Int(i % r.x, i / r.x % r.y, i / (r.x * r.y));
            EditorGUI.BeginChangeCheck();
            var local = box.transform.InverseTransformPoint(box.GetHandlePosition(i));
            local = EditorGUILayout.Vector3Field($"Handle {i} ({c.x}, {c.y}, {c.z})", local);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(box, "Edit Knead Handle");
                box.SetHandlePosition(i, box.transform.TransformPoint(local));
                box.Apply();
            }
        }

        void DrawPoses(KneadBox box)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Poses", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                newPoseName = EditorGUILayout.TextField(newPoseName);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newPoseName)))
                    if (GUILayout.Button(box.FindPose(newPoseName) != null ? "Overwrite Pose" : "Save Pose", GUILayout.Width(110)))
                    {
                        Undo.RecordObject(box, "Save Knead Pose");
                        box.SavePose(newPoseName.Trim());
                        newPoseName = NextName(box);
                    }
            }
            if (box.Poses.Count == 0)
                EditorGUILayout.HelpBox("Shape the box with Edit Handles, then Save Pose. In code: box.TweenToPose(\"name\").",
                    MessageType.None);

            foreach (var pose in box.Poses.ToList())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (renaming == pose.Name)
                    {
                        GUI.SetNextControlName("KneadRename");
                        renameText = EditorGUILayout.TextField(renameText);
                        bool enter = Event.current.type == EventType.KeyUp && Event.current.keyCode is KeyCode.Return or KeyCode.KeypadEnter;
                        if (GUILayout.Button("OK", GUILayout.Width(40)) || enter)
                        {
                            Undo.RecordObject(box, "Rename Knead Pose");
                            if (!box.RenamePose(pose.Name, renameText.Trim())) Debug.LogWarning($"Knead: can't rename to \"{renameText}\" (empty or taken).");
                            renaming = null;
                            GUI.FocusControl(null);
                        }
                        continue;
                    }
                    EditorGUILayout.LabelField(pose.Name);
                    if (GUILayout.Button(EditorGUIUtility.TrIconContent("PlayButton", "Tween to this pose and back"), GUILayout.Width(28)))
                        KneadPreview.PlayPose(box, pose.Name);
                    if (GUILayout.Button("Apply", GUILayout.Width(50)))
                    {
                        Undo.RecordObject(box, "Apply Knead Pose");
                        box.SetPose(pose.Name);
                        box.Apply();
                    }
                    if (GUILayout.Button(new GUIContent("Overwrite", "Replace this pose with the current handles"), GUILayout.Width(70)))
                    {
                        Undo.RecordObject(box, "Overwrite Knead Pose");
                        box.SavePose(pose.Name);
                    }
                    if (GUILayout.Button(new GUIContent("Rename"), GUILayout.Width(60)))
                    {
                        renaming = pose.Name;
                        renameText = pose.Name;
                    }
                    if (GUILayout.Button(EditorGUIUtility.TrIconContent("TreeEditor.Trash", "Delete pose"), GUILayout.Width(28)))
                    {
                        Undo.RecordObject(box, "Delete Knead Pose");
                        box.DeletePose(pose.Name);
                    }
                }
            }
        }

        static string NextName(KneadBox box)
        {
            for (int i = box.Poses.Count + 1; ; i++)
                if (box.FindPose($"Pose {i}") == null) return $"Pose {i}";
        }

        void DrawTryIt(KneadBox box)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Try It", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                effect = (KneadEffect)EditorGUILayout.EnumPopup(effect);
                if (GUILayout.Button(EditorGUIUtility.TrTextContentWithIcon("Bounce", "PlayButton"), GUILayout.Width(90)))
                    KneadPreview.Bounce(box, KneadTween.Preset(effect));
            }
            EditorGUILayout.HelpBox($"box.Bounce(KneadTween.{effect});", MessageType.None);
        }

        [MenuItem("CONTEXT/KneadBox/Save Deformed Mesh...")]
        static void SaveDeformedMesh(MenuCommand command)
        {
            var box = (KneadBox)command.context;
            foreach (var r in box.Targets)
            {
                if (!r) continue;
                var mesh = box.BakeMesh(r);
                if (!mesh) continue;
                string path = EditorUtility.SaveFilePanelInProject("Save Deformed Mesh", mesh.name.Replace(" (Knead Baked)", " Deformed"),
                    "asset", $"Save the deformed mesh of {r.name}");
                if (string.IsNullOrEmpty(path)) { DestroyImmediate(mesh); continue; }
                mesh.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(mesh, path);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("GameObject/Effects/Knead Box", false, 10)]
        static void CreateBox(MenuCommand command) => Add<KneadBox>(command, "Knead Box");

        [MenuItem("GameObject/Effects/Knead Physics Box", false, 11)]
        static void CreatePhysicsBox(MenuCommand command) => Add<KneadPhysicsBox>(command, "Knead Physics Box");

        // Adds the component to the object right-clicked (or the selection), or a new cube when nothing is selected.
        static void Add<T>(MenuCommand command, string label) where T : Component
        {
            // From the Hierarchy context menu this runs once per selected object, with that object as context.
            var objects = command.context is GameObject go ? new[] { go } : UnityEditor.Selection.gameObjects;
            if (objects.Length == 0)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = label;
                Undo.RegisterCreatedObjectUndo(cube, $"Create {label}");
                objects = new[] { cube };
            }
            foreach (var o in objects)
                if (!o.GetComponent<T>()) Undo.AddComponent<T>(o);
            UnityEditor.Selection.objects = objects;
        }
    }

    // Plays tweens and physics in edit mode, without entering play mode. Leaves no trace: poses tween back.
    [InitializeOnLoad]
    static class KneadPreview
    {
        static readonly HashSet<KneadBox> boxes = new();
        static readonly HashSet<KneadPhysicsBox> physics = new();
        static readonly Dictionary<KneadPhysicsBox, float> physicsTime = new();
        static double last;
        static float accumulator;

        static KneadPreview()
        {
            EditorApplication.update += Update;
            Undo.undoRedoPerformed += () =>
            {
                foreach (var b in KneadBox.Active) b.Apply();
            };
        }

        internal static void Bounce(KneadBox box, KneadTweenParams p)
        {
            if (Application.isPlaying) { box.Bounce(p); return; }
            box.Bounce(p);
            Start(box);
        }

        internal static void PlayPose(KneadBox box, string pose)
        {
            if (Application.isPlaying) { box.TweenToPose(pose); return; }
            var before = (Vector3[])box.RawHandles.Clone();
            box.TweenToPose(pose, 0.35f).OnComplete(() =>
            {
                EditorApplication.delayCall += () =>
                {
                    if (!box) return;
                    // Hold, then go back to where the handles were.
                    double until = EditorApplication.timeSinceStartup + 0.5;
                    void Wait()
                    {
                        if (!box) { EditorApplication.update -= Wait; return; }
                        if (EditorApplication.timeSinceStartup < until) return;
                        EditorApplication.update -= Wait;
                        box.TweenHandles(before, 0.35f, KneadEase.OutBack, false);
                        Start(box);
                    }
                    EditorApplication.update += Wait;
                };
            });
            Start(box);
        }

        internal static void DropTest(KneadPhysicsBox p)
        {
            var box = p.GetComponent<KneadBox>();
            // 1 kg dropped from 1 m onto the top face (along the box axis).
            var up = Vector3.zero;
            up[(int)box.Axis] = 1f;
            var top = box.transform.TransformPoint(box.Center + Vector3.Scale(up, box.Size) * 0.5f);
            var dir = box.transform.TransformDirection(-up).normalized;
            p.AddImpulse(top, dir * Mathf.Sqrt(2f * 9.81f));
            if (Application.isPlaying) return;
            physics.Add(p);
            physicsTime[p] = 0f;
            Start(box);
        }

        static void Start(KneadBox box)
        {
            if (boxes.Count == 0 && physics.Count == 0) last = EditorApplication.timeSinceStartup;
            boxes.Add(box);
        }

        static void Update()
        {
            if (boxes.Count == 0) return;
            if (Application.isPlaying) { boxes.Clear(); physics.Clear(); return; }
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Min((float)(now - last), 0.1f);
            last = now;

            accumulator += dt;
            float fixedDt = Time.fixedDeltaTime;
            while (accumulator >= fixedDt)
            {
                accumulator -= fixedDt;
                foreach (var p in physics.ToList())
                {
                    if (!p) { physics.Remove(p); continue; }
                    p.Tick(fixedDt);
                    physicsTime[p] += fixedDt;
                    if (physicsTime[p] > 0.5f && p.IsSleeping || physicsTime[p] > 6f)
                    {
                        p.ResetState();
                        physics.Remove(p);
                    }
                }
            }

            foreach (var b in boxes.ToList())
            {
                if (!b) { boxes.Remove(b); continue; }
                b.Step(dt, dt);
                b.Apply();
                bool simulating = physics.Any(p => p && p.gameObject == b.gameObject);
                if (!b.IsTweening && !simulating) boxes.Remove(b);
            }
            SceneView.RepaintAll();
        }
    }

    // Puts the original meshes back while a scene or prefab is saved, so a temporary mesh is never saved.
    [InitializeOnLoad]
    static class KneadSaveHooks
    {
        static KneadSaveHooks()
        {
            EditorSceneManager.sceneSaving += (scene, _) => ForScene(scene, b => b.Detach());
            EditorSceneManager.sceneSaved += scene => ForScene(scene, b => b.Reattach());
            PrefabStage.prefabSaving += root => ForRoot(root, b => b.Detach());
            PrefabStage.prefabSaved += root => ForRoot(root, b => b.Reattach());
        }

        static void ForScene(Scene scene, System.Action<KneadBox> action)
        {
            foreach (var b in KneadBox.Active.ToList())
                if (b && b.gameObject.scene == scene) action(b);
        }

        static void ForRoot(GameObject root, System.Action<KneadBox> action)
        {
            foreach (var b in KneadBox.Active.ToList())
                if (b && b.transform.IsChildOf(root.transform)) action(b);
        }
    }

    // Static bake: in builds, boxes marked Bake (or static-batched) are replaced by their deformed meshes.
    sealed class KneadBakeProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return; // entering play mode: keep the boxes live
            foreach (var root in scene.GetRootGameObjects())
            foreach (var box in root.GetComponentsInChildren<KneadBox>(true))
            {
                bool isStatic = GameObjectUtility.AreStaticEditorFlagsSet(box.gameObject, StaticEditorFlags.BatchingStatic);
                if (!box.Bake && !isStatic) continue;
                if (box.GetComponent<KneadPhysicsBox>())
                {
                    Debug.LogWarning($"Knead: {box.name} has a Knead Physics Box, so it isn't baked.", box);
                    continue;
                }
                Bake(box);
            }
        }

        internal static void Bake(KneadBox box)
        {
            var baked = new List<(Renderer, Mesh)>();
            foreach (var r in box.Targets)
                if (r && box.BakeMesh(r) is { } mesh) baked.Add((r, mesh));
            Object.DestroyImmediate(box); // puts the source meshes back first
            foreach (var (r, mesh) in baked)
            {
                if (r is SkinnedMeshRenderer s) s.sharedMesh = mesh;
                else r.GetComponent<MeshFilter>().sharedMesh = mesh;
            }
        }
    }
}
