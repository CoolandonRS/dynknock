using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CoolandonRS.consolelib;
using CoolandonRS.consolelib.Arg;
using CoolandonRS.consolelib.Arg.Builders;
using CoolandonRS.consolelib.Arg.Contracts;
using ProtocolType = System.Net.Sockets.ProtocolType;

namespace Dynknock_Client;


internal class Client {
    // TODO-LT++ change to conf file
    public static readonly ArgHandler argHandler = new(
        new ValueArg<string>("hallway", "The name (without extensions) of the hallway you want to use", "", str => str),
        new ValueArg<string?>("hallwayDir", "The directory to get Hallways from", null, str => str),
        new SingleFlagArg("verbose", "Print when ports are knocked", 'v'),
        new SingleFlagArg("manual", "Require user input before each knock (implies -p)", 'm'),
        new SingleFlagArg("terminate", "DEBUG SERVER ONLY: send ENDKNOCK when complete.", 't'),
        new SingleFlagArg("terminateNow", "DEBUG SERVER ONLY: send ENDKNOCK", 'T'),
        new SingleFlagArg("advance", "DEBUG SERVER ONLY: Send ADVANCE_", 'A')
    );

    private static readonly IArgContract argContract = ArgContracts.All(
        ArgContracts.Present("hallway", IArgContract.ConditionalString.From(null, "Hallway must be specified")),
        ArgContracts.Relations("manual", [], ["terminateNow", "advance"], IArgContract.ConditionalString.From(null, "-m cannot be used with -T or -A")),
        ArgContracts.Relations("terminate", [], ["terminateNow", "advance"], IArgContract.ConditionalString.From(null, "-t cannot be used with -T or -A")),
        ArgContracts.Relations("terminateNow", [], ["advance"], IArgContract.ConditionalString.From(null, "-T cannot be used with -A"))
    );

    public static async Task Main(string[] args) {
        argHandler.Parse(args);
        var contractResult = argHandler.Validate(argContract);
        if (!contractResult.Success) Fatal("Argument validation failed.\n" + (contractResult.Message.ToString() ?? ""));
        
        var path = argHandler.Get<string?>("hallwayDir");
        if (path is null) {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                path = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE")!, ".hallways");
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                path = Path.Combine(Environment.GetEnvironmentVariable("HOME")!, ".hallways");
            } else {
                throw new PlatformNotSupportedException();
            }
        }
        if (!Directory.Exists(path) || Directory.GetFiles(path).Length == 0) {
            Directory.CreateDirectory(path);
            Fatal($"Put your hallways in {path}");
        }

        var hallwayName = argHandler.Get<string>("hallway");
        var hallwayFile = new DirectoryInfo(path).GetFiles().Select(info => Path.GetFileNameWithoutExtension(info.FullName)).FirstOrDefault(name => name == hallwayName);
        if (hallwayFile is null) Fatal($"The hallway file {hallwayName} doesn't exist!");
        
        var hallway = JsonSerializer.Deserialize(await File.ReadAllTextAsync(hallwayFile), HallwayContext.Default.Hallway)!;

        if (!IPAddress.TryParse(hallway.hostname, out var ip)) {
            try {
                ip = (await Dns.GetHostAddressesAsync(hallway.hostname))[0];
            } catch (Exception e) when (e is SocketException or ArgumentException) {
                Fatal("No such host is known");
            }
        }
        
        string doorbellText;
        var termAfterDoorbell = true;
        if (argHandler.Get<bool>("terminateNow")) {
            doorbellText = "ENDKNOCK";
        } else if (argHandler.Get<bool>("advance")) {
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
        var print = argHandler.Get<bool>("verbose");
        
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
        if (argHandler.Get<bool>("terminate")) Knock(new Socket(SocketType.Dgram, ProtocolType.Udp), new IPEndPoint(ip, hallway.doorbell), "ENDKNOCK"u8.ToArray());
        #pragma warning restore CS4014 
    }

    [DoesNotReturn]
    public static void Fatal(string msg, int exit = -1) {
        ConsoleUtil.WriteColoredLine($"Fatal: {msg}", ConsoleColor.Red);
        Environment.Exit(exit);
    }
}