using System;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Aviary;

/// <summary>
/// Beta Prep 2 — the "I agree" screen (Grant, 21 Nov 2026: Aviary's Terms of Use must be agreed to
/// before anyone browses, and they are also always in Settings → Legal).
///
/// <para>Shown when a window finishes loading and this profile has not agreed to the CURRENT
/// revision of the Terms (<see cref="TermsGate"/>) — so on the first launch, and again whenever the
/// Terms change. It comes up before the window makes its first tab, and
/// <see cref="CreateTabAsync"/> refuses to make one until it is answered, so no page exists behind
/// it and no keyboard shortcut can open one. "I agree" writes down the revision and the day in
/// settings.json; "Quit Aviary" closes the window. Esc is not an answer: the screen comes back.</para>
///
/// <para>Everything that is agreed to can be read ON the screen, offline, before agreeing: the
/// short version, and under it the whole Terms of Use and the whole Privacy Policy, each folded
/// until pressed. All of it is plain text from <see cref="LegalTexts"/> and
/// <see cref="PrivacyPolicy"/> — the same words Settings shows.</para>
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>True once the Terms are agreed to; false when the person chose to quit instead.</summary>
    private async Task<bool> EnsureTermsAcceptedAsync()
    {
        if (TermsGate.Accepted)
        {
            return true;
        }

        Diagnostics.Mark("terms-asked");

        for (int attempt = 0; attempt < 50; attempt++)
        {
            // Another window of this launch may have been answered while this one waited.
            if (TermsGate.Accepted)
            {
                return true;
            }

            ContentDialogResult? answer = await ShowTermsDialogAsync();
            if (answer == ContentDialogResult.Primary)
            {
                TermsGate.Accept();
                Diagnostics.Ui("terms-agreed", new { revision = LegalTexts.TermsRevision });
                return true;
            }

            if (answer is null or ContentDialogResult.Secondary)
            {
                // "Quit Aviary" - or the screen could not be shown at all, and a browser that cannot
                // show its Terms does not carry on as if they had been agreed to.
                return false;
            }

            // Esc, or anything else that dismissed it: not an answer. Ask again.
        }

        return false;
    }

    private async Task<ContentDialogResult?> ShowTermsDialogAsync()
    {
        if (_dialogOpen || RootGrid.XamlRoot is null)
        {
            return null;
        }

        _dialogOpen = true;
        try
        {
            StackPanel body = new() { Spacing = 10 };
            body.Children.Add(LegalText("Before you start, please read and agree to Aviary’s Terms of Use and Privacy Policy."));
            body.Children.Add(LegalText("The short version:", bold: true));
            foreach (string line in LegalTexts.ShortVersion)
            {
                body.Children.Add(LegalText("•  " + line));
            }

            body.Children.Add(LegalFold("Read the Terms of Use", "TermsOfUseFold",
                LegalTexts.Terms.revision, LegalTexts.Terms.summary, LegalTexts.Terms.sections));

            LegalTexts.Section[] policy = Array.ConvertAll(PrivacyPolicy.Sections,
                s => new LegalTexts.Section(s.title, s.paragraphs));
            body.Children.Add(LegalFold("Read the Privacy Policy", "PrivacyPolicyFold",
                PrivacyPolicy.Revision, PrivacyPolicy.Summary, policy));

            body.Children.Add(LegalText(
                "Pressing “I agree” saves that you agreed, which revision, and today’s date, in Aviary’s settings on this computer. Nothing is sent anywhere.",
                quiet: true));

            ContentDialog dialog = new()
            {
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme,
                Title = "Welcome to Aviary",
                Content = new ScrollViewer
                {
                    Content = body,
                    MaxHeight = 460,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(0, 0, 14, 0),
                },
                PrimaryButtonText = "I agree",
                SecondaryButtonText = "Quit Aviary",
                // Neither is the Enter key's: agreeing is a deliberate press, and so is leaving.
                DefaultButton = ContentDialogButton.None,
            };

            // Wide enough to read a page of text in; the default is a narrow question box.
            dialog.Resources["ContentDialogMaxWidth"] = 640d;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(dialog, "TermsDialog");

            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "terms: show the I agree screen");
            return null;
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private static TextBlock LegalText(string text, bool bold = false, bool quiet = false) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Opacity = quiet ? 0.7 : 1,
        FontSize = quiet ? 12.5 : 14,
    };

    /// <summary>One whole document, folded until pressed, in the words Settings → Legal shows.</summary>
    private static Expander LegalFold(string header, string automationId, string revision, string summary, LegalTexts.Section[] sections)
    {
        StackPanel text = new() { Spacing = 8 };
        text.Children.Add(LegalText("Revision: " + revision, quiet: true));
        text.Children.Add(LegalText(summary));
        foreach (LegalTexts.Section section in sections)
        {
            TextBlock title = LegalText(section.title, bold: true);
            title.Margin = new Thickness(0, 8, 0, 0);
            text.Children.Add(title);
            foreach (string paragraph in section.paragraphs)
            {
                text.Children.Add(LegalText(paragraph));
            }
        }

        Expander fold = new()
        {
            Header = header,
            Content = text,
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(fold, automationId);
        return fold;
    }
}
