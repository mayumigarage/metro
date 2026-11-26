using System.Globalization;
using System.Text;

/// <summary>
/// Writes a chart in the same compact layout as the hand-written charts: one note per
/// line, fields left out when they hold their default value. JsonUtility reads it back
/// unchanged (it ignores what is missing), so this is only about keeping the files tidy
/// and diffable.
/// </summary>
public static class ChartJsonWriter
{
    public static string Write(SongChartData chart)
    {
        StringBuilder json = new StringBuilder();
        json.Append("{\n");
        json.Append("  \"title\": ").Append(Quote(chart.title)).Append(",\n");
        if (!string.IsNullOrEmpty(chart.artist))
        {
            json.Append("  \"artist\": ").Append(Quote(chart.artist)).Append(",\n");
        }

        json.Append("  \"bpm\": ").Append(Number(chart.bpm)).Append(",\n");
        if (chart.offset != 0d)
        {
            json.Append("  \"offset\": ").Append(Time(chart.offset)).Append(",\n");
        }

        json.Append("  \"level\": ").Append(chart.level.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        if (chart.difficulty != 0)
        {
            json.Append("  \"difficulty\": ").Append(chart.difficulty.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        }

        if (!string.IsNullOrEmpty(chart.audio))
        {
            json.Append("  \"audio\": ").Append(Quote(chart.audio)).Append(",\n");
        }

        if (chart.audioOffset != 0d)
        {
            json.Append("  \"audioOffset\": ").Append(Time(chart.audioOffset)).Append(",\n");
        }

        if (!string.IsNullOrEmpty(chart.jacket))
        {
            json.Append("  \"jacket\": ").Append(Quote(chart.jacket)).Append(",\n");
        }

        json.Append("  \"notes\": [");
        TapNoteData[] notes = chart.notes ?? new TapNoteData[0];
        for (int index = 0; index < notes.Length; index++)
        {
            json.Append(index == 0 ? "\n" : ",\n");
            AppendNote(json, notes[index]);
        }

        json.Append(notes.Length > 0 ? "\n  ],\n" : "],\n");

        json.Append("  \"lasers\": [");
        LaserNoteData[] lasers = chart.lasers ?? new LaserNoteData[0];
        for (int index = 0; index < lasers.Length; index++)
        {
            json.Append(index == 0 ? "\n" : ",\n");
            AppendLaser(json, lasers[index]);
        }

        json.Append(lasers.Length > 0 ? "\n  ]\n" : "]\n");
        json.Append("}\n");
        return json.ToString();
    }

    private static void AppendNote(StringBuilder json, TapNoteData note)
    {
        json.Append("    { \"lane\": ").Append(note.lane.ToString(CultureInfo.InvariantCulture));
        json.Append(", \"time\": ").Append(Time(note.time));
        if (note.duration > 0d && !note.IsFlickOnly && !note.IsSpin)
        {
            json.Append(", \"duration\": ").Append(Time(note.duration));
        }

        if (note.IsSpin)
        {
            json.Append(", \"spin\": ").Append(note.spin.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(note.stick))
            {
                json.Append(", \"stick\": ").Append(Quote(note.stick));
            }

            json.Append(" }");
            return;
        }

        if (note.HasDirection)
        {
            json.Append(", \"direction\": ").Append(((int)note.direction).ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(note.stick))
            {
                json.Append(", \"stick\": ").Append(Quote(note.stick));
            }

            if (note.flickOnly)
            {
                json.Append(", \"flickOnly\": true");
            }
        }

        json.Append(" }");
    }

    private static void AppendLaser(StringBuilder json, LaserNoteData laser)
    {
        json.Append("    {\n");
        json.Append("      \"side\": ").Append(laser.side.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        json.Append("      \"points\": [");
        for (int index = 0; index < laser.points.Length; index++)
        {
            LaserPointData point = laser.points[index];
            json.Append(index == 0 ? "\n" : ",\n");
            json.Append("        { \"time\": ").Append(Time(point.time));
            json.Append(", \"x\": ").Append(Number(point.x));
            json.Append(", \"y\": ").Append(Number(point.y)).Append(" }");
        }

        json.Append("\n      ]\n");
        json.Append("    }");
    }

    /// <summary>Seconds, to the tenth of a millisecond, with at least two decimals (2.50).</summary>
    private static string Time(double seconds)
    {
        return seconds.ToString("0.00##", CultureInfo.InvariantCulture);
    }

    private static string Number(double value)
    {
        return value.ToString("0.0###", CultureInfo.InvariantCulture);
    }

    private static string Quote(string text)
    {
        StringBuilder quoted = new StringBuilder("\"");
        foreach (char character in text ?? string.Empty)
        {
            switch (character)
            {
                case '"': quoted.Append("\\\""); break;
                case '\\': quoted.Append("\\\\"); break;
                case '\n': quoted.Append("\\n"); break;
                case '\r': quoted.Append("\\r"); break;
                case '\t': quoted.Append("\\t"); break;
                default:
                    if (character < ' ')
                    {
                        quoted.Append("\\u").Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        quoted.Append(character);
                    }

                    break;
            }
        }

        return quoted.Append('"').ToString();
    }
}
