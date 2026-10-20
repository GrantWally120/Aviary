using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aviary;

/// <summary>
/// The Experimental menu's parts that live in the window (Alpha Build 5 M6): "Turn every
/// experiment off", "Restart Aviary" under a switch that only applies at launch, and the question
/// after the crash protection (<see cref="SafetyMarker"/>) has switched the experiments off.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Settings → Experimental → "Turn every experiment off".</summary>
    private void TurnEveryExperimentOff()
    {
        List<string> were = Experiments.TurnAllOff();
        if (were.Count > 0)
        {
            SafetyMarker.ExperimentChanged();
        }

        DiagFeature("experiments-all-off", new { experiments = were });
        foreach (Experiments.Experiment e in Experiments.All)
        {
            PushToEverySettingsPage(e.Key, false);
        }

        ApplyExperimentsToOpenTabs();
    }

    /// <summary>
    /// The experiments that act on open tabs at once (no restart), after ANY way of changing them:
    /// the switch itself, "Turn All Off", and "Turn Them Back On". Cheap when nothing changed: a
    /// tab only talks to its engine when what it should show differs from what it last sent.
    /// </summary>
    private static void ApplyExperimentsToOpenTabs()
    {
        bool tint = AppSettings.GetBool("exp.tintToolbar");
        bool previews = AppSettings.GetBool("exp.tabPreview");
        foreach (MainWindow w in OpenWindows.ToList())
        {
            w.ReconcileReadyTabs();
            foreach (BrowserTab t in w._tabs)
            {
                t.RefreshDark();
                t.RefreshMute();

                // M6 stage 3: thin scroll bars (the adopted stylesheet), and each site's own zoom.
                _ = t.RefreshAidsAsync();
                t.ApplyZoom();

                // M6 stage 4: the fingerprint defences, from the next page this tab opens.
                _ = t.RefreshFingerprintAsync();

                // The tint: every open page asked for its colour when it is switched on.
                if (tint)
                {
                    _ = w.ReadTintAsync(t);
                }
            }

            w.ApplyTint();
            w.ApplyCloseOldTabs();
            if (!previews)
            {
                w.DropPreviews();
            }
        }

        _ = ApplyPerfOverlayAsync();

        // M6 stage 4: "Forget everything when Aviary closes" switched on means this session now has
        // something to forget, even if it ends badly; off means nothing is owed.
        ForgetEverything.MarkSession();
    }

    /// <summary>A change every open Settings page should draw, in every window.</summary>
    private static void PushToEverySettingsPage(string key, object value)
    {
        foreach (MainWindow w in OpenWindows.ToList())
        {
            w.PushToSettingsPage(key, value);
        }
    }

    /// <summary>
    /// "Restart Aviary": close every window and start again, so switches that only apply at launch
    /// take effect. Asked first, in plain words — Aviary cannot yet reopen the tabs you had open
    /// ("On startup: Continue where you left off" is a later build), so it says which things come
    /// back and which do not, and that any download still running stops.
    /// </summary>
    private async Task RestartAviaryAsync()
    {
        int downloads = OpenWindows.Sum(w => w.RunningDownloadsHere());
        string body = "Aviary closes and opens again, with your changes in place. Pinned tabs and tab groups "
                    + "are kept; other open tabs close.";
        if (downloads > 0)
        {
            body += downloads == 1
                ? " One download is still running, and it will stop."
                : " " + downloads + " downloads are still running, and they will stop.";
        }

        if (ForgetEverything.On)
        {
            body += " Forget everything when Aviary closes is on, so websites' data is cleared as it closes.";
        }

        if (!await ConfirmAsync("Restart Aviary?", body, "Restart"))
        {
            return;
        }

        DiagUi("restart", new { downloads });
        Program.RequestRelaunch();

        // A restart closes Aviary: "Forget everything when Aviary closes" forgets first, once, while every
        // window is still open - Window.Close() on its own would skip it (bug 96).
        if (OpenWindows.FirstOrDefault(w => !w._private) is { } main)
        {
            await main.ForgetEverythingIfDueAsync(everyWindow: true, when: "as Aviary restarted");
        }

        // Everything was asked above, so no window asks again on the way out.
        foreach (MainWindow w in OpenWindows.ToList())
        {
            w._closeConfirmed = true;
            w._forgotEverything = true;
            w.Close();
        }
    }

    /// <summary>
    /// After a problem, the launch switched every experiment off (App → SafetyMarker.AtLaunch);
    /// the first normal window says so, once, with the safe answer as the default.
    /// </summary>
    private async Task TellAboutExperimentsSwitchedOffAsync()
    {
        if (SafetyMarker.TakeSwitchedOff() is not { } off || _dialogOpen)
        {
            return;
        }

        string names = string.Join(Environment.NewLine, off.Keys
            .Select(k => k == Quik.Key ? "Quik (Beta)" : Experiments.Find(k)?.Name ?? k)
            .Select(n => "•  " + n));

        bool turnBackOn;
        _dialogOpen = true;
        try
        {
            ContentDialog dialog = new()
            {
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme,
                Title = "Aviary switched off experimental features",
                Content = new TextBlock
                {
                    Text = "Last time Aviary ran, " + off.Why + ", soon after experimental features were changed. "
                         + "To be safe, they are all off:" + Environment.NewLine + Environment.NewLine + names
                         + Environment.NewLine + Environment.NewLine
                         + "You can change these choices in Settings → Experimental or Settings → Quik.",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Keep Them Off",
                SecondaryButtonText = "Turn Them Back On",
                // Keeping them off is the safe answer, so it is the one Enter gives.
                DefaultButton = ContentDialogButton.Primary,
            };

            turnBackOn = await dialog.ShowAsync() == ContentDialogResult.Secondary;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "experiments: ask after a problem");
            return;
        }
        finally
        {
            _dialogOpen = false;
        }

        DiagFeature("experiments-after-problem", new { answer = turnBackOn ? "turn back on" : "keep off", experiments = off.Keys });
        if (!turnBackOn)
        {
            return;
        }

        foreach (string key in off.Keys)
        {
            AppSettings.Set(key, "true");
            PushToEverySettingsPage(key, true);
            if (key == Quik.Key)
            {
                Quik.Switched(true);
            }
        }

        SafetyMarker.ExperimentChanged();
        ApplyExperimentsToOpenTabs();

        // The ones that change the engine only apply at launch - and this launch is running without them.
        if (off.Keys.Any(k => k == Quik.Key || Experiments.Find(k)?.NeedsRestart == true))
        {
            await RestartAviaryAsync();
        }
    }
}
