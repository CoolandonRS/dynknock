using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using CoolandonRS.consolelib;
using ProtocolType = System.Net.Sockets.ProtocolType;

namespace Dynknock_Client;


internal class Client {
    // TODO change to conf file
    public static readonly ArgHandler ArgHandler = new(new Dictionary<string, ArgData>() {
            { "interval", new ArgData(new ArgDesc("--interval=[int]", "Interval to generate new codes in seconds (>=30). Default 1 day"), "86400") },
            { "length", new ArgData(new ArgDesc("--length=[int]", "The length of the sequence. Default 32"), "32") },
            { "doorbell", new ArgData(new ArgDesc("--doorbell=[port]", "The port to use as the doorbell."), "12345") },
            { "hostname", new ArgData(new ArgDesc("--hostname=[str]", "The hostname or ip of the server"))},
            { "pause", new ArgData(new ArgDesc("--pause=[int]", "The time to pause between knocks in ms. Default 50"), "50") },
        }, new Dictionary<char, FlagData>() {
            { 'p', new FlagData(new ArgDesc("-p", "Print when ports are knocked")) }
        }
    );
    
    public static async Task Main(string[] args) {
        ArgHandler.ParseArgs(args);
        // TODO enforce param ranges and required params, and env var
        var hostname = ArgHandler.GetValue("hostname").AsString();
        IPAddress ip;
        if (!IPAddress.TryParse(hostname, out ip)) {
            ip = (await Dns.GetHostAddressesAsync(hostname))[0];
        }
        var seq = SequenceGen.Gen(Environment.GetEnvironmentVariable("KNOCK_KEY")!, ArgHandler.GetValue("interval").AsInt(), ArgHandler.GetValue("length").AsInt());
        seq = seq.Prepend((ArgHandler.GetValue("doorbell").AsInt(), Protocol.Tcp)).ToArray();
        var pause = ArgHandler.GetValue("pause").AsInt();
        var print = ArgHandler.GetFlag('p');
        async Task knock(Socket sock, EndPoint ep) {
            await sock.ConnectAsync(ep);
            await sock.SendAsync(Array.Empty<byte>());
            sock.Close();
        }
        foreach (var (port, protocol) in seq) {
            var endpoint = new IPEndPoint(ip, port);
            var sock = protocol switch {
                Protocol.Tcp => new Socket(SocketType.Stream, ProtocolType.Tcp),
                Protocol.Udp => new Socket(SocketType.Dgram, ProtocolType.Udp),
                _ => throw new InvalidEnumArgumentException()
            };
            try {
                #pragma warning disable CS4014
                knock(sock, endpoint);
                #pragma warning restore CS4014
            } catch {
                // we expect to error so the catch is needed. Theres a chance we dont if the port is in use.
            } finally {
                sock.Dispose();
            }
            if (print) Console.WriteLine($"Knocked {port}/{protocol.ToString().ToLower()}");
            await Task.Delay(pause);
        }
    }

    public static void Fatal(string msg, int exit = 1) {
        ConsoleUtil.WriteColoredLine($"Fatal: {msg}", ConsoleColor.Red);
        Environment.Exit(exit);
    }
}