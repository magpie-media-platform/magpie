using System;
using System.Net;
using System.Threading;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal sealed class TrackerSession
{
    private int _authenticated;
    private string? _clearanceUserAgent;
    private int _clearanceStamp;
    private long _tlsBindingExpiry;

    public bool Authenticated
    {
        get => Volatile.Read(ref _authenticated) != 0;
        set => Volatile.Write(ref _authenticated, value ? 1 : 0);
    }

    public string? ClearanceUserAgent
    {
        get => Volatile.Read(ref _clearanceUserAgent);
        set => Volatile.Write(ref _clearanceUserAgent, value);
    }

    public int ClearanceStamp
    {
        get => Volatile.Read(ref _clearanceStamp);
        set => Volatile.Write(ref _clearanceStamp, value);
    }

    public CookieContainer Cookies { get; set; } = new();

    public SemaphoreSlim ClearanceGate { get; } = new(1, 1);

    public bool ClearanceIsTlsBound(DateTimeOffset now) => now.UtcTicks < Volatile.Read(ref _tlsBindingExpiry);

    public void BindClearanceToTls(DateTimeOffset expiry) =>
        Volatile.Write(ref _tlsBindingExpiry, expiry.UtcTicks);

    public void ClearTlsBinding() => Volatile.Write(ref _tlsBindingExpiry, 0);
}
