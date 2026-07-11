using System.Security.Cryptography;

namespace Messenger.Api;

public static class Uuid7
{
    private static long _lastMs = -1;
    private static int _seq;
    private static readonly object _lock = new();

    public static Guid NewUuid7()
    {
        long ms;
        int seq;
        lock (_lock)
        {
            ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (ms <= _lastMs) { ms = _lastMs; _seq++; }
            else { _lastMs = ms; _seq = 0; }
            seq = _seq;
        }
        Span<byte> b = stackalloc byte[16];
        b[0] = (byte)(ms >> 40);
        b[1] = (byte)(ms >> 32);
        b[2] = (byte)(ms >> 24);
        b[3] = (byte)(ms >> 16);
        b[4] = (byte)(ms >> 8);
        b[5] = (byte)ms;
        b[6] = (byte)(0x70 | ((seq >> 8) & 0x0F));
        b[7] = (byte)seq;
        RandomNumberGenerator.Fill(b[8..]);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);
        return new Guid(b, bigEndian: true);
    }
}
