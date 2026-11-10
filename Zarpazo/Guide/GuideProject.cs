using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Zarpazo.Guide;

/// <summary>
/// A guide saved as one <c>.zarpazo</c> file, so it can be finished later or kept for when the screens change. It is a zip with
/// <c>guide.json</c> (title, and each step's text and picture in order) and the pictures as PNG files.
/// </summary>
internal static class GuideProject
{
    public const string Extension = ".zarpazo";

    private const string ManifestName = "guide.json";
    private const int Version = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed record Manifest(int Version, string Title, List<StepEntry> Steps);

    private sealed record StepEntry(string Text, string Image);

    /// <summary>Writes the guide. An existing file is replaced only once the new one is complete.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The place is not writable.</exception>
    public static void Save(GuideDocument guide, string path)
    {
        string partial = path + ".tmp";
        try
        {
            using (FileStream file = File.Create(partial))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var steps = new List<StepEntry>();
                foreach (GuideStep step in guide.Steps)
                {
                    string name = $"steps/{step.Number:D3}.png";
                    steps.Add(new StepEntry(step.Text, name));
                    ZipArchiveEntry picture = zip.CreateEntry(name, CompressionLevel.NoCompression); // a PNG is already compressed
                    using Stream target = picture.Open();
                    target.Write(step.ReadPng());
                }

                ZipArchiveEntry manifest = zip.CreateEntry(ManifestName);
                using Stream json = manifest.Open();
                JsonSerializer.Serialize(json, new Manifest(Version, guide.Title, steps), JsonOptions);
            }
            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            File.Delete(partial); // nothing left behind if it failed half way
        }
    }

    /// <summary>
    /// Replaces the contents of <paramref name="guide"/> with the project. Everything is read and checked first, so a damaged
    /// file leaves the guide as it was.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a guide, is damaged, or comes from a newer Zarpazo.</exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static void Load(GuideDocument guide, string path)
    {
        string title;
        var steps = new List<(string Text, byte[] Png)>();
        try
        {
            using var zip = ZipFile.OpenRead(path);
            ZipArchiveEntry entry = zip.GetEntry(ManifestName) ?? throw new InvalidDataException("No es una guía de Zarpazo.");
            Manifest manifest;
            using (Stream json = entry.Open())
            {
                manifest = JsonSerializer.Deserialize<Manifest>(json, JsonOptions) ?? throw new InvalidDataException("La guía está vacía o dañada.");
            }
            if (manifest.Version > Version)
            {
                throw new InvalidDataException("La guía es de una versión más nueva de Zarpazo.");
            }

            title = manifest.Title ?? "";
            foreach (StepEntry step in manifest.Steps ?? [])
            {
                ZipArchiveEntry picture = zip.GetEntry(step.Image ?? "") ?? throw new InvalidDataException("Falta una imagen dentro de la guía.");
                using var memory = new MemoryStream();
                using (Stream source = picture.Open())
                {
                    source.CopyTo(memory);
                }

                byte[] png = memory.ToArray();
                _ = BitmapFrame.Create(new MemoryStream(png)); // fails now, not later when the list is drawn, if it is not a picture
                steps.Add((step.Text ?? "", png));
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or FileFormatException or InvalidDataException)
        {
            throw ex as InvalidDataException ?? new InvalidDataException("El archivo está dañado o no es una guía de Zarpazo.", ex);
        }

        guide.Clear();
        guide.Title = title;
        foreach ((string text, byte[] png) in steps)
        {
            guide.AddPng(png).Text = text;
        }
    }
}
