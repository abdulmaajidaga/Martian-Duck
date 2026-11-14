using UnityEditor;
using UnityEngine;

/// <summary>Adds a "Bake Terrain To Scene" button below the MarsTerrainGenerator settings.</summary>
[CustomEditor(typeof(MarsTerrainGenerator))]
[CanEditMultipleObjects]
public class MarsTerrainGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Bake Terrain To Scene", GUILayout.Height(28)))
            {
                foreach (Object t in targets)
                    ((MarsTerrainGenerator)t).BakeTerrainToScene();
            }
        }

        EditorGUILayout.HelpBox(
            Application.isPlaying
                ? "Exit Play Mode to bake."
                : "Builds the terrain and player in the scene and saves the terrain data to Assets/MarsData/Baked/. " +
                  "Re-bake after changing the heightmap or settings, then save the scene.",
            MessageType.None);
    }
}
