using System.Net;
using System.Text;
using PacketDotNet.DhcpV4;
using static Dynknock_Server.Escort.VerbosityUtil;

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
            if (sent != sequence[idx]) {
                SwitchDebug(() => {
                    WriteDebug($"{hallwayName}: {ip} failed knock {idx}. Sent {sent.port}, expected {sequence[idx]}", ConsoleColor.Yellow);
                }, () => {
                    Fail(false);
                });
            }
            if (!Escort.advanceOnFail) {
                WriteDebug($"{hallwayName}: Advancing sequence for {ip}");
                idx++;
            }
            if (idx != sequence.Length) return;
            WriteEither($"{hallwayName}: {ip} successfully knocked", ConsoleColor.DarkGreen);
            WhenNotDebug(() => success(ip));
            Dispose();
        }
        
        public void Dispose() {
            if (disposed) throw new ObjectDisposedException("Guest");
            WriteDebug($"{hallwayName}: Disposed guest {ip}");
            disposed = true;
            cancel?.Cancel();
            clean?.Invoke(this);
        }

        private void Fail(bool timeout) {
            WriteVerbose($"{hallwayName}: {ip} {(timeout ? "timed out" : $"failed knock {idx}")}", ConsoleColor.Red);
            SwitchDebug(() => {
                // dont print non-timeout cases as those are handled in their respective areas before Fail() is called.
                if (timeout) WriteDebug($"{hallwayName}: {ip} just timed out.", ConsoleColor.Yellow);
            }, () => {
                failure(ip);
                Dispose(); 
            });
        }

        private async Task DelayCall(Action callback,int timeout) {
            cancel = new CancellationTokenSource();
            await Task.Delay(TimeSpan.FromSeconds(timeout), cancel.Token);
            if (!disposed && !cancel.IsCancellationRequested) callback();
        }

        public void Advance() => idx++;

        public Guest(IPAddress ip, (int port, Protocol protocol)[] sequence, Hallway hallway, string hallwayName, Action<Guest>? onDispose = null) {
            this.ip = ip;
            this.sequence = sequence;
            this.clean = onDispose;
            this.success = hallway.Open;
            this.failure = hallway.Banish;
            this.hallwayName = hallwayName;
            #pragma warning disable CS4014
            DelayCall(() => Fail(true), hallway.timeout);
            WhenDebug(() => DelayCall(Dispose, 120));
            #pragma warning restore CS4014
        }
    }

    private bool RefreshSequence() {
        var cPeriod = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds() / hallway.interval;
        if (cPeriod == period) {
            WriteDebug("Refresh attempted, unneeded", ConsoleColor.Gray);
            return false;
        }
        WriteDebug("Starting Refresh", ConsoleColor.Gray);
        period = cPeriod;
        sequence = SequenceGen.GenPeriod(SequenceGen.GetKey(hallway.key), period, hallway.length);
        WriteDebug("Refreshed");
        WhenNotDebug(() => hallway.Update());
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
            WriteDebug("Timer requesting refresh");
            RefreshSequence();
        }
    }

    public void Ring(IPAddress ip, int port, byte[] data) {
        if (port != hallway.doorbell) return;
        try {
            var datStr = Encoding.UTF8.GetString(data);
            switch (datStr[..8]) {
                case "DOORBELL": break;
                case "ADVANCE_" when Escort.debug && Registered(ip):
                    guests[ip].Advance();
                    return;
                case "ENDKNOCK" when Escort.debug && Registered(ip):
                    guests[ip].Dispose();
                    return;
                default:
                    return;
            }
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
                WriteError($"{hallwayName}: {ip} failed to ring doorbell", $"{hallway}: {ip} failed to ring doorbell, sent {guestPeriod}, period was evaluated to {period}");
                WhenNotDebug(() => hallway.Banish(ip));
                return;
            }
        } catch { return; }
        WriteEither($"{hallwayName}: {ip} rung doorbell", ConsoleColor.Blue);
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