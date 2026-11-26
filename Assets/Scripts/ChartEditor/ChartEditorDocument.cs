using UnityEngine;

/// <summary>
/// The chart being edited in <see cref="ChartEditorWindow"/>. It is a ScriptableObject
/// only so that Unity's Undo can record it and so that it survives domain reloads; it is
/// never saved as an asset (the chart is written to its JSON file instead).
/// Kept in a file of its own name: Unity needs that to restore it after a reload.
/// </summary>
public sealed class ChartEditorDocument : ScriptableObject
{
    public SongChartData chart = new SongChartData();
}
