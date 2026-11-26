using UnityEngine;

/// <summary>
/// The four difficulty grades of a chart, named after train services: Local (green),
/// Rapid (yellow), Express (red) and Limited Express (purple). A chart picks its grade
/// with "difficulty" (0-3, omitted = Local); the song select, the selected-song sign
/// and the train doors take their line colour from it.
/// </summary>
public sealed class SongDifficulty
{
    public static readonly SongDifficulty[] All =
    {
        new SongDifficulty(0, "Local", "各停", new Color32(0x00, 0xA6, 0x5A, 0xFF)),
        new SongDifficulty(1, "Rapid", "快速", new Color32(0xF5, 0xB4, 0x00, 0xFF)),
        new SongDifficulty(2, "Express", "急行", new Color32(0xE6, 0x00, 0x12, 0xFF)),
        new SongDifficulty(3, "Limited Express", "特急", new Color32(0x8E, 0x3F, 0xD1, 0xFF))
    };

    /// <summary>The chart's "difficulty" value (position in <see cref="All"/>).</summary>
    public readonly int Index;

    public readonly string Name;

    /// <summary>The train service's Japanese name, as a car's door display prints it (各停, 快速, 急行, 特急).</summary>
    public readonly string JapaneseName;
    public readonly Color Color;

    private SongDifficulty(int index, string name, string japaneseName, Color color)
    {
        Index = index;
        Name = name;
        JapaneseName = japaneseName;
        Color = color;
    }

    /// <summary>The grade for a chart's "difficulty" value; out-of-range values are clamped.</summary>
    public static SongDifficulty Get(int index)
    {
        return All[Mathf.Clamp(index, 0, All.Length - 1)];
    }
}
