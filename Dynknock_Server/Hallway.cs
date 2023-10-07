using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json.Serialization;
using CoolandonRS.consolelib;
using static Dynknock_Server.Escort.VerbosityUtil;

namespace Dynknock_Server; 

public class Hallway {
    private static OperatingSystem os;
    
    // ReSharper disable InconsistentNaming
    [JsonInclude]
    public string key { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude]
    public int interval { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude]
    public int length { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude]
    public int timeout { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude]
    public int doorbell { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude, JsonPropertyName("interface")]
    public string inf { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude]
    public string openCommand { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude]
    public int? closeDelay { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude]
    public string? closeCommand { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude, JsonPropertyName("failCommand")]
    public string? banishmentCommand { get; [Obsolete("Use only for source generation")] internal set; }
    // ReSharper restore InconsistentNaming

    private enum OperatingSystem {
        Windows, Linux
    }

    public void Open(IPAddress ip) {
        WriteVerbose($"Opening for {ip}");
        Execute(openCommand, ip);
        #pragma warning disable CS4014
        WaitAndClose(ip);
        #pragma warning restore CS4014
    }

    private async Task WaitAndClose(IPAddress ip) {
        Validate(); // just in case
        if (closeDelay == null) return;
        await Task.Delay(TimeSpan.FromSeconds(closeDelay!.Value));
        Execute(closeCommand!, ip);
    }

    public void Banish(IPAddress ip) {
        WriteVerbose($"Banishing {ip}");
        if (banishmentCommand != null) Execute(banishmentCommand, ip);
    }

    public void Validate() {
        if ((closeDelay == null) != (closeCommand == null)) throw new InvalidOperationException();
    }

    private static void Execute(string cmd, IPAddress ip) => Execute(cmd.Replace("%IP%", ip.ToString()));
    private static void Execute(string cmd) {
        var startInfo = new ProcessStartInfo {
            FileName = os switch {
                OperatingSystem.Windows => "cmd.exe",
                OperatingSystem.Linux => "/bin/bash",
                _ => throw new InvalidEnumArgumentException()
            },
            Arguments = os switch {
                OperatingSystem.Windows => "/c",
                OperatingSystem.Linux => "-c",
                _ => throw new InvalidEnumArgumentException()
            } + $" \"{cmd}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };
        #pragma warning disable CS4014
        RunProcess(new Process { StartInfo = startInfo });
        #pragma warning restore CS4014
    }

    private static async Task RunProcess(Process process) {
        if (Escort.debug) throw new SecurityException("Attempted to run a command in debug mode");
        process.Start();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) {
            ConsoleUtil.WriteColoredLine("Process exited with a non-zero exit code", ConsoleColor.Yellow);
        }
    }
    static Hallway() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            os = OperatingSystem.Windows;
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            os = OperatingSystem.Linux;
        } else {
            throw new PlatformNotSupportedException();
        }
    }
}