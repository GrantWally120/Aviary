namespace Aviary;

/// <summary>
/// Reader's state, kept by the TAB it belongs to (bug 93, found 25 Sep 2026 building M6 stage 3's
/// "Use Reader on every article").
///
/// <para>It used to be two fields on the WINDOW — "Reader is on" and "this page has an article" —
/// which every tab's navigation reset and re-asked, the tabs behind included, and which switching
/// tabs never touched. Measured: Reader on in tab A, a page loaded in tab B, back to A — A still
/// showed Reader, its Aa menu offered "Show Reader", and choosing it did nothing, so the only way
/// out of Reader was to leave the page (control: with no other tab loading, A's menu offered
/// "Hide Reader" and it worked). Now each tab answers for itself and the window reads the tab in
/// front.</para>
/// </summary>
internal sealed partial class BrowserTab
{
    /// <summary>Whether this tab's page can be read as an article — asked after each navigation.</summary>
    public bool ReaderAvailable { get; set; }

    /// <summary>Whether this tab is showing Reader right now (the window sets it as Reader comes and goes).</summary>
    public bool ReaderShown => _readerShown;

    /// <summary>
    /// A page that loaded BEHIND, due to open in Reader automatically (the per-site choice, or M6's
    /// "Use Reader on every article"): Reader opens the first time the tab comes to the front.
    /// </summary>
    public bool ReaderDue { get; set; }

    /// <summary>From OnNavigationStarting: a new page has no article yet, and nothing is due.</summary>
    private void ReaderOnNavigationStarting()
    {
        ReaderAvailable = false;
        ReaderDue = false;
    }
}
