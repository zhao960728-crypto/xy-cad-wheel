using System;

namespace XiaoYang.AutoCADPlugin;

// No host API calls until a ready idle notification. Readiness and startup can
// pump nested messages, so both must be protected against reentry/cancellation.
internal sealed class LegacyDeferredStartup
{
    private readonly Func<bool> _isReady;
    private readonly Action _start;
    private bool _finished;
    private bool _running;

    internal LegacyDeferredStartup(Func<bool> isReady, Action start)
    {
        _isReady = isReady;
        _start = start;
    }

    internal bool Tick()
    {
        if (_finished) return true;
        if (_running) return false;
        _running = true;
        try
        {
            if (!_isReady()) return _finished;
            if (_finished) return true;
            _finished = true;
            _start();
            return true;
        }
        finally { _running = false; }
    }

    internal void Cancel() => _finished = true;
}
