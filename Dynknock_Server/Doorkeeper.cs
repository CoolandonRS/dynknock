using System.Net;
using System.Text;

namespace Dynknock_Server;

public class Doorkeeper {
    private (int port, Protocol protocol)[] sequence;
    private Hallway hallway;
    private int period;
    private Dictionary<IPAddress, Guest> guests;
    private readonly string hallwayName;

    private class Guest {
        public IPAddress ip { get; private set; }
        private int idx = 0;
        private (int port, Protocol protocol)[] sequence;
        private Action<IPAddress> success;
        private Action<IPAddress> failure;
        private bool disposed = false;
        private Action<Guest>? clean;
        private CancellationTokenSource? cancel;
        private readonly string hallwayName;

        public void Knock((int port, Protocol protocol) sent) {
            if (disposed) throw new ObjectDisposedException("Guest");
            if (sent != sequence[idx]) { Fail(false); return; }
            idx++;
            if (idx != sequence.Length) return;
            if (Escort.verbose) Console.WriteLine($"{hallwayName}: {ip} successfully knocked");
            success(ip);
            Dispose();
        }
        
        public void Dispose() {
            if (disposed) throw new ObjectDisposedException("Guest");
            disposed = true;
            cancel?.Cancel();
            clean?.Invoke(this);
        }

        private void Fail(bool timeout) {
            if (Escort.verbose) Console.WriteLine($"{hallwayName}: {ip} {(timeout ? "timed out" : $"failed knock {idx}")}");
            this.failure(ip);
            Dispose();
        }

        private async Task AwaitTimeout(int timeout) {
            cancel = new CancellationTokenSource();
            await Task.Delay(TimeSpan.FromSeconds(timeout), cancel.Token);
            if (!disposed && !cancel.IsCancellationRequested) Fail(true);
        }

        public Guest(IPAddress ip, (int port, Protocol protocol)[] sequence, Hallway hallway, string hallwayName, Action<Guest>? onDispose = null) {
            this.ip = ip;
            this.sequence = sequence;
            this.clean = onDispose;
            this.success = hallway.Open;
            this.failure = hallway.Banish;
            this.hallwayName = hallwayName;
            #pragma warning disable CS4014
            AwaitTimeout(hallway.timeout);
            #pragma warning restore CS4014
        }
    }

    private bool RefreshSequence() {
        var cPeriod = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds() / hallway.interval;
        if (cPeriod == period) return false;
        period = cPeriod;
        sequence = SequenceGen.GenPeriod(SequenceGen.GetKey(hallway.key), period, hallway.length);
        hallway.Update();
        return true;
    }

    // TODO consider using entirely passive refresh, where sequence is only updated on knock attempt. The main argument to not use this is the updateCommand in hallway becoming arguably less useful.
    private async Task BackgroundRefresh() {
        // Await the amount of time until the next refresh occurs (plus a lil extra) to sync ourselves
        await Task.Delay(TimeSpan.FromSeconds((int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % hallway.interval) + 1));
        RefreshSequence();
        // Now we can periodically await the length of interval 
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(hallway.interval));
        while (await timer.WaitForNextTickAsync()) {
            RefreshSequence();
        }
    }

    public void Ring(IPAddress ip, int port, byte[] data) {
        if (port != hallway.doorbell) return;
        try {
            var datStr = Encoding.UTF8.GetString(data);
            if (datStr[..8] != "DOORBELL") return;
            var guestPeriod = int.Parse(datStr[8..]);
            // no exceptions go on here except the ones I throw. Easier this way.
            try {
                if (guestPeriod != period) {
                    // Just in case we still have desync (probably about a one second window) attempt to refresh if the guest's period is one ahead of hours.
                    if (guestPeriod == period + 1) {
                        if (!RefreshSequence()) throw new Exception();
                    } else throw new Exception();
                }
            } catch {
                if (Escort.verbose) Console.WriteLine($"{hallwayName}: {ip} failed to ring doorbell");
                hallway.Banish(ip);
                return;
            }
        } catch { return; }
        if (Escort.verbose) Console.WriteLine($"{hallwayName}: {ip} rung doorbell");
        guests.Add(ip, new Guest(ip, sequence, hallway, hallwayName, g => guests.Remove(g.ip)));
    }

    public bool Registered(IPAddress ip) => guests.ContainsKey(ip);
    
    public void Knock(IPAddress ip, (int port, Protocol protocol) knock) {
        guests[ip].Knock(knock);
    }

    public Doorkeeper(Hallway hallway, string hallwayName) {
        this.hallway = hallway;
        this.hallwayName = hallwayName;
        this.period = 0;
        this.guests = new Dictionary<IPAddress, Guest>();
        RefreshSequence();
        #pragma warning disable CS4014
        BackgroundRefresh();
    }
}