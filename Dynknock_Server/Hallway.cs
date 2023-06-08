using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using CoolandonRS.consolelib;

namespace Dynknock_Server; 

public class Hallway {
    private static OperatingSystem os;
    
    [JsonInclude]
    public readonly string key;
    [JsonInclude]
    public readonly int interval;
    [JsonInclude]
    public readonly int length;
    [JsonInclude]
    public readonly int timeout;
    [JsonInclude]
    public readonly int doorbell;
    [JsonInclude, JsonPropertyName("interface")]
    public readonly string inf;
    [JsonInclude, JsonPropertyName("openCommand")]
    public readonly string openCommand;
    [JsonInclude, JsonPropertyName("updateCommand")]
    public readonly string? updateCommand;
    [JsonInclude]
    public readonly int? closeDelay;
    [JsonInclude, JsonPropertyName("closeCommand")]
    public readonly string? closeCommand;

    private enum OperatingSystem {
        Windows, Linux
    }

    public void Open(IPAddress ip) {
        Execute(openCommand.Replace("%IP%", ip.ToString()));
        #pragma warning disable CS4014
        WaitAndClose(ip);
        #pragma warning restore CS4014
    }

    private async Task WaitAndClose(IPAddress ip) {
        Validate(); // just in case
        if (closeDelay == null) return;
        await Task.Delay(TimeSpan.FromSeconds(closeDelay!.Value));
        Execute(closeCommand!.Replace("%IP%", ip.ToString()));
    }

    public void Update() {
        if (updateCommand != null) Execute(updateCommand);
    }

    public void Validate() {
        if ((closeDelay != null && closeCommand == null) || (closeDelay == null && closeCommand != null)) throw new InvalidOperationException();
    }

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
            } + $"\"{cmd}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        #pragma warning disable CS4014
        RunProcess(new Process { StartInfo = startInfo });
        #pragma warning restore CS4014
    }

    private static async Task RunProcess(Process process) {
        process.Start();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) {
            ConsoleUtil.WriteColoredLine("Process exited with a non-zero exit code");
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