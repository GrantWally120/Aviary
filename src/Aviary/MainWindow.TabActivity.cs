using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Aviary;

public sealed partial class MainWindow
{
    private Button BuildTabAudioButton(BrowserTab tab)
    {
        Button audio = new()
        {
            Width = 16, Height = 20, MinWidth = 0, MinHeight = 0,
            Padding = new Thickness(0), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Visibility = Visibility.Collapsed,
        };
        audio.Click += (_, e) =>
        {
            tab.SetUserMuted(!tab.UserMuted);
            DiagFeature("tab-mute", new { tab = tab.Id, muted = tab.UserMuted });
        };
        return audio;
    }

    private void RefreshTabActivity(TabChrome chrome)
    {
        BrowserTab tab = chrome.Tab;
        chrome.Icon.SetLoading(tab.IsLoading);
        if (chrome.Audio is not Button audio) { return; }
        bool visible = tab.IsPlayingAudio || tab.UserMuted;
        audio.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (chrome.AudioOverIcon) { chrome.Icon.Visibility = visible ? Visibility.Collapsed : Visibility.Visible; }
        if (!visible) { return; }
        string name = tab.UserMuted ? "Unmute Tab" : tab.IsMuted ? "Keep Tab Muted" : "Mute Tab";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(audio, name);
        ToolTipService.SetToolTip(audio, tab.IsMuted && !tab.UserMuted
            ? "Muted by the background setting. Click to keep this tab muted." : name);
        audio.Foreground = ThemeBrush("AvIconBrush");
        string glyph = tab.IsMuted ? "\uE74F" : "\uE767";
        if (audio.Content is FontIcon icon) { icon.Glyph = glyph; }
        else { audio.Content = new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Glyph = glyph }; }
    }

    private string TabAccessibleName(BrowserTab tab) => ShownTitle(tab)
        + (tab.IsLoading ? ", loading" : string.Empty)
        + (tab.IsMuted ? ", muted" : tab.IsPlayingAudio ? ", playing audio" : string.Empty);

    private static bool IsTabAudioTarget(object source, TabChrome chrome)
    {
        if (chrome.Audio is null) { return false; }
        for (DependencyObject? part = source as DependencyObject; part is not null; part = VisualTreeHelper.GetParent(part))
        {
            if (ReferenceEquals(part, chrome.Audio)) { return true; }
        }
        return false;
    }
}
