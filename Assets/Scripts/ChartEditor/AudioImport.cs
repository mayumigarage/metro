using System;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Copies a music file picked in the chart editor into <c>Assets/Resources/Audio</c>, so the
/// chart's <c>audio</c> path (Resources-relative, without extension) can load it, and sets the
/// import to Decompress On Load so the waveform can be read. Editor only: a build has no
/// AssetDatabase and cannot add Resources, so the editor's import button is not shown there.
/// </summary>
public static class AudioImport
{
    private const string FolderInProject = "Assets/Resources/Audio";

    /// <summary>Copies <paramref name="sourcePath"/> into the project and returns its Resources path (no extension), or null.</summary>
    public static string Import(string sourcePath)
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        string folder = Path.Combine(Application.dataPath, "Resources", "Audio");
        Directory.CreateDirectory(folder);

        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        string baseName = SanitizeName(Path.GetFileNameWithoutExtension(sourcePath));
        string fileName = baseName + extension;
        string destination = Path.Combine(folder, fileName);

        // Same name with different content gets a numbered name instead of overwriting another song.
        for (int n = 2; File.Exists(destination) && !SameContent(sourcePath, destination); n++)
        {
            fileName = $"{baseName}_{n}{extension}";
            destination = Path.Combine(folder, fileName);
        }

        if (!File.Exists(destination))
        {
            File.Copy(sourcePath, destination);
        }

        string assetPath = FolderInProject + "/" + fileName;
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
#else
        return null;
#endif
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
