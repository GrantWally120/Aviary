using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Aviary;

/// <summary>
/// Search suggestions from the engine (Phase 5 M4): the "Search suggestions from the engine" row
/// in Settings → Search engine, made real. Off by default, and the row says why in plain words —
/// it sends what you type in the address bar to the engine as you type it.
///
/// The rules, each one a promise the Settings row makes:
///   - Only to the engine you chose, at the address it publishes (SearchEngines.cs), and only
///     while the switch is on. Changing engine switches it OFF: agreeing to send your typing to
///     one company is not agreeing to send it to the next (SearchEngineChanged).
///   - Never from a private window, and never while the engine has no suggestion service (Mojeek).
///   - Never an address: anything that looks like a web address, a host:port, a path or a scheme
///     stays on the machine (SearchSuggestions.MaySend).
///   - No cookies either way, no Referer, no redirects followed — an engine cannot tie one
///     keystroke to your next visit, or bounce the request somewhere else. Global Privacy
///     Control goes with it when that switch is on, as it does with every page.
///   - A short pause after each keystroke first (SuggestDelayMs): a word typed quickly goes as
///     one request, not one per letter.
///
/// The engine's answer is untrusted text from the network. It is shown as plain text in a
/// TextBlock, trimmed, stripped of control characters and capped in length and number; choosing
/// one only ever SEARCHES for it with the same engine — a suggestion that looks like an address
/// is still searched for, never opened.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How long typing has to pause before the words are sent.</summary>
    private const int SuggestDelayMs = 120;

    /// <summary>Your own matches are cut to this many while the engine's are shown, so the list stays one screen tall.</summary>
    private const int LocalLimitWithEngine = 5;

    /// <summary>One client for every window: no cookies, no redirects, no Referer (SearchSuggestions.CreateClient).</summary>
    private static readonly HttpClient SuggestHttp = SearchSuggestions.CreateClient(BrowserTab.SafariUserAgent);

    private CancellationTokenSource? _engineFetch;

    /// <summary>The words a request is out for, so the same words are not asked for twice.</summary>
    private string _engineAsking = string.Empty;

    /// <summary>
    /// The engine's last answer and the words it answered. Kept while what is in the box still
    /// starts with those words, so the list does not empty and refill on every keystroke while
    /// the next answer is on its way.
    /// </summary>
    private string _engineQuery = string.Empty;
    private string _engineName = string.Empty;
    private List<string> _engineSuggestions = new();

    /// <summary>The switch is on, this window may send, and the engine can answer.</summary>
    private bool EngineSuggestionsOn =>
        !_private && AppSettings.GetBool("suggest") && SearchEngines.Current.Suggests;

    /// <summary>
    /// What the engine has suggested for these words, from its last answer: all of it when that
    /// answer was for exactly these words, otherwise only the entries that still start with them.
    /// </summary>
    private List<string> EngineSuggestionsFor(string query)
    {
        List<string> shown = new();

        if (!EngineSuggestionsOn
            || _engineSuggestions.Count == 0
            || !string.Equals(_engineName, SearchEngines.Current.Name, StringComparison.Ordinal)
            || !query.StartsWith(_engineQuery, StringComparison.OrdinalIgnoreCase))
        {
            return shown;
        }

        bool exact = string.Equals(query, _engineQuery, StringComparison.OrdinalIgnoreCase);
        foreach (string s in _engineSuggestions)
        {
            if (string.Equals(s, query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (exact || s.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                shown.Add(s);
            }

            if (shown.Count == SearchSuggestions.MaxShown)
            {
                break;
            }
        }

        return shown;
    }

    /// <summary>Asks the engine about these words, after a pause, unless it has already answered or is being asked.</summary>
    private void RequestEngineSuggestions(string query)
    {
        if (!EngineSuggestionsOn || !SearchSuggestions.MaySend(query))
        {
            CancelEngineSuggestions();
            return;
        }

        SearchEngines.Engine engine = SearchEngines.Current;

        if (string.Equals(query, _engineAsking, StringComparison.Ordinal)
            || (string.Equals(query, _engineQuery, StringComparison.Ordinal)
                && string.Equals(engine.Name, _engineName, StringComparison.Ordinal)))
        {
            return;
        }

        // Cancelled, not disposed: the request it belongs to may still be reading the token.
        _engineFetch?.Cancel();
        _engineFetch = new CancellationTokenSource();
        _engineAsking = query;

        _ = FetchEngineSuggestionsAsync(query, engine, _engineFetch.Token);
    }

    /// <summary>The list closed or the typing changed course: whatever is on its way is not wanted.</summary>
    private void CancelEngineSuggestions()
    {
        _engineFetch?.Cancel();
        _engineFetch = null;
        _engineAsking = string.Empty;
    }

    /// <summary>A different engine, or the switch went off: nothing it said before is shown again.</summary>
    private void ForgetEngineSuggestions()
    {
        CancelEngineSuggestions();
        _engineQuery = string.Empty;
        _engineName = string.Empty;
        _engineSuggestions = new List<string>();
    }

    private async Task FetchEngineSuggestionsAsync(string query, SearchEngines.Engine engine, CancellationToken cancel)
    {
        string url = engine.SuggestUrl + Uri.EscapeDataString(query);
        Stopwatch clock = new();

        try
        {
            // Still typing: this is cancelled by the next keystroke, and nothing is sent.
            await Task.Delay(SuggestDelayMs, cancel);

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            if (AppSettings.GetBool("gpc"))
            {
                request.Headers.TryAddWithoutValidation("Sec-GPC", "1");
            }

            clock.Start();
            using HttpResponseMessage response = await SuggestHttp.SendAsync(request, cancel);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancel);
            clock.Stop();

            // Category 3: a request the browser made, like any page's — the words are in its address.
            Diagnostics.Request(new NetRecord
            {
                Tab = _active?.Id ?? 0,
                Url = url,
                Method = "GET",
                Context = "Suggestions",
                Status = (int)response.StatusCode,
                Type = "search suggestions",
                Bytes = body.Length,
                Ms = Math.Round(clock.Elapsed.TotalMilliseconds, 1),
                Initiator = "address bar",
            });

            if (!response.IsSuccessStatusCode || cancel.IsCancellationRequested)
            {
                return;
            }

            _engineQuery = query;
            _engineName = engine.Name;
            _engineSuggestions = SearchSuggestions.Parse(body, query);

            // Only if these are still the words in the box, and nobody has started walking the list
            // with the arrow keys — redrawing would move the row they are on. An answer that is not
            // drawn now is kept (above) for the next keystroke.
            if (_addressFocused && _suggestIndex < 0
                && string.Equals(AddressBox.Text.Trim(), query, StringComparison.Ordinal))
            {
                ShowSuggestions(AddressBox.Text);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // Typing moved on, or the list closed. Nothing to report.
        }
        catch (OperationCanceledException)
        {
            // HttpClient's own time limit: the engine did not answer in four seconds.
            Diagnostics.Anomaly("suggest-timeout", engine.Name + " took over 4 s to suggest",
                new { engine = engine.Name, ms = Math.Round(clock.Elapsed.TotalMilliseconds) });
        }
        catch (Exception ex)
        {
            // Offline, a refused connection, an answer too big or not JSON: the list simply has
            // no suggestions from the engine this time. Your own still show.
            Diagnostics.Swallowed(ex, "search suggestions: " + engine.Name);
        }
        finally
        {
            if (string.Equals(_engineAsking, query, StringComparison.Ordinal) && !cancel.IsCancellationRequested)
            {
                _engineAsking = string.Empty;
            }
        }
    }

    /// <summary>
    /// The engine changed in Settings. Suggestions are agreed to one engine at a time, so if they
    /// were on they go off, and every open Settings page is told — the new engine hears nothing as
    /// you type until the switch is turned on again for it.
    /// </summary>
    private void SearchEngineChanged(string? name)
    {
        foreach (MainWindow window in OpenWindows)
        {
            window.ForgetEngineSuggestions();
        }

        if (!AppSettings.GetBool("suggest"))
        {
            return;
        }

        AppSettings.Set("suggest", "false");
        Diagnostics.Ui("suggest-off-for-new-engine", new { engine = SearchEngines.Get(name).Name });

        foreach (MainWindow window in OpenWindows)
        {
            window.PushToSettingsPage("suggest", false);
        }
    }
}
