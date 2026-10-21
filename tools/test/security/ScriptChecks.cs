using Aviary;

internal static class ScriptChecks
{
    internal static async Task RunAsync(Action<string, bool> check)
    {
        foreach (bool newerFirst in new[] { false, true })
        {
            DocumentScriptSlot slot = new();
            List<TaskCompletionSource<string>> pending = new();
            HashSet<string> active = new();
            Task<string> Add(string script)
            {
                TaskCompletionSource<string> source = new(); pending.Add(source); return source.Task;
            }
            void Complete(int index) { active.Add(index.ToString()); pending[index].SetResult(index.ToString()); }
            void Remove(string id) { active.Remove(id); }
            var first = slot.SetAsync("diagnostics", Add, Remove);
            await slot.SetAsync(null, Add, Remove);
            var second = slot.SetAsync("diagnostics", Add, Remove);
            Complete(newerFirst ? 1 : 0);
            Complete(newerFirst ? 0 : 1);
            await Task.WhenAll(first, second);
            check("On/off/on owns only latest script, newer completion first=" + newerFirst, active.SetEquals(new[] { "1" }));
            await slot.SetAsync(null, Add, Remove);
            check("Off removes every registered script, newer completion first=" + newerFirst, active.Count == 0 && !slot.Requested);
        }
        DocumentScriptSlot closing = new();
        TaskCompletionSource<string> late = new();
        HashSet<string> remaining = new();
        var registration = closing.SetAsync("diagnostics", _ => late.Task, id => remaining.Remove(id));
        closing.Close(id => remaining.Remove(id));
        remaining.Add("late"); late.SetResult("late"); await registration;
        check("Close invalidates an in-flight script registration", remaining.Count == 0 && !closing.Requested);
        check("Closed tab cannot register another script", !await closing.SetAsync("new", _ => throw new Exception("should not run"), _ => { }));
        DocumentScriptSlot retry = new();
        try { await retry.SetAsync("script", _ => Task.FromException<string>(new InvalidOperationException("synthetic registration failure")), _ => { }); }
        catch (InvalidOperationException) { }
        check("Failed registration can retry the same script", await retry.SetAsync("script", _ => Task.FromResult("retry"), _ => { }));
    }
}
