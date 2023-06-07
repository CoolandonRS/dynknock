using System.Net;
using System.Runtime.CompilerServices;
using PacketDotNet.Utils.Converters;

namespace Dynknock_Server;

public class Doorkeeper {
    private (int port, Protocol protocol)[] sequence;
    private string key;
    private int interval;
    private int period;
    private int len;
    private int timeout;
    private Dictionary<IPAddress, Guest> guests;
    private int doorbell;

    private class Guest {
        public IPAddress ip { get; private set; }
        private int idx = 0;
        private (int port, Protocol protocol)[] sequence;
        private Action<Guest> finish;
        private bool disposed = false;
        private Action<Guest> clean;
        private CancellationTokenSource cancel;

        public void Knock((int port, Protocol protocol) sent) {
            if (disposed) throw new ObjectDisposedException("Guest");
            Console.WriteLine(sent);
            if (sent != sequence[idx]) Dispose();
            idx++;
            if (idx != sequence.Length) return;
            Console.WriteLine("finish");
            finish(this);
            Dispose();
        }
        
        public void Dispose() {
            if (disposed) throw new ObjectDisposedException("Guest");
            Console.WriteLine("dispose");
            Console.WriteLine(ip);
            Console.WriteLine(string.Join(", ", sequence));
            disposed = true;
            cancel.Cancel();
            clean(this);
        }

        private async Task AwaitTimeout(int timeout) {
            cancel = new CancellationTokenSource();
            await Task.Delay(TimeSpan.FromSeconds(timeout), cancel.Token);
            Console.WriteLine("timeout");
            Dispose();
        }

        public Guest(IPAddress ip, (int port, Protocol protocol)[] sequence, int timeout, Action<Guest> onAuth, Action<Guest>? onDispose = null) {
            this.ip = ip;
            this.sequence = sequence;
            this.finish = onAuth;
            this.clean = onDispose;
            #pragma warning disable CS4014
            AwaitTimeout(timeout);
            #pragma warning restore CS4014
        }
    }

    private void RefreshSequence() {
        var cPeriod = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds() / interval;
        if (cPeriod == period) return;
        period = cPeriod;
        sequence = SequenceGen.GenPeriod(key, period, len);
        Console.WriteLine("refresh");
    }

    private async Task BackgroundRefresh() {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync()) {
            RefreshSequence();
        }
    }

    public void Ring(IPAddress ip, (int port, Protocol protocol) knock) {
        if (knock.port != doorbell) return;
        if (guests.ContainsKey(ip)) return;
        Console.WriteLine("doorbell");
        guests.Add(ip, new Guest(ip, sequence, timeout, g => {
            // TODO actually do the thing
            Console.WriteLine("You win! :)");
        }, g => guests.Remove(g.ip)));
    }
    
    public void Knock(IPAddress ip, (int port, Protocol protocol) knock) {
        if (!guests.ContainsKey(ip)) return;
        if (knock.port == doorbell) return;
        Console.WriteLine("knock");
        guests[ip].Knock(knock);
    }

    public Doorkeeper(string key, int interval, int len, int timeout, int doorbell) {
        this.key = key;
        this.interval = interval;
        this.period = 0;
        this.len = len;
        this.timeout = timeout;
        this.doorbell = doorbell;
        this.guests = new Dictionary<IPAddress, Guest>();
        RefreshSequence();
        #pragma warning disable CS4014
        BackgroundRefresh();
    }
}