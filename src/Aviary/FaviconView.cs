using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Aviary;

/// <summary>
/// The one 16px cell that says which site this is — used by the tab strip, the favourites
/// bar, and later the sidebar and history.
///
/// Four identity layers stacked in the same box, cross-fading between them:
///   slug      a faint rounded placeholder, shown only when there is genuinely nothing yet
///   monogram  the coloured chip and letter, when a site has no icon
///   dome      Aviary's own mark, for Aviary's own pages
///   image     the real favicon
///
/// Safari does not spin here and it does not blank on navigation: it keeps showing the
/// previous icon until the new one resolves, then cross-fades. This does the same — the
/// slug only appears when the incoming key has nothing cached AND the outgoing key was
/// different, so a reload never flickers.
///
/// Not hit-testable. In WinUI an element at Opacity 0 is still hit-testable (unlike
/// Visibility.Collapsed), so if this could take the pointer it would steal the press from
/// the close button that cross-fades on top of it.
/// </summary>
internal sealed class FaviconView : Grid
{
    private static readonly List<WeakReference<FaviconView>> Live = new();

    private readonly double _size;
    private readonly ResourceDictionary _styles;

    private readonly Border _slug;
    private readonly Border _chip;
    private readonly TextBlock _letter;
    private readonly Microsoft.UI.Xaml.Shapes.Path _dome;
    private readonly Image _image;

    private readonly Action<string> _onUpdated;
    private bool _subscribed;

    public string Key { get; private set; } = string.Empty;

    public FaviconView(double logicalSize, ResourceDictionary styles)
    {
        _size = logicalSize;
        _styles = styles;

        Width = logicalSize;
        Height = logicalSize;

        // See the class comment: opacity-0 is still hit-testable in WinUI.
        IsHitTestVisible = false;

        _slug = new Border { Opacity = 0 };
        TryStyle(_slug, "AvFaviconSlugStyle");

        _chip = new Border { Opacity = 0 };
        _letter = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        TryStyle(_letter, "AvMonogramInkStyle");
        _chip.Child = _letter;

        // The dome mark, minus the interior bars: at 16px three 0.9px verticals alias into
        // a smear rather than reading as a glasshouse.
        _dome = new Microsoft.UI.Xaml.Shapes.Path
        {
            Opacity = 0,
            Stretch = Stretch.Uniform,
            Width = logicalSize,
            Height = logicalSize,
            Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                typeof(Geometry), "M3,20 V12.6 A9,9 0 0 1 21,12.6 V20 M2.2,20 H21.8"),
        };
        TryStyle(_dome, "AvFaviconMarkStyle");

        _image = new Image
        {
            Opacity = 0,
            Stretch = Stretch.Uniform,
            Width = logicalSize,
            Height = logicalSize,
        };

        Children.Add(_slug);
        Children.Add(_chip);
        Children.Add(_dome);
        Children.Add(_image);

        ApplyLookGeometry(IsGlass());

        // Held as a field so the -= in Unloaded actually matches. A per-element lambda on a
        // static event pins every cell ever created for the life of the process.
        _onUpdated = OnIconUpdated;

        Loaded += (_, _) =>
        {
            if (!_subscribed)
            {
                Favicons.Updated += _onUpdated;
                _subscribed = true;
            }

            Refresh(animate: false);
        };

        Unloaded += (_, _) =>
        {
            if (_subscribed)
            {
                Favicons.Updated -= _onUpdated;
                _subscribed = false;
            }
        };

        lock (Live) { Live.Add(new WeakReference<FaviconView>(this)); }
    }

    private void TryStyle(FrameworkElement element, string key)
    {
        try
        {
            if (_styles.TryGetValue(key, out object? style) && style is Style s)
            {
                element.Style = s;
            }
        }
        catch (Exception ex)
        {
            // A missing style leaves the element unpainted rather than crashing the tab.
            Diagnostics.Swallowed(ex, "favicon view: apply style " + key);
        }
    }

    private static bool IsGlass() =>
        AppSettings.Get("look").Equals("Liquid Glass", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Points the cell at a URL.
    /// </summary>
    /// <param name="monogramOverride">The letter to fall back to; null derives one.</param>
    /// <param name="internalPage">True for Aviary's own pages, which show the dome.</param>
    public void SetSource(string url, string? monogramOverride, bool internalPage)
    {
        string incoming = internalPage ? string.Empty : Favicons.KeyFor(url);
        bool keyChanged = !string.Equals(incoming, Key, StringComparison.OrdinalIgnoreCase);
        Key = incoming;
        _isInternal = internalPage;

        _monogram = monogramOverride is { Length: > 0 }
            ? monogramOverride
            : PublicSuffix.Monogram(BrowserTab.HostOf(url));
        _accentIndex = PublicSuffix.AccentIndex(BrowserTab.HostOf(url));

        Refresh(animate: keyChanged);
    }

    private bool _isInternal;
    private string _monogram = "•";
    private int _accentIndex;

    private void OnIconUpdated(string key)
    {
        // An empty key means the whole cache changed (a clear), so everyone redraws.
        if (key.Length == 0 || string.Equals(key, Key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(PublicSuffix.RegistrableDomain(key),
                             PublicSuffix.RegistrableDomain(Key), StringComparison.OrdinalIgnoreCase))
        {
            Refresh(animate: true);
        }
    }

    private void Refresh(bool animate)
    {
        if (_isInternal)
        {
            Show(_dome, animate);
            return;
        }

        bool wantIcons = AppSettings.GetBool("siteIcons");
        ImageSource? source = null;

        if (wantIcons && Key.Length > 0)
        {
            double scale = XamlRoot?.RasterizationScale ?? 1.0;
            source = Favicons.GetImage(Key, _size, scale);
        }

        if (source is not null)
        {
            _image.Source = source;
            Show(_image, animate);
            return;
        }

        // No icon. The monogram is the fallback rather than Safari's grey globe, because
        // it is what this project already shows and removing it was never asked for.
        _letter.Text = _monogram;
        TryStyle(_chip, "AvMonogramChip" + (_accentIndex + 1) + "Style");
        ApplyRadius(IsGlass());
        Show(_chip, animate);
    }

    private void Show(UIElement layer, bool animate)
    {
        foreach (UIElement child in Children)
        {
            double target = ReferenceEquals(child, layer) ? 1 : 0;
            if (animate)
            {
                Animate(child, target);
            }
            else
            {
                Stop(child);
                child.Opacity = target;
            }
        }
    }

    // A Storyboard animating Opacity HOLDS its final value, and a later plain assignment to
    // Opacity is then silently ignored. One storyboard per element, stopped before reuse.
    private static readonly Dictionary<UIElement, Storyboard> Fades = new();

    private static void Stop(UIElement element)
    {
        if (Fades.TryGetValue(element, out Storyboard? existing))
        {
            try { existing.Stop(); } catch (Exception ex) { Diagnostics.Swallowed(ex, "favicon view: stop fade"); }
        }
    }

    private static void Animate(UIElement element, double to)
    {
        Stop(element);

        DoubleAnimation animation = new()
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");

        Storyboard storyboard = new();
        storyboard.Children.Add(animation);
        Fades[element] = storyboard;
        storyboard.Begin();
    }

    /// <summary>
    /// Re-applies the radius by hand after a look swap.
    ///
    /// AvMonogramRadius is a StaticResource, which resolves once at load and never again —
    /// constraint 4. A real favicon is deliberately never rounded: Safari does not clip a
    /// site's own mark in a tab, and neither does this.
    /// </summary>
    public void ApplyLookGeometry(bool glass) => ApplyRadius(glass);

    private void ApplyRadius(bool glass)
    {
        CornerRadius radius = new(glass ? 5 : 4);
        _chip.CornerRadius = radius;
        _slug.CornerRadius = radius;
    }

    /// <summary>Called from MainWindow.ApplyLookGeometry, exactly as ToolbarRow is.</summary>
    public static void ApplyLookGeometryToAll(bool glass)
    {
        lock (Live)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (Live[i].TryGetTarget(out FaviconView? view))
                {
                    view.ApplyLookGeometry(glass);
                }
                else
                {
                    // The tab it belonged to is gone; the registry must not keep its
                    // visuals alive.
                    Live.RemoveAt(i);
                }
            }
        }
    }
}
