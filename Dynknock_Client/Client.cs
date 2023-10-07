using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CoolandonRS.consolelib;
using ProtocolType = System.Net.Sockets.ProtocolType;

namespace Dynknock_Client;


internal class Client {
    // TODO-LT++ change to conf file
    public static readonly ArgHandler argHandler = new(new Dictionary<string, ArgData>() {
            { "hallway", new ArgData(new ArgDesc("--hallway=[str]", "The name (without extensions) of the hallway you want to use")) },
            { "hallway-dir", new ArgData(new ArgDesc("--hallway-dir=[str]", "The directory to get Hallways from")) }
        }, new Dictionary<char, FlagData>() {
            { 'p', new FlagData(new ArgDesc("-p", "Print when ports are knocked")) },
            { 'm', new FlagData(new ArgDesc("-m", "Manual mode. Require user input before each knock (implies -p)"))},
            { 't', new FlagData(new ArgDesc("-t", "DEBUG SERVER: send ENDKNOCK when complete.")) },
            { 'T', new FlagData(new ArgDesc("-T", "DEBUG SERVER: send ENDKNOCK")) },
            { 'A', new FlagData(new ArgDesc("-A", "DEBUG SERVER: Send ADVANCE_")) }
        }
    );

    public static async Task Main(string[] args) {
        argHandler.ParseArgs(args);
        // TODO-LT+++ enforce param ranges and required params, and env var
        string path;
        if (argHandler.GetValue("hallway-dir").IsSet()) {
            path = argHandler.GetValue("hallway-dir").AsString();
        } else {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                path = @$"{Environment.GetEnvironmentVariable("USERPROFILE")}\.hallways";
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                path = @$"{Environment.GetEnvironmentVariable("HOME")}/.hallways";
            } else {
                throw new PlatformNotSupportedException();
            }
        }
        if (!Directory.Exists(path)) {
            Directory.CreateDirectory(path);
            Console.WriteLine($"Put your hallways in {path}");
            Environment.Exit(-1);
        }
        var files = Directory.GetFiles(path);
        if (files.Length == 0) {
            Console.WriteLine($"Put your hallways in {path}");
            Environment.Exit(-1);
        }

        if (!argHandler.GetValue("hallway").IsSet()) {
            Console.WriteLine("You must specify a hallway to use. (--hallway)");
            Environment.Exit(-1);
        }

        var hallwayPath = "";
        try {
            hallwayPath = files.Select(filepath => (path: filepath, name: Path.GetFileNameWithoutExtension(filepath))).First(val => val.name.Replace(".client", "") == argHandler.GetValue("hallway").AsString()).path;
        } catch (InvalidOperationException) {
            Console.WriteLine($"No hallway of name {argHandler.GetValue("hallway").AsString()} found");
            Environment.Exit(-1);
        }
        var hallway = JsonSerializer.Deserialize(await File.ReadAllTextAsync(hallwayPath), HallwayContext.Default.Hallway)!;
        IPAddress ip;
        if (!IPAddress.TryParse(hallway.hostname, out ip)) {
            try {
                ip = (await Dns.GetHostAddressesAsync(hallway.hostname))[0];
            } catch (Exception e) when (e is SocketException or ArgumentException) {
                Console.WriteLine("No such host is known");
                Environment.Exit(-1);
            }
        }
        string doorbellText;
        var termAfterDoorbell = true;
        if (argHandler.GetFlag('T')) {
            doorbellText = "ENDKNOCK";
        } else if (argHandler.GetFlag('A')) {
            doorbellText = "ADVANCE_";
        } else {
            doorbellText = $"DOORBELL{(int)DateTimeOffset.UtcNow.ToUnixTimeSeconds() / hallway.interval}";
            termAfterDoorbell = false;
        }
        var sock = new Socket(SocketType.Dgram, ProtocolType.Udp);
        var ep = new IPEndPoint(ip, hallway.doorbell);
        #pragma warning disable CS4014
        Knock(new Socket(SocketType.Dgram, ProtocolType.Udp), new IPEndPoint(ip, hallway.doorbell), Encoding.UTF8.GetBytes(doorbellText));
        #pragma warning restore CS4014
        if (termAfterDoorbell) return;
        await KnockHallway(hallway, ip);
    }
    
    private static async Task Knock(Socket sock, EndPoint ep, byte[]? data = null) {
        await sock.ConnectAsync(ep);
        await sock.SendAsync(data ?? Array.Empty<byte>());
        sock.Close();
    }

    private static async Task KnockHallway(Hallway hallway, IPAddress ip) {
        var seq = SequenceGen.Gen(SequenceGen.GetKey(hallway.key), hallway.interval, hallway.length);
        var print = argHandler.GetFlag('p');

        
        
        if (print) Console.WriteLine($"Rung {hallway.doorbell}");
        await Task.Delay(hallway.pause);
        foreach (var (port, protocol) in seq) {
            var endpoint = new IPEndPoint(ip, port);
            var sock = protocol switch {
                Protocol.Tcp => new Socket(SocketType.Stream, ProtocolType.Tcp),
                Protocol.Udp => new Socket(SocketType.Dgram, ProtocolType.Udp),
                _ => throw new InvalidEnumArgumentException()
            };
            try {
                #pragma warning disable CS4014
                Knock(sock, endpoint);
                #pragma warning restore CS4014
            } catch {
                // we expect to error so the catch is needed. Theres a chance we dont if the port is in use.
            } finally {
                sock.Dispose();
            }

            if (print) Console.WriteLine($"Knocked {port}/{protocol.ToString().ToLower()}");
            await Task.Delay(hallway.pause);
        }
        #pragma warning disable CS4014
        if (argHandler.GetFlag('t')) Knock(new Socket(SocketType.Dgram, ProtocolType.Udp), new IPEndPoint(ip, hallway.doorbell), "ENDKNOCK"u8.ToArray());
        #pragma warning restore CS4014 
    }

    public static void Fatal(string msg, int exit = 1) {
        ConsoleUtil.WriteColoredLine($"Fatal: {msg}", ConsoleColor.Red);
        Environment.Exit(exit);
    }
}