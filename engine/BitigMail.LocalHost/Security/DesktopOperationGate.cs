namespace BitigMail.LocalHost.Security;

/// <summary>Stops new HTTP operations while existing archive operations drain.</summary>
public sealed class DesktopOperationGate
{
    private readonly object _gate = new();
    private bool _draining;
    private int _active;
    public int ActiveCount { get { lock (_gate) return _active; } }
    public bool IsDrained { get { lock (_gate) return _draining && _active == 0; } }
    public void BeginDrain() { lock (_gate) _draining = true; }

    public IDisposable? TryEnter()
    {
        lock (_gate)
        {
            if (_draining) return null;
            _active++;
            return new Lease(this);
        }
    }

    private void Leave() { lock (_gate) _active--; }
    private sealed class Lease(DesktopOperationGate owner) : IDisposable
    {
        private DesktopOperationGate? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Leave();
    }
}

public sealed class DesktopOperationGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, DesktopOperationGate gate)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        { await next(context); return; }
        using var lease = gate.TryEnter();
        if (lease is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Uygulama güvenli kapanış için yeni işlemleri durdurdu." });
            return;
        }
        await next(context);
    }
}
