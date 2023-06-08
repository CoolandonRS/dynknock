using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
            { 'p', new FlagData(new ArgDesc("-p", "Print when ports are knocked")) }
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
            Environment.Exit(0);
        }
        var files = Directory.GetFiles(path);
        if (files.Length == 0) {
            Console.WriteLine($"Put your hallways in {path}");
            Environment.Exit(0);
        }

        var hallwayPath = files.Select(filepath => (path: filepath, name: Path.GetFileNameWithoutExtension(filepath))).First(val => val.name == argHandler.GetValue("hallway").AsString()).path;
        var hallway = JsonSerializer.Deserialize<Hallway>(await File.ReadAllTextAsync(hallwayPath));
        
        await Knock(hallway);
    }

    private static async Task Knock(Hallway hallway) {
        var hostname = hallway.hostname;
        IPAddress ip;
        if (!IPAddress.TryParse(hostname, out ip)) {
            ip = (await Dns.GetHostAddressesAsync(hostname))[0];
        }

        var seq = SequenceGen.Gen(hallway.key, hallway.interval, hallway.length);
        var print = argHandler.GetFlag('p');

        async Task knock(Socket sock, EndPoint ep) {
            await sock.ConnectAsync(ep);
            await sock.SendAsync(Array.Empty<byte>());
            sock.Close();
        }
        #pragma warning disable CS4014
        knock(new Socket(SocketType.Stream, ProtocolType.Tcp), new IPEndPoint(ip, hallway.doorbell));
        #pragma warning restore CS4014
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
                knock(sock, endpoint);
                #pragma warning restore CS4014
            } catch {
                // we expect to error so the catch is needed. Theres a chance we dont if the port is in use.
            } finally {
                sock.Dispose();
            }

            if (print) Console.WriteLine($"Knocked {port}/{protocol.ToString().ToLower()}");
            await Task.Delay(hallway.pause);
        }
    }

    public static void Fatal(string msg, int exit = 1) {
        ConsoleUtil.WriteColoredLine($"Fatal: {msg}", ConsoleColor.Red);
        Environment.Exit(exit);
    }
}