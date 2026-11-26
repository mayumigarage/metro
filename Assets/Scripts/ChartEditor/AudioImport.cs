using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
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
/// turns that back into a file path, and the chart editor and the game load it with <see cref="LoadFile"/>.</item>
/// </list>
/// </summary>
public static class AudioImport
{
    /// <summary>Prefix of a chart's <c>audio</c> value that points into <c>persistentDataPath</c>.</summary>
    public const string FilePrefix = "file:";

    private const string ProjectFolder = "Assets/Resources/Audio";

    // The clip loaded from a file at run time (build) and the audio value it was loaded for. Its samples are
    // fully in memory, so they can be read even though its loadType is not necessarily Decompress On Load.
    // Only the latest one is kept.
    private static string fileClipAudio;
    private static AudioClip fileClip;

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

    /// <summary>Remembers the clip loaded from the file of a <c>file:</c> audio value, replacing the previous one.</summary>
    public static void SetFileClip(string audio, AudioClip clip)
    {
        bool replaced = fileClip != null && fileClip != clip;
        fileClipAudio = audio;
        fileClip = clip;
        if (replaced)
        {
            // A whole decompressed song: free it once nothing (chart editor, AudioSource) holds it any more.
            Resources.UnloadUnusedAssets();
        }
    }

    /// <summary>
    /// Reads the file of a <c>file:</c> audio value into a clip, over a few frames. <paramref name="done"/>
    /// gets the clip, or null and a message saying why not. The caller decides whether to
    /// <see cref="SetFileClip"/> it.
    /// </summary>
    public static IEnumerator LoadFile(string audio, Action<AudioClip, string> done)
    {
        string absolute = ResolveFilePath(audio);
        if (!File.Exists(absolute))
        {
            done(null, "音源ファイルが見つかりません: " + audio);
            yield break;
        }

        using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(absolute).AbsoluteUri, AudioTypeOf(absolute));
        ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = false;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            done(null, "音源を読み込めませんでした: " + request.error);
            yield break;
        }

        done(DownloadHandlerAudioClip.GetContent(request), null);
    }

    /// <summary>
    /// The clip of a chart's <c>audio</c> value: a Resources clip, or for a <c>file:</c> value the clip last
    /// loaded from that file (<see cref="LoadFile"/>). Null when there is none.
    /// </summary>
    public static AudioClip GetClip(string audio)
    {
        if (string.IsNullOrWhiteSpace(audio))
        {
            return null;
        }

        if (IsFileAudio(audio))
        {
            return audio == fileClipAudio ? fileClip : null;
        }

        return Resources.Load<AudioClip>(audio);
    }

    /// <summary>True when the clip's samples can be read (waveform, auto-generation).</summary>
    public static bool CanReadSamples(AudioClip clip)
    {
        return clip != null && (clip.loadType == AudioClipLoadType.DecompressOnLoad || clip == fileClip);
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
