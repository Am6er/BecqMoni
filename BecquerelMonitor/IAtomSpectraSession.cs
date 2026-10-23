using System;

namespace BecquerelMonitor
{
    // What AtomSpectraTcpDeviceController needs from a transport: the TCP bridge (AtomSpectraTcpSession)
    // or the board's REST API (AtomSpectraRestSession).
    public interface IAtomSpectraSession : IDisposable
    {
        string TransportName { get; }           // "TCP" | "REST"
        string Host { get; }
        int Port { get; }
        bool IsConnected { get; }
        bool IsLinkLost { get; }
        bool IsOpen { get; }
        string LastError { get; }
        bool JoinedRunning { get; }             // Begin only joined an acquisition already running on the board
        double ElapsedAtBeginSeconds { get; }   // board acquisition time at Begin when joined, else 0
        AtomSpectraTcpBeginOutcome Begin(bool newDocument, int commandTimeoutMs);
        AtomSpectraTcpStopOutcome Stop(int commandTimeoutMs);
        void Clear(int commandTimeoutMs);
        void Detach();
        string Query(string command, int timeoutMs);
        event EventHandler<AtomSpectraTcpDataReadyArgs> DataReady;
        event EventHandler LinkLost;
    }
}
