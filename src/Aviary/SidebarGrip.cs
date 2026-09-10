using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Aviary;

/// <summary>
/// The 6px column between the sidebar and the page that you drag to resize.
///
/// It exists as its own type for exactly one reason: <c>UIElement.ProtectedCursor</c> is
/// protected, so a stock element cannot be given the west-east resize cursor from
/// MainWindow's code-behind. A subclass can set its own.
///
/// Derived from Grid rather than Border because Border is SEALED in WinUI 3. Grid is a
/// Panel, is not sealed, and carries the same Background and hit-testing behaviour.
///
/// InputSystemCursor lives in Microsoft.UI.Input, NOT Microsoft.UI.Xaml.Input — the same
/// family of namespace trap as the WindowId and TypedEventHandler mistakes in the handoff.
/// </summary>
internal sealed class SidebarGrip : Grid
{
    public SidebarGrip()
    {
        // A null Background is not hit-testable; Transparent is. Without this the grip is
        // a 6px strip of nothing and the drag never starts.
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        SetResizeCursor();
    }

    public void SetResizeCursor()
    {
        try
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        }
        catch (System.Exception)
        {
            // An unavailable cursor shape costs the hover affordance, not the drag.
        }
    }
}
