using System;
using UnityEngine;

/// <summary>
/// The genre folders of the song select, drawn as subway lines: each genre has a line
/// letter and colour, and its songs are the stations of that line. A chart picks its
/// folder with "genre" (the id or the letter); a chart without one is an original.
/// </summary>
public sealed class SongGenre
{
    public static readonly SongGenre[] All =
    {
        new SongGenre(0, "pops", "P", "ポップス", "Pops", new Color32(0xF0, 0x6E, 0xAA, 0xFF)),
        new SongGenre(1, "vocaloid", "V", "ボカロ", "Vocaloid", new Color32(0x00, 0xA9, 0xA5, 0xFF)),
        new SongGenre(2, "game", "G", "ゲーム", "Game", new Color32(0x8B, 0x6F, 0xD0, 0xFF)),
        new SongGenre(3, "original", "M", "オリジナル", "Original", new Color32(0xE6, 0x00, 0x12, 0xFF))
    };

    /// <summary>Position in <see cref="All"/>.</summary>
    public readonly int Index;

    /// <summary>The chart's "genre" value.</summary>
    public readonly string Id;

    /// <summary>Line letter on the badges, e.g. P for P01, P02...</summary>
    public readonly string Letter;

    public readonly string Name;
    public readonly string EnglishName;
    public readonly Color Color;

    private SongGenre(int index, string id, string letter, string name, string englishName, Color color)
    {
        Index = index;
        Id = id;
        Letter = letter;
        Name = name;
        EnglishName = englishName;
        Color = color;
    }

    /// <summary>Where charts without a (known) genre go.</summary>
    public static SongGenre Default => All[All.Length - 1];

    /// <summary>The genre a chart's "genre" value names (its id or its letter, any case); Default otherwise.</summary>
    public static SongGenre Find(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Default;
        }

        string trimmed = key.Trim();
        foreach (SongGenre genre in All)
        {
            if (string.Equals(genre.Id, trimmed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(genre.Letter, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return genre;
            }
        }

        return Default;
    }

    /// <summary>Station number of a song on its line: "01", "02"...</summary>
    public static string FormatStation(int number)
    {
        return Mathf.Clamp(number, 0, 99).ToString("00");
    }
}
