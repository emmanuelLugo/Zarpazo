using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace Zarpazo.Update;

/// <summary>
/// Downloads a release's installer into a folder of its own and only keeps it if its size and SHA-256 match what GitHub
/// published. Nothing is run from here.
/// </summary>
internal sealed class UpdateDownloader(HttpClient http, string folder)
{
    public static string DefaultFolder { get; } = Path.Combine(Path.GetTempPath(), "Zarpazo", "update");

    /// <returns>The path of the verified installer.</returns>
    /// <exception cref="UpdateException">It can't be downloaded, or what arrived is not what was published.</exception>
    public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (!release.CanInstallInPlace || !release.SetupUrl!.StartsWith(ReleaseFeed.DownloadPrefix, StringComparison.Ordinal))
        {
            throw new UpdateException("Esta versión no se puede instalar desde aquí. Descárgala desde la página de la versión.");
        }

        Directory.CreateDirectory(folder);
        foreach (string old in Directory.GetFiles(folder)) // earlier downloads, finished or not
        {
            TryDelete(old);
        }

        string target = Path.Combine(folder, $"Zarpazo-Setup-{release.Version}.exe");
        string partial = target + ".part";
        try
        {
            using HttpResponseMessage response = await http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"No se pudo descargar la actualización (código {(int)response.StatusCode}).");
            }

            long total = response.Content.Headers.ContentLength ?? release.SetupSize;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (Stream source = await response.Content.ReadAsStreamAsync(cancel))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                    received += read;
                    if (total > 0)
                    {
                        progress?.Report(Math.Min(1.0, (double)received / total));
                    }
                }
            }

            if (release.SetupSize > 0 && received != release.SetupSize)
            {
                throw new UpdateException("La descarga llegó incompleta. Inténtalo de nuevo.");
            }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(release.SetupSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException("Lo descargado no coincide con lo que se publicó en GitHub, así que no se instaló.");
            }

            File.Move(partial, target, overwrite: true);
            return target;
        }
        catch (HttpRequestException)
        {
            throw new UpdateException("No hay conexión con GitHub.");
        }
        catch (IOException ex)
        {
            throw new UpdateException($"No se pudo guardar la descarga: {ex.Message}");
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a leftover in the temp folder is harmless
        }
    }
}
