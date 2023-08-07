using System.Security.Cryptography;
using System.Text;

namespace Dynknock_Server; 
// SHARED
public static class SequenceGen {
    public static (int port, Protocol protocol)[] Gen(byte[] key, int interval, int len) {
        var period = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds() / interval;
        return GenPeriod(key, period, len);
    }
    public static (int port, Protocol protocol)[] GenPeriod(byte[] key, int period, int len) {
        var dat = new (int port, Protocol protocol)[len];
        for (var i = 0; i < len; i++) {
            dat[i] = GenDoor(key, period, i, len);
        }
        return dat;
    }

    private static byte[] GenHash(byte[] key, int period, int seqIndex, int len) {
        return new HMACSHA256(key).ComputeHash(Encoding.Unicode.GetBytes("" + period + seqIndex + len));
    }

    private static (int port, Protocol protocol) GenDoor(byte[] key, int period, int seqIndex, int len) {
        var hash = GenHash(key, period, seqIndex, len);
        var port = (int) (BitConverter.ToUInt32(hash.AsSpan()[..4]) % 65535) + 1; // 2^32 - 1 % 65535 == 0, so no modulo bias
        var protocol = (hash[4] & 1) == 0 ? Protocol.Tcp : Protocol.Udp;
        return (port, protocol);
    }
    
    /// <summary>
    /// Taking a string, it returns the base64 represented by the string if applicable, otherwise the unicode representation of the string.
    /// </summary>
    public static byte[] GetKey(string str) {
        try {
            return Convert.FromBase64String(str);
        } catch (FormatException) {
            return Encoding.Unicode.GetBytes(str);
        }
    }
}