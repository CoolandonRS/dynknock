﻿using System.Net;
using System.Text;
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
            ObjectDisposedException.ThrowIf(disposed, this);
            if (sent != sequence[idx]) {
                SwitchDebug(() => {
                    WriteDebug($"{hallwayName}: {ip} failed knock {idx}. Sent {sent}, expected {sequence[idx]}", ConsoleColor.Yellow);
                }, () => {
                    Fail(false);
                });
            } else {
                WriteDebug($"{hallwayName}: {ip} successfully knocked {idx} {sequence[idx]}");
            }
            if (Escort.advanceOnFail) {
                WriteDebug($"{hallwayName}: Advancing sequence for {ip}");
                idx++;
            }
            if (idx != sequence.Length) return;
            WriteEither($"{hallwayName}: {ip} successfully knocked", ConsoleColor.DarkGreen);
            WhenNotDebug(() => success(ip));
            Dispose();
        }
        
        public void Dispose() {
            ObjectDisposedException.ThrowIf(disposed, this);
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

    private int RefreshSequence() {
        var cPeriod = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds() / hallway.interval;
        if (cPeriod == period) {
            WriteDebug("Refresh attempted, unneeded", ConsoleColor.DarkGray);
            return period;
        }
        WriteDebug("Starting Refresh", ConsoleColor.DarkGray);
        period = cPeriod;
        sequence = SequenceGen.GenPeriod(SequenceGen.GetKey(hallway.key), period, hallway.length);
        WriteDebug("Refreshed");
        return period;
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
                // whenever doorbell is rung, attempt to regenerate. This could be behind guestPeriod > period to save even more computation but it's negligible and the code looks nicer this way.
                if (guestPeriod != RefreshSequence()) throw new Exception();
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
        this.period = -1;
        this.guests = new Dictionary<IPAddress, Guest>();
        // an initial sequence generation is unneeded since it is impossible to be in period -1, so when the doorbell is rung for the first time, it will regenerate.
    }
}