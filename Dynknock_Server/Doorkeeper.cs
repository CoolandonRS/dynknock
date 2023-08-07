using System.Net;

namespace Dynknock_Server;

public class Doorkeeper {
    private (int port, Protocol protocol)[] sequence;
    private Hallway hallway;
    private int period;
    private Dictionary<IPAddress, Guest> guests;

    private class Guest {
        public IPAddress ip { get; private set; }
        private int idx = 0;
        private (int port, Protocol protocol)[] sequence;
        private Action<IPAddress> finish;
        private bool disposed = false;
        private Action<Guest>? clean;
        private CancellationTokenSource? cancel;
        
        public void Knock((int port, Protocol protocol) sent) {
            if (disposed) throw new ObjectDisposedException("Guest");
            if (sent != sequence[idx]) Dispose();
            idx++;
            if (idx != sequence.Length) return;
            finish(ip);
            Dispose();
            
        }
        
        public void Dispose() {
            if (disposed) throw new ObjectDisposedException("Guest");
            disposed = true;
            cancel?.Cancel();
            clean?.Invoke(this);
        }

        private async Task AwaitTimeout(int timeout) {
            cancel = new CancellationTokenSource();
            await Task.Delay(TimeSpan.FromSeconds(timeout), cancel.Token);
            Dispose();
        }

        public Guest(IPAddress ip, (int port, Protocol protocol)[] sequence, Hallway hallway, Action<Guest>? onDispose = null) {
            this.ip = ip;
            this.sequence = sequence;
            this.clean = onDispose;
            this.finish = hallway.Open;
            #pragma warning disable CS4014
            AwaitTimeout(hallway.timeout);
            #pragma warning restore CS4014
        }
    }

    private void RefreshSequence() {
        var cPeriod = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds() / hallway.interval;
        if (cPeriod == period) return;
        period = cPeriod;
        sequence = SequenceGen.GenPeriod(SequenceGen.GetKey(hallway.key), period, hallway.length);
        hallway.Update();
    }

    private async Task BackgroundRefresh() {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync()) {
            RefreshSequence();
        }
    }

    public void Ring(IPAddress ip, (int port, Protocol protocol) knock) {
        if (knock.port != hallway.doorbell) return;
        if (guests.ContainsKey(ip)) return;
        guests.Add(ip, new Guest(ip, sequence, hallway, g => guests.Remove(g.ip)));
    }
    
    public void Knock(IPAddress ip, (int port, Protocol protocol) knock) {
        if (!guests.ContainsKey(ip)) return;
        if (knock.port == hallway.doorbell) return;
        guests[ip].Knock(knock);
    }

    public Doorkeeper(Hallway hallway) {
        this.hallway = hallway;
        this.period = 0;
        this.guests = new Dictionary<IPAddress, Guest>();
        RefreshSequence();
        #pragma warning disable CS4014
        BackgroundRefresh();
    }
}