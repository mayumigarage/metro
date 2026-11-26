using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Editor helpers for setting up Game.unity.</summary>
public static class MetroSceneTools
{
    /// <summary>
    /// Creates the HUD canvas and texts as real scene objects under the
    /// RhythmJudgementUI and assigns them, so they can be laid out in the editor.
    /// </summary>
    [MenuItem("Metro/Build HUD Into Scene")]
    private static void BuildHudIntoScene()
    {
        RhythmJudgementUI ui = Object.FindFirstObjectByType<RhythmJudgementUI>();
        if (ui == null)
        {
            Debug.LogWarning("Metro: no RhythmJudgementUI in the open scene.");
            return;
        }

        if (ui.HasHud)
        {
            Debug.Log("Metro: the HUD is already in the scene.", ui);
            return;
        }

        Undo.RecordObject(ui, "Build HUD Into Scene");
        GameObject canvas = ui.BuildHud();
        Undo.RegisterCreatedObjectUndo(canvas, "Build HUD Into Scene");
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        Selection.activeGameObject = canvas;
        Debug.Log("Metro: HUD created in the scene. Save the scene to keep it.", canvas);
    }
}
