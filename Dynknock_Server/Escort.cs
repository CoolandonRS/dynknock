using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Text.Json;
using CoolandonRS.consolelib;
using static Dynknock_Server.Escort.VerbosityUtil;

namespace Dynknock_Server; 

public class Escort {
    private static ArgHandler argHandler = new ArgHandler(new Dictionary<string, ArgData>() {
        { "hallway-dir", new ArgData(new ArgDesc("--hallway-dir=[str]", "The directory to get Hallways from")) }
    }, new Dictionary<char, FlagData>() {
        { 'v', new FlagData(new ArgDesc("-v", "Verbose mode (print things other then failures)"))},
        { 'd', new FlagData(new ArgDesc("-d", "Debug mode. Very verbose, does not grant entry, and overrides timeout to 120. Adds doorbell commands \"ADVANCE_\" to manually advance the knock index, and \"ENDKNOCK\" to deny entry."))},
        { 'a', new FlagData(new ArgDesc("-a", "Only in debug mode: Don't advance on failure (if a knock is missed, the next correct knock is the missed one, not the next in the sequence)"))}
    });

    public static bool verbose { get; private set; }
    public static bool debug { get; private set; }
    public static bool dontAdvanceOnFail { get; private set; }
    public static async Task Main(string[] args) {
        argHandler.ParseArgs(args);
        debug = argHandler.GetFlag('d');
        dontAdvanceOnFail = argHandler.GetFlag('a') && debug;
        verbose = argHandler.GetFlag('v') && !debug; // debug verbosity overrides normal verbosity to avoid duplicate messages.
        string path;
        if (argHandler.GetValue("hallway-dir").IsSet()) {
            path = argHandler.GetValue("hallway-dir").AsString();
        } else {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                path = @$"{Environment.GetEnvironmentVariable("appdata")}\dynknock\hallways";
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                path = @"/etc/dynknock/hallways";
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
        
        foreach (var file in files) {
            if (Path.GetExtension(file) is not (".json" or ".hallway")) continue;
            var hallway = JsonSerializer.Deserialize(await File.ReadAllTextAsync(file), HallwayContext.Default.Hallway)!;
            new Thread(async () => {
                var hallwayName = Path.GetFileNameWithoutExtension(file).Replace(".server", "");
                try {
                    WriteEither($"Starting hallway {hallwayName}");
                    await Server.Start(hallway, hallwayName);
                } catch (Exception e) {
                    ConsoleUtil.WriteColoredLine($"Hallway failure: {hallwayName}", ConsoleColor.Red);
                    WriteEither(e.Message, ConsoleColor.Red);
                    if (debug) throw;
                }
            }).Start();
        }
        
        await Task.Delay(-1);
    }

    public static class VerbosityUtil {
        public static void WriteVerbose(string msg, ConsoleColor? color = null) {
            if (!verbose) return;
            if (color == null) Console.WriteLine(msg);
            else ConsoleUtil.WriteColoredLine(msg, color);
        }

        public static void WriteDebug(string msg, ConsoleColor? color = null) {
            if (!debug) return;
            if (color == null) Console.WriteLine(msg);
            else ConsoleUtil.WriteColoredLine(msg, color);
        }

        public static void WriteEither(string msg, ConsoleColor? color = null) {
            if (!(verbose || debug)) return;
            if (color == null) Console.WriteLine(msg);
            else ConsoleUtil.WriteColoredLine(msg, color);
        }

        public static void WriteDependent(string verbose, string debug, ConsoleColor? color = null) {
            WriteVerbose(verbose, color);
            WriteDebug(debug, color);
        }

        public static void WriteError(string verbose, string? debug = null) {
            WriteVerbose(verbose, ConsoleColor.Red);
            WriteDebug(debug ?? verbose, ConsoleColor.Yellow);
        }

        public static void WhenDebug(Action action) {
            if (debug) action();
        }
        
        public static void WhenNotDebug(Action action) {
            if (!debug) action();
        }

        public static void SwitchDebug(Action @true, Action @false) {
            if (debug) @true(); else @false();
        }
    }
}