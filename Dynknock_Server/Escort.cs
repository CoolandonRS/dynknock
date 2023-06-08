using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoolandonRS.consolelib;

namespace Dynknock_Server; 

public class Escort {
    private static ArgHandler argHandler = new ArgHandler(new Dictionary<string, ArgData>() {
        { "hallway-dir", new ArgData(new ArgDesc("--hallway-dir=[str]", "The directory to get Hallways from")) }
    }, new Dictionary<char, FlagData>() {
        
    });
    public static async Task Main(string[] args) {
        argHandler.ParseArgs(args);
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
            var hallway = JsonSerializer.Deserialize<Hallway>(File.ReadAllText(file), new JsonSerializerOptions() {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                ReadCommentHandling = JsonCommentHandling.Skip,
                IncludeFields = true
            })!;
            new Thread(async () => {
                var hallwayName = Path.GetFileNameWithoutExtension(file);
                try {
                    Console.WriteLine($"Starting hallway {hallwayName}");
                    await Server.Start(hallway);
                } catch {
                    ConsoleUtil.WriteColoredLine($"Hallway failure: {hallwayName}", ConsoleColor.Red);
                }
            }).Start();
        }
        
        await Task.Delay(-1);
    }
}