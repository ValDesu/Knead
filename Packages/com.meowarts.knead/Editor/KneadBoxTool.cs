using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meowarts.Knead.Editor
{
    // Scene view tool for editing the handles of a Knead Box: click, shift-click, drag a rectangle, then move,
    // rotate or scale the selection with Unity's handles. W / E / R switch while the tool is active.
    [EditorTool("Edit Knead Box", typeof(KneadBox))]
    sealed class KneadBoxTool : EditorTool
    {
        internal enum Mode { Move, Rotate, Scale }

        internal static readonly HashSet<int> Selection = new();
        internal static Mode mode;
        static KneadBox selectedBox;
        static Vector3Int selectedResolution;

        static readonly Color CageColor = new(1f, 0.55f, 0.2f, 0.9f);
        static readonly Color RestColor = new(1f, 1f, 1f, 0.12f);
        static readonly Color SelectedColor = new(1f, 0.9f, 0.2f, 1f);

        Vector2 rectStart;
        bool rectSelecting;
        Quaternion rotation = Quaternion.identity;
        Vector3 scale = Vector3.one;
        readonly Context context = new();

        sealed class Context : IShortcutContext
        {
            public bool active => ToolManager.IsActiveTool(Instance);
        }

        static KneadBoxTool Instance;

        public override GUIContent toolbarIcon => new(EditorGUIUtility.IconContent("EditCollider").image, "Edit Knead Box handles");

        public override void OnActivated()
        {
            Instance = this;
            ShortcutManager.RegisterContext(context);
        }

        public override void OnWillBeDeactivated() => ShortcutManager.UnregisterContext(context);

        [Shortcut("Knead Box/Move Handles", typeof(Context), KeyCode.W)] static void MoveMode() => SetMode(Mode.Move);
        [Shortcut("Knead Box/Rotate Handles", typeof(Context), KeyCode.E)] static void RotateMode() => SetMode(Mode.Rotate);
        [Shortcut("Knead Box/Scale Handles", typeof(Context), KeyCode.R)] static void ScaleMode() => SetMode(Mode.Scale);

        [Shortcut("Knead Box/Select All Handles", typeof(Context), KeyCode.A, ShortcutModifiers.Action)]
        static void SelectAllShortcut()
        {
            if (selectedBox) SelectAll(selectedBox);
        }

        internal static void SetMode(Mode m)
        {
            mode = m;
            SceneView.RepaintAll();
        }

        internal static bool IsEditing(KneadBox box) => Instance && ToolManager.IsActiveTool(Instance) && selectedBox == box;

        internal static void Toggle()
        {
            if (Instance && ToolManager.IsActiveTool(Instance)) ToolManager.RestorePreviousPersistentTool();
            else ToolManager.SetActiveTool<KneadBoxTool>();
        }

        static void Track(KneadBox box)
        {
            if (box == selectedBox && box.Resolution == selectedResolution) return;
            Selection.Clear();
            selectedBox = box;
            selectedResolution = box.Resolution;
        }

        internal static void SelectAll(KneadBox box)
        {
            Track(box);
            for (int i = 0; i < box.HandleCount; i++) Selection.Add(i);
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (target is not KneadBox box || !box.isActiveAndEnabled) return;
            Track(box);
            var e = Event.current;
            int n = box.HandleCount;
            var r = box.Resolution;
            var world = new Vector3[n];
            for (int i = 0; i < n; i++) world[i] = box.GetHandlePosition(i);

            if (e.type == EventType.Repaint) DrawCage(box, world, r);

            // Picking: one control per handle, a default control for rectangle selection.
            int baseId = GUIUtility.GetControlID(FocusType.Passive);
            int rectId = GUIUtility.GetControlID(FocusType.Passive);
            var ids = new int[n];
            for (int i = 0; i < n; i++) ids[i] = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(rectId);
                for (int i = 0; i < n; i++)
                {
                    float size = HandleUtility.GetHandleSize(world[i]) * 0.06f;
                    HandleUtility.AddControl(ids[i], HandleUtility.DistanceToCircle(world[i], size));
                }
            }

            if (Selection.Count > 0) TransformSelection(box, world);

            switch (e.GetTypeForControl(rectId))
            {
                case EventType.MouseDown when e.button == 0 && !e.alt:
                {
                    int picked = System.Array.IndexOf(ids, HandleUtility.nearestControl);
                    if (picked >= 0)
                    {
                        if (e.shift || EditorGUI.actionKey)
                        {
                            if (!Selection.Remove(picked)) Selection.Add(picked);
                        }
                        else if (!Selection.Contains(picked))
                        {
                            Selection.Clear();
                            Selection.Add(picked);
                        }
                        e.Use();
                    }
                    else if (HandleUtility.nearestControl == rectId)
                    {
                        rectStart = e.mousePosition;
                        rectSelecting = true;
                        GUIUtility.hotControl = rectId;
                        e.Use();
                    }
                    break;
                }
                case EventType.MouseDrag when GUIUtility.hotControl == rectId:
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == rectId:
                {
                    GUIUtility.hotControl = 0;
                    rectSelecting = false;
                    var rect = Rect(rectStart, e.mousePosition);
                    if (!e.shift && !EditorGUI.actionKey) Selection.Clear();
                    if (rect.width < 3f && rect.height < 3f) { e.Use(); break; }
                    for (int i = 0; i < n; i++)
                        if (rect.Contains(HandleUtility.WorldToGUIPoint(world[i]))) Selection.Add(i);
                    e.Use();
                    break;
                }
                case EventType.Repaint when rectSelecting:
                    Handles.BeginGUI();
                    EditorStyles.selectionRect.Draw(Rect(rectStart, e.mousePosition), GUIContent.none, false, false, false, false);
                    Handles.EndGUI();
                    break;
            }
            if (e.type == EventType.MouseDrag && rectSelecting) window.Repaint();
        }

        static Rect Rect(Vector2 a, Vector2 b) =>
            new(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        static void DrawCage(KneadBox box, Vector3[] world, Vector3Int r)
        {
            var rest = new Vector3[world.Length];
            for (int i = 0; i < world.Length; i++) rest[i] = box.GetHandleRestPosition(i);
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Lines(rest, r, RestColor, 1f);
            Lines(world, r, CageColor, 2f);
            for (int i = 0; i < world.Length; i++)
            {
                float size = HandleUtility.GetHandleSize(world[i]) * 0.06f;
                Handles.color = Selection.Contains(i) ? SelectedColor : CageColor;
                Handles.DotHandleCap(0, world[i], Quaternion.identity, size, EventType.Repaint);
            }
        }

        static void Lines(Vector3[] p, Vector3Int r, Color color, float width)
        {
            Handles.color = color;
            for (int z = 0; z < r.z; z++)
            for (int y = 0; y < r.y; y++)
            for (int x = 0; x < r.x; x++)
            {
                int i = KneadSolver.Index(r, x, y, z);
                if (x + 1 < r.x) Handles.DrawLine(p[i], p[KneadSolver.Index(r, x + 1, y, z)], width);
                if (y + 1 < r.y) Handles.DrawLine(p[i], p[KneadSolver.Index(r, x, y + 1, z)], width);
                if (z + 1 < r.z) Handles.DrawLine(p[i], p[KneadSolver.Index(r, x, y, z + 1)], width);
            }
        }

        void TransformSelection(KneadBox box, Vector3[] world)
        {
            var pivot = Vector3.zero;
            foreach (int i in Selection) pivot += world[i];
            pivot /= Selection.Count;
            var frame = Tools.pivotRotation == PivotRotation.Local ? box.transform.rotation : Quaternion.identity;
            if (GUIUtility.hotControl == 0)
            {
                rotation = frame;
                scale = Vector3.one;
            }

            EditorGUI.BeginChangeCheck();
            var moved = new Dictionary<int, Vector3>();
            switch (mode)
            {
                case Mode.Move:
                {
                    var p = Handles.PositionHandle(pivot, frame);
                    if (EditorGUI.EndChangeCheck())
                        foreach (int i in Selection) moved[i] = world[i] + (p - pivot);
                    break;
                }
                case Mode.Rotate:
                {
                    var q = Handles.RotationHandle(rotation, pivot);
                    if (EditorGUI.EndChangeCheck())
                    {
                        var delta = q * Quaternion.Inverse(rotation);
                        rotation = q;
                        foreach (int i in Selection) moved[i] = pivot + delta * (world[i] - pivot);
                    }
                    break;
                }
                default:
                {
                    var s = Handles.ScaleHandle(scale, pivot, frame, HandleUtility.GetHandleSize(pivot));
                    if (EditorGUI.EndChangeCheck())
                    {
                        var ratio = new Vector3(Ratio(s.x, scale.x), Ratio(s.y, scale.y), Ratio(s.z, scale.z));
                        scale = s;
                        var inverse = Quaternion.Inverse(frame);
                        foreach (int i in Selection)
                            moved[i] = pivot + frame * Vector3.Scale(inverse * (world[i] - pivot), ratio);
                    }
                    break;
                }
            }
            if (moved.Count == 0) return;
            Undo.RecordObject(box, "Edit Knead Handles");
            foreach (var kv in moved) box.SetHandlePosition(kv.Key, kv.Value);
            box.Apply();
        }

        static float Ratio(float now, float before) => Mathf.Abs(before) < 1e-5f ? 1f : now / before;

        // Right-click in the Scene view while the tool is active.
        public override void PopulateMenu(DropdownMenu menu)
        {
            if (target is not KneadBox box) return;
            Track(box);
            bool any = Selection.Count > 0;
            menu.AppendAction("Select All", _ => SelectAll(box));
            menu.AppendAction("Select None", _ => { Selection.Clear(); SceneView.RepaintAll(); },
                any ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendAction("Expand Selection", _ => Grow(box, true), Status(any));
            menu.AppendAction("Shrink Selection", _ => Grow(box, false), Status(any));
            menu.AppendSeparator();
            menu.AppendAction("Reset Selected", _ => ResetSelected(box), Status(any));
            menu.AppendAction("Mirror/X", _ => Mirror(box, 0), Status(any));
            menu.AppendAction("Mirror/Y", _ => Mirror(box, 1), Status(any));
            menu.AppendAction("Mirror/Z", _ => Mirror(box, 2), Status(any));
        }

        static DropdownMenuAction.Status Status(bool enabled) =>
            enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;

        static Vector3Int Coord(Vector3Int r, int i) => new(i % r.x, i / r.x % r.y, i / (r.x * r.y));

        internal static void Grow(KneadBox box, bool expand)
        {
            var r = box.Resolution;
            var result = new HashSet<int>(Selection);
            for (int i = 0; i < box.HandleCount; i++)
            {
                var c = Coord(r, i);
                bool touching = false, allIn = true;
                for (int a = 0; a < 3; a++)
                for (int s = -1; s <= 1; s += 2)
                {
                    var d = c;
                    d[a] += s;
                    if (d[a] < 0 || d[a] >= r[a]) continue;
                    bool inside = Selection.Contains(KneadSolver.Index(r, d.x, d.y, d.z));
                    touching |= inside;
                    allIn &= inside;
                }
                if (expand && touching) result.Add(i);
                if (!expand && !allIn) result.Remove(i);
            }
            Selection.Clear();
            Selection.UnionWith(result);
            SceneView.RepaintAll();
        }

        internal static void ResetSelected(KneadBox box)
        {
            Undo.RecordObject(box, "Reset Knead Handles");
            foreach (int i in Selection) box.SetHandleOffset(i, Vector3.zero);
            box.Apply();
        }

        // Copies the selected handles to the other side of the axis. When both sides are selected, + wins.
        internal static void Mirror(KneadBox box, int axis)
        {
            var r = box.Resolution;
            Undo.RecordObject(box, "Mirror Knead Handles");
            float mid = (r[axis] - 1) * 0.5f;
            foreach (int i in Selection)
            {
                var c = Coord(r, i);
                var m = c;
                m[axis] = r[axis] - 1 - c[axis];
                int j = KneadSolver.Index(r, m.x, m.y, m.z);
                var o = box.GetHandleOffset(i);
                if (j == i)
                {
                    o[axis] = 0f;
                    box.SetHandleOffset(i, o);
                    continue;
                }
                if (Selection.Contains(j) && c[axis] < mid) continue;
                o[axis] = -o[axis];
                box.SetHandleOffset(j, o);
            }
            box.Apply();
        }
    }
}
