using System;
using System.Threading.Tasks;

namespace Aviary;

/// <summary>Owns one document-created script on the UI thread, including out-of-order async registrations.</summary>
internal sealed class DocumentScriptSlot
{
    private string? _requested;
    private string? _id;
    private long _generation;
    private bool _closed;
    internal bool Requested => !_closed && _requested is not null;

    internal async Task<bool> SetAsync(string? script, Func<string, Task<string>> add, Action<string> remove)
    {
        if (_closed || string.Equals(script, _requested, StringComparison.Ordinal)) { return false; }
        _requested = script;
        long generation = ++_generation;
        RemoveCurrent(remove);
        if (script is null) { return false; }
        try
        {
            string id = await add(script);
            if (_closed || generation != _generation)
            {
                remove(id);
                return false;
            }
            _id = id;
            return true;
        }
        catch
        {
            if (generation == _generation) { _requested = null; }
            throw;
        }
    }

    internal void Close(Action<string> remove)
    {
        _closed = true;
        _requested = null;
        ++_generation;
        RemoveCurrent(remove);
    }

    private void RemoveCurrent(Action<string> remove)
    {
        string? old = _id;
        _id = null;
        if (old is not null) { remove(old); }
    }
}
