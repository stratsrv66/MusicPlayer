using System.Text;

namespace MusicPlatform.IntegrationTests;

/// <summary>
/// Génère des fichiers WAV valides en mémoire.
///
/// Produire l'audio à la volée évite d'embarquer un binaire dans le dépôt et garantit
/// que le pipeline d'upload est éprouvé avec un contenu réellement décodable.
/// </summary>
public static class TestAudio
{
    private const int SampleRate = 44100;
    private const short Channels = 1;
    private const short BitsPerSample = 16;

    /// <summary>Construit un WAV PCM mono de <paramref name="seconds"/> secondes à 440 Hz.</summary>
    public static byte[] CreateWav(int seconds = 12)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seconds, 1);

        var sampleCount = SampleRate * seconds;
        var dataSize = sampleCount * Channels * (BitsPerSample / 8);

        using var buffer = new MemoryStream(44 + dataSize);
        using var writer = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1); // PCM non compressé
        writer.Write(Channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * Channels * (BitsPerSample / 8));
        writer.Write((short)(Channels * (BitsPerSample / 8)));
        writer.Write(BitsPerSample);

        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);

        for (var i = 0; i < sampleCount; i++)
        {
            var value = (short)(short.MaxValue * 0.3 * Math.Sin(2 * Math.PI * 440 * i / SampleRate));
            writer.Write(value);
        }

        writer.Flush();
        return buffer.ToArray();
    }

    /// <summary>Construit un contenu qui n'est pas de l'audio, pour vérifier les rejets.</summary>
    public static byte[] CreateNonAudio() => "Ceci n'est pas un fichier audio."u8.ToArray();

    /// <summary>Construit une image PNG 2x2 valide, pour les tests de pochette.</summary>
    public static byte[] CreatePng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02,
        0x08, 0x02, 0x00, 0x00, 0x00, 0xFD, 0xD4, 0x9A, 0x73,
        0x00, 0x00, 0x00, 0x16, 0x49, 0x44, 0x41, 0x54,
        0x78, 0x9C, 0x63, 0xFC, 0xCF, 0xC0, 0xF0, 0x9F, 0x81, 0x81,
        0x89, 0x81, 0x01, 0x00, 0x14, 0xC6, 0x02, 0xFB, 0x9C, 0x9D, 0x8D, 0x8B,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];
}
