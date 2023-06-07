using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;

namespace Dynknock_Client; 
// SHARED

public static class SequenceGen {
    public static (int port, Protocol protocol)[] Gen(string key, int interval, int len) {
        var period = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds() / interval;
        return GenPeriod(key, period, len);
    }
    public static (int port, Protocol protocol)[] GenPeriod(string key, int period, int len) {
        var dat = new (int port, Protocol protocol)[len];
        for (var i = 0; i < len; i++) {
            dat[i] = GenDoor(key, period, i, len);
        }
        return dat;
    }

    private static byte[] GenHash(string key, int period, int seqIndex, int len) {
        return SHA256.HashData(Encoding.Unicode.GetBytes(key + period + seqIndex + len));
    }

    private static (int port, Protocol protocol) GenDoor(string key, int period, int seqIndex, int len) {
        var hash = GenHash(key, period, seqIndex, len);
        var port = Math.Abs((BitConverter.ToInt32(hash.AsSpan()[..4]) % 65535) + 1);
        var protocol = Math.Abs(BitConverter.ToInt32(hash.AsSpan()[4..8])) % 2 == 0 ? Protocol.Tcp : Protocol.Udp;
        return (port, protocol);
    }
}