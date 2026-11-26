using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Copies a music file picked in the chart editor and returns the value to store in the chart's
/// <c>audio</c> field.
/// <list type="bullet">
/// <item>Editor: copied into <c>Assets/Resources/Audio</c>, imported as Decompress On Load, and stored as a
/// Resources path (<c>Audio/name</c>, no extension).</item>
/// <item>Build: Resources cannot be added at run time, so the file is copied into
/// <c>persistentDataPath/Audio</c> and stored as <c>file:Audio/name.ext</c>. <see cref="ResolveFilePath"/>
/// turns that back into a file path, and the editor loads it with UnityWebRequest.</item>
/// </list>
/// </summary>
public static class AudioImport
{
    /// <summary>Prefix of a chart's <c>audio</c> value that points into <c>persistentDataPath</c>.</summary>
    public const string FilePrefix = "file:";

    private const string ProjectFolder = "Assets/Resources/Audio";

    // Clips loaded from files at run time (build). Their samples are fully in memory, so they can be read
    // even though their loadType is not necessarily Decompress On Load. Only the latest one is kept.
    private static readonly HashSet<AudioClip> fileClips = new HashSet<AudioClip>();

    /// <summary>Copies <paramref name="sourcePath"/> into the project (editor) or the persistent folder (build) and returns the value for the chart's <c>audio</c>, or null.</summary>
    public static string Import(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

#if UNITY_EDITOR
        return ImportIntoProject(sourcePath);
#else
        string folder = Path.Combine(Application.persistentDataPath, "Audio");
        string fileName = CopyUnique(sourcePath, folder);
        return FilePrefix + "Audio/" + fileName;
#endif
    }

    /// <summary>True when a chart's audio value names a file under persistentDataPath (not a Resources path).</summary>
    public static bool IsFileAudio(string audio)
    {
        return audio != null && audio.StartsWith(FilePrefix, StringComparison.Ordinal);
    }

    /// <summary>The absolute file path of a <c>file:</c> audio value, or null for anything else.</summary>
    public static string ResolveFilePath(string audio)
    {
        if (!IsFileAudio(audio))
        {
            return null;
        }

        string relative = audio.Substring(FilePrefix.Length).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(Application.persistentDataPath, relative);
    }

    /// <summary>Remembers a clip loaded from a file, replacing the previous one.</summary>
    public static void MarkAsFileClip(AudioClip clip)
    {
        fileClips.Clear();
        if (clip != null)
        {
            fileClips.Add(clip);
        }
    }

    /// <summary>True when the clip's samples can be read (waveform, auto-generation).</summary>
    public static bool CanReadSamples(AudioClip clip)
    {
        return clip != null && (clip.loadType == AudioClipLoadType.DecompressOnLoad || fileClips.Contains(clip));
    }

    /// <summary>The UnityWebRequest audio type for a file's extension.</summary>
    public static AudioType AudioTypeOf(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".wav": return AudioType.WAV;
            case ".ogg": return AudioType.OGGVORBIS;
            case ".mp3": return AudioType.MPEG;
            case ".aif":
            case ".aiff": return AudioType.AIFF;
            default: return AudioType.UNKNOWN;
        }
    }

#if UNITY_EDITOR
    private static string ImportIntoProject(string sourcePath)
    {
        string folder = Path.Combine(Application.dataPath, "Resources", "Audio");
        string fileName = CopyUnique(sourcePath, folder);

        string assetPath = ProjectFolder + "/" + fileName;
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

        if (AssetImporter.GetAtPath(assetPath) is AudioImporter importer)
        {
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (settings.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }

        return "Audio/" + Path.GetFileNameWithoutExtension(fileName);
    }
#endif

    /// <summary>
    /// Copies the file into <paramref name="folder"/> and returns its file name (with extension). A name already
    /// taken by a different file gets a number instead of overwriting another song.
    /// </summary>
    private static string CopyUnique(string sourcePath, string folder)
    {
        Directory.CreateDirectory(folder);

        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        string baseName = SanitizeName(Path.GetFileNameWithoutExtension(sourcePath));
        string fileName = baseName + extension;
        string destination = Path.Combine(folder, fileName);

        for (int n = 2; File.Exists(destination) && !SameContent(sourcePath, destination); n++)
        {
            fileName = $"{baseName}_{n}{extension}";
            destination = Path.Combine(folder, fileName);
        }

        if (!File.Exists(destination))
        {
            File.Copy(sourcePath, destination);
        }

        return fileName;
    }

    private static string SanitizeName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "audio" : name.Trim();
    }

    private static bool SameContent(string a, string b)
    {
        FileInfo fa = new FileInfo(a);
        FileInfo fb = new FileInfo(b);
        if (fa.Length != fb.Length)
        {
            return false;
        }

        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
