using System.Runtime.InteropServices;
using System.Text.Json;
using CoolandonRS.consolelib;
using CoolandonRS.consolelib.Arg;
using CoolandonRS.consolelib.Arg.Builders;
using CoolandonRS.consolelib.Arg.Contracts;
using static Dynknock_Server.Escort.VerbosityUtil;

namespace Dynknock_Server; 

public class Escort {
    private static ArgHandler argHandler = new(
        new ValueArg<string?>("hallwayDir", "The directory to get Hallways from", null, str => str),
        new SingleFlagArg("verbose", "Verbose mode (print things other than failures)", 'v'),
        new SingleFlagArg("debug", "Debug mode. Very verbose, does not grant entry, and overrides timeout to 120. Adds doorbell commands \"ADVANCE_\" to manually advance the knock index, and \"ENDKNOCK\" to deny entry.", 'd'),
        new SingleFlagArg("advanceOnFailure", "DEBUG MODE ONLY: Don't advance on failure (if a knock is missed, the next correct knock is the missed one, not the next in the sequence)", 'a', true)
    );

    private static IArgContract argContract = ArgContracts.All(
        ArgContracts.Relations("advanceOnFailure", [["debug"]], [], IArgContract.ConditionalString.From(null, "-d must be set for -a"))
    );

    public static bool verbose { get; private set; }
    public static bool debug { get; private set; }
    public static bool advanceOnFail { get; private set; }
    public static async Task Main(string[] args) {
        argHandler.Parse(args);
        var contractResult = argHandler.Validate(argContract);
        if (!contractResult.Success) {
            Console.WriteLine("Argument validation error.\n" + (contractResult.Message.ToString() ?? ""));
            Environment.Exit(-1);
        }
        
        debug = argHandler.Get<bool>("debug");
        advanceOnFail = argHandler.Get<bool>("advanceOnFailure");
        verbose = argHandler.Get<bool>("verbose") && !debug; // debug verbosity overrides normal verbosity to avoid duplicate messages.

        var path = argHandler.Get<string?>("hallwayDir");
        if (path is null) {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                path = Path.Combine(Environment.GetEnvironmentVariable("appdata")!, @"\dynknock\hallways");
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                path = "/etc/dynknock/hallways";
            } else {
                throw new PlatformNotSupportedException();
            }
        }
        
        if (!Directory.Exists(path) || Directory.GetFiles(path).Length == 0) {
            Directory.CreateDirectory(path);
            Console.WriteLine($"Put your hallways in {path}");
            Environment.Exit(-1);
        }
        
        foreach (var file in new DirectoryInfo(path).GetFiles()) {
            if (file.Extension is not (".json" or ".hallway")) continue;
            var hallway = JsonSerializer.Deserialize(await File.ReadAllTextAsync(file.FullName), HallwayContext.Default.Hallway)!;
            new Thread(async () => {
                var hallwayName = Path.GetFileNameWithoutExtension(file.FullName).Replace(".server", "");
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