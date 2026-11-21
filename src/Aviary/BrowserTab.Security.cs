using Microsoft.Web.WebView2.Core;

namespace Aviary;

internal sealed partial class BrowserTab
{
    private CoreWebView2WebErrorStatus? _certificateFailureStatus;
    private void AttachSecurity(CoreWebView2 core)
    {
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsReputationCheckingRequired = true;
        core.PermissionRequested += OnPermissionRequested;
        core.ServerCertificateErrorDetected += OnServerCertificateError;
        core.LaunchingExternalUriScheme += OnLaunchingExternalUriScheme;
    }

    private void DetachSecurity(CoreWebView2 core)
    {
        core.PermissionRequested -= OnPermissionRequested;
        core.ServerCertificateErrorDetected -= OnServerCertificateError;
        core.LaunchingExternalUriScheme -= OnLaunchingExternalUriScheme;
    }

    private void OnPermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args)
    {
        // Keep the engine's permission prompt for secure websites. In private browsing a
        // decision cannot become a saved grant. Insecure origins never receive a grant.
        if (IsPrivate) args.SavesInProfile = false;
        if (!SecurityPolicy.IsSecurePermissionOrigin(args.Uri))
        {
            args.State = CoreWebView2PermissionState.Deny;
            return;
        }

        // Beta Prep 2 (Settings → Content → "What websites may ask for"): a kind set to Block is
        // refused for every site without a question, and the refusal is never saved as a site's own
        // answer, so switching back to Ask really does ask again.
        if (SitePermissions.Blocked(args.PermissionKind))
        {
            args.State = CoreWebView2PermissionState.Deny;
            args.SavesInProfile = false;
            if (Records)
            {
                Diagnostics.Browse("permission-blocked", args.PermissionKind.ToString(), new { tab = Id, kind = args.PermissionKind.ToString(), site = HostOf(args.Uri) });
            }
        }
    }

    private void OnServerCertificateError(CoreWebView2 sender, CoreWebView2ServerCertificateErrorDetectedEventArgs args)
    {
        // Some runtimes report a cancelled certificate as ConnectionAborted at completion.
        // Preserve the security cause so it cannot turn into an HTTP fallback or download.
        _certificateFailureStatus = args.ErrorStatus;
        args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        if (Records) Diagnostics.Browse("certificate-rejected", args.ErrorStatus.ToString(), new { tab = Id, url = args.RequestUri });
    }

    private static void OnLaunchingExternalUriScheme(CoreWebView2 sender, CoreWebView2LaunchingExternalUriSchemeEventArgs args)
    {
        // Clicking an app link retains the OS prompt; scripts cannot launch an external app.
        if (!args.IsUserInitiated) args.Cancel = true;
    }
}
