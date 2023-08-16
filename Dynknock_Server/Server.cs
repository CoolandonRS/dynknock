using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using CoolandonRS.consolelib;
using Microsoft.Win32;
using PacketDotNet;
using SharpPcap;

namespace Dynknock_Server;

internal class Server {
    public static async Task Start(Hallway hallway, string hallwayName) {
        var devices = CaptureDeviceList.Instance;
        
        ILiveDevice? device;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            // ewwww windows
            device = devices.Select(inf => {
                var rawName = inf.Name!;
                var uuid = rawName[rawName.IndexOf('{')..];
                #pragma warning disable CA1416
                // who told you this was a good idea. It's not. Stop.
                var name = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Network\{4D36E972-E325-11CE-BFC1-08002BE10318}\" + uuid + @"\Connection")!.GetValue("Name")! as string;
                #pragma warning restore CA1416
                return (inf, name);
            }).FirstOrDefault(inf => inf.Item2 == hallway.inf).Item1;
        } else {
            // Look see some people are nice. You can be too, windows.
            device = devices.FirstOrDefault(inf => inf.Name == hallway.inf);
        }
        
        if (device == null) Fatal("Interface not found");
        device.Open(DeviceModes.None, 50);
        var inf = device!;

        var ips = NetworkInterface.GetAllNetworkInterfaces().First(nInf => Equals(nInf.GetPhysicalAddress(), inf.MacAddress)).GetIPProperties().UnicastAddresses.Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork).Select(addr => addr.Address).ToArray(); 
        // TODO-LT++ make the filter work on localhost too
        inf.Filter = $"{string.Join(" or ", ips.Select(ip => "dst host " + ip).ToArray())}";

        var doorkeeper = new Doorkeeper(hallway, hallwayName);
        
        inf.OnPacketArrival += (sender, capture) => {
            var packet = capture.GetPacket().GetPacket();
            var ipPacket = packet.Extract<IPPacket>();
            var tcpPacket = packet.Extract<TcpPacket>();
            var udpPacket = packet.Extract<UdpPacket>();

            Protocol protocol;
            int port;
            byte[] data;
            if (tcpPacket != null) {
                protocol = Protocol.Tcp;
                port = tcpPacket.DestinationPort;
                data = tcpPacket.PayloadData;
            } else if (udpPacket != null) {
                protocol = Protocol.Udp;
                port = udpPacket.DestinationPort;
                data = udpPacket.PayloadData;
            } else {
                return;
            }
            var ip = ipPacket.SourceAddress;

            if (doorkeeper.Registered(ip)) {
                doorkeeper.Knock(ip, (port, protocol));
            } else {
                doorkeeper.Ring(ip, port, data);
            }
        };
        
        inf.StartCapture();

        await Task.Delay(-1);
    }

    public static void Fatal(string msg, int exit = -1) {
        ConsoleUtil.WriteColoredLine($"Fatal: {msg}", ConsoleColor.Red);
        throw new Exception();
    }
}