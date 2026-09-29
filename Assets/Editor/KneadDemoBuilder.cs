using Meowarts.Knead;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds Assets/Scenes/Knead.unity: a row of props to tween, and one physics box per material preset.
static class KneadDemoBuilder
{
    [MenuItem("Knead/Build Demo Scene")]
    public static void Build()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
        var cam = Camera.main.transform;
        cam.position = new Vector3(0, 4.5f, -10f);
        cam.rotation = Quaternion.Euler(18f, 0, 0);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(3, 1, 2);

        var shapes = new[] { PrimitiveType.Capsule, PrimitiveType.Cylinder, PrimitiveType.Sphere, PrimitiveType.Cube };
        var props = new KneadBox[shapes.Length];
        for (int i = 0; i < shapes.Length; i++)
        {
            var go = GameObject.CreatePrimitive(shapes[i]);
            go.name = shapes[i].ToString();
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = new Vector3(-4.5f + i * 3f, shapes[i] == PrimitiveType.Sphere || shapes[i] == PrimitiveType.Cube ? 0.5f : 1f, -2f);
            props[i] = go.AddComponent<KneadBox>();
        }

        // Poses on the capsule: tall and thin, and leaning to the side.
        var posed = props[0];
        var r = posed.Resolution;
        for (int i = 0; i < posed.HandleCount; i++)
        {
            var rest = KneadSolver.Rest(r, i);
            posed.SetHandleOffset(i, new Vector3((0.5f - rest.x) * 0.3f * rest.y, 0.35f * rest.y, (0.5f - rest.z) * 0.3f * rest.y));
        }
        posed.SavePose("Tall");
        for (int i = 0; i < posed.HandleCount; i++)
        {
            var rest = KneadSolver.Rest(r, i);
            posed.SetHandleOffset(i, new Vector3(0.6f * rest.y * rest.y, -0.1f * rest.y, 0));
        }
        posed.SavePose("Lean");
        posed.ResetHandles();

        var presets = new[]
        {
            KneadMaterialPreset.Jelly, KneadMaterialPreset.Rubber, KneadMaterialPreset.Pillow, KneadMaterialPreset.Cardboard,
            KneadMaterialPreset.Clay, KneadMaterialPreset.Metal, KneadMaterialPreset.Rock,
        };
        for (int i = 0; i < presets.Length; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = presets[i].ToString();
            go.transform.position = new Vector3(-6f + i * 2f, 0.5f, 2f);
            var p = go.AddComponent<KneadPhysicsBox>();
            p.Preset = presets[i];
            go.GetComponent<KneadBox>().Resolution = new Vector3Int(4, 4, 4);

            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition = new Vector3(0, -0.2f, -0.51f);
            label.text = presets[i].ToString();
            label.characterSize = 0.06f;
            label.fontSize = 48;
            label.anchor = TextAnchor.MiddleCenter;
        }

        var demo = new GameObject("Knead Demo").AddComponent<KneadDemo>();
        demo.props = props;
        demo.posed = posed;

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Scenes/Knead.unity");
    }

    // Player build of the demo, to check the runtime compiles without the editor.
    public static void BuildPlayer()
    {
        Build();
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Knead.unity" }, "Build/Knead.app", BuildTarget.StandaloneOSX, BuildOptions.None);
        Debug.Log($"KNEAD BUILD {report.summary.result} {report.summary.totalErrors} errors");
    }
}
