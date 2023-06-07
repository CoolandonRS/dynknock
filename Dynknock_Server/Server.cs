using System.Buffers.Text;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using CoolandonRS.consolelib;
using Microsoft.Win32;
using PacketDotNet;
using SharpPcap;
using SharpPcap.WinpkFilter;

namespace Dynknock_Server;

internal class Server {
    // TODO change to conf file
    public static readonly ArgHandler ArgHandler = new(new Dictionary<string, ArgData>() {
            { "interface", new ArgData(new ArgDesc("--interface=[str]", "What interface to listen on")) },
            { "interval", new ArgData(new ArgDesc("--interval=[int]", "Interval to generate new codes in seconds (>=30). Default 1 day"), "86400") },
            { "length", new ArgData(new ArgDesc("--length=[int]", "The length of the sequence. Default 32"), "32") },
            { "timeout", new ArgData(new ArgDesc("--timeout=[int]", "How long to wait for sequence completion in seconds. Default 10"), "10") },
            { "doorbell", new ArgData(new ArgDesc("--doorbell=[port]", "The port to use as the doorbell. Should be unused."), "12345") }
        }, new Dictionary<char, FlagData>() {

        }
    );
    
    public static async Task Main(string[] args) {
        ArgHandler.ParseArgs(args);
        // TODO enforce param ranges and required params, and env var
        if (!ArgHandler.GetValue("interface").IsSet()) Fatal("Did not set interface");
        var devices = CaptureDeviceList.Instance;
        
        var infName = ArgHandler.GetValue("interface").AsString();
        ILiveDevice? device;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            // ewwww windows
            device = devices.Select(inf => {
                var rawName = inf.Name!;
                // Console.WriteLine(rawName);
                var uuid = rawName[rawName.IndexOf('{')..];
                #pragma warning disable CA1416
                // who told you this was a good idea. It's not. Stop.
                var name = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Network\{4D36E972-E325-11CE-BFC1-08002BE10318}\" + uuid + @"\Connection")!.GetValue("Name")! as string;
                #pragma warning restore CA1416
                // Console.WriteLine(name);
                return (inf, name);
            }).FirstOrDefault(inf => inf.Item2 == infName).Item1;
        } else {
            // Look see some people are nice. You can be too, windows.
            device = devices.FirstOrDefault(inf => inf.Name == infName);
        }
        
        if (device == null) Fatal("Interface not found");
        device.Open(DeviceModes.NoCaptureLocal, 50);
        var inf = device!;

        var ips = NetworkInterface.GetAllNetworkInterfaces().First(nInf => Equals(nInf.GetPhysicalAddress(), inf.MacAddress)).GetIPProperties().UnicastAddresses.Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork).Select(addr => addr.Address).ToArray(); 
        // TODO-LT make the filter work on localhost too
        // BEFORECOMMIT uncomment \/
        inf.Filter = $"{string.Join(" or ", ips.Select(ip => "dst host " + ip).ToArray())}";

        var doorkeeper = new Doorkeeper(Environment.GetEnvironmentVariable("KNOCK_KEY")!, ArgHandler.GetValue("interval").AsInt(), ArgHandler.GetValue("length").AsInt(), ArgHandler.GetValue("timeout").AsInt(), ArgHandler.GetValue("doorbell").AsInt());
        
        inf.OnPacketArrival += (sender, capture) => {
            var packet = capture.GetPacket().GetPacket();
            var ipPacket = packet.Extract<IPPacket>();
            var tcpPacket = packet.Extract<TcpPacket>();
            var udpPacket = packet.Extract<UdpPacket>();

            Protocol protocol;
            int port;
            if (tcpPacket != null) {
                protocol = Protocol.Tcp;
                port = tcpPacket.DestinationPort;
            } else if (udpPacket != null) {
                protocol = Protocol.Udp;
                port = udpPacket.DestinationPort;
            } else {
                return;
            }
            var ip = ipPacket.SourceAddress;

            doorkeeper.Ring(ip, (port, protocol));
            doorkeeper.Knock(ip, (port, protocol));
            
            // Console.WriteLine(packet);
        };
        
        inf.StartCapture();

        await Task.Delay(-1);
    }

    public static void Fatal(string msg, int exit = 1) {
        ConsoleUtil.WriteColoredLine($"Fatal: {msg}", ConsoleColor.Red);
        Environment.Exit(exit);
    }
}