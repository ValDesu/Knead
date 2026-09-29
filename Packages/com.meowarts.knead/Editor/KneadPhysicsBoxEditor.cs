using UnityEditor;
using UnityEngine;

namespace Meowarts.Knead.Editor
{
    [CustomEditor(typeof(KneadPhysicsBox)), CanEditMultipleObjects]
    sealed class KneadPhysicsBoxEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var preset = serializedObject.FindProperty("preset");
            var material = serializedObject.FindProperty("material");

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(preset, new GUIContent("Material"));
            if (EditorGUI.EndChangeCheck() && preset.enumValueIndex != (int)KneadMaterialPreset.Custom)
                material.boxedValue = KneadMaterial.FromPreset((KneadMaterialPreset)preset.enumValueIndex);

            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.IndentLevelScope())
            {
                var it = material.Copy();
                var end = material.GetEndProperty();
                it.NextVisible(true);
                while (!SerializedProperty.EqualContents(it, end))
                {
                    EditorGUILayout.PropertyField(it);
                    if (!it.NextVisible(false)) break;
                }
            }
            if (EditorGUI.EndChangeCheck()) preset.enumValueIndex = (int)KneadMaterialPreset.Custom;
            serializedObject.ApplyModifiedProperties();

            var p = (KneadPhysicsBox)target;
            if (!p.GetComponent<Collider>())
                EditorGUILayout.HelpBox("Add a collider so rigidbodies can hit the box.", MessageType.Warning);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Drop Test", "Drop 1 kg from 1 m on top of the box"), GUILayout.Height(24)))
                    foreach (var t in targets) KneadPreview.DropTest((KneadPhysicsBox)t);
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                    if (GUILayout.Button("Reset Dents", GUILayout.Height(24)))
                        foreach (var t in targets) ((KneadPhysicsBox)t).ResetDents();
            }
        }
    }
}
