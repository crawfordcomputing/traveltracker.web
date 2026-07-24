using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace TravelTracker.Tests;

// Minimal IUrlHelper for driving page handlers that build links (e.g. the Invites
// create handler's Url.Page(...) call). Returns a deterministic path so handlers
// can run outside the routing pipeline. Url.Page/Url.Action are extension methods
// that funnel through RouteUrl, so that is the only member that needs real output.
internal sealed class FakeUrlHelper : IUrlHelper
{
    public FakeUrlHelper(ActionContext actionContext) => ActionContext = actionContext;

    public ActionContext ActionContext { get; }

    public string? Action(UrlActionContext actionContext) => "/";
    public string? Content(string? contentPath) =>
        contentPath is null ? "/" : contentPath.Replace("~", string.Empty);
    public bool IsLocalUrl(string? url) => !string.IsNullOrEmpty(url) && url.StartsWith('/');
    public string? Link(string? routeName, object? values) => "https://localhost/link";
    public string? RouteUrl(UrlRouteContext routeContext) => "https://localhost/Account/Register?token=test";
}
