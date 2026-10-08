using System.Text;

namespace Loudify.Core;

public static class AtomicFile
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    /// <summary>
    /// Writes through a temporary file and a rename, so a reader (Equalizer APO
    /// reloads on every change) never sees a half-written file. Retries briefly
    /// because the reader may hold the target open while we replace it.
    /// </summary>
    public static void WriteAllText(string path, string contents)
    {
        WriteAllBytes(path, Utf8NoBom.GetBytes(contents));
    }

    public static void WriteAllBytes(string path, byte[] contents)
    {
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, contents);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(25);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
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
        }
    }
}
