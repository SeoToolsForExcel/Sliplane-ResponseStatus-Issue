using Microsoft.AspNetCore.Http.Features;

// Minimal reproduction: an ASP.NET Core app that sets a custom HTTP/1.1 reason
// phrase on the status line. Reached directly, the phrase arrives as sent.
// Reached through Sliplane's HTTP router, it is replaced with the canonical text
// for the status code.
//
// No dependencies, no database, no configuration. The whole demonstration is the
// status line.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

/// Sets the status line to "<code> <phrase>" and returns a short body.
static async Task WriteWithPhrase(HttpContext context, int statusCode, string phrase)
{
    var responseFeature = context.Features.Get<IHttpResponseFeature>();

    context.Response.StatusCode = statusCode;
    if (responseFeature is not null)
    {
        responseFeature.ReasonPhrase = phrase;
    }

    // The same code on a header, which every proxy tested forwards untouched.
    // A reverse proxy that terminates the connection can rebuild the status line
    // from this, which is how the real store now sends it.
    context.Response.Headers["X-SeoTools-Error"] = phrase;

    context.Response.ContentType = "text/plain; charset=utf-8";
    await context.Response.WriteAsync(
        $"This response was sent with status line: HTTP/1.1 {statusCode} {phrase}\n" +
        "If the status line you received says anything else, something between us rewrote it.\n");
}

// The case that matters to us: 401 carrying an application error code.
app.MapGet("/reason-phrase", context => WriteWithPhrase(context, 401, "ActivationNotFound"));

// Same thing with an arbitrary phrase, so any value can be tried.
app.MapGet("/reason-phrase/{phrase}", (HttpContext context, string phrase) =>
    WriteWithPhrase(context, 401, phrase));

// Not specific to 401 -- a 2xx custom phrase is rewritten the same way.
app.MapGet("/ok-phrase", context => WriteWithPhrase(context, 200, "EverythingIsFine"));

// What the container sees, in case the request side is of interest.
app.MapGet("/echo", (HttpContext context) => Results.Json(new
{
    Protocol = context.Request.Protocol,
    Scheme = context.Request.Scheme,
    Method = context.Request.Method,
    Path = context.Request.Path.Value,
    Headers = context.Request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value!)),
}));

app.MapGet("/health", () => Results.Text("ok"));

app.MapGet("/", () => Results.Text(
    """
    Reason-phrase reproduction
    ==========================

    ASP.NET Core / Kestrel sets a custom HTTP/1.1 reason phrase on the status
    line. We need that phrase to survive to the client.

      GET /reason-phrase          -> sends: HTTP/1.1 401 ActivationNotFound
      GET /reason-phrase/{phrase} -> sends: HTTP/1.1 401 {phrase}
      GET /ok-phrase              -> sends: HTTP/1.1 200 EverythingIsFine
      GET /echo                   -> what the container sees
      GET /health                 -> ok

    Compare the first line of the response:

      curl -i -s https://<this-service>/reason-phrase | head -1

    Expected (and what the container sends):
      HTTP/1.1 401 ActivationNotFound

    Observed through the platform router:
      HTTP/1.1 401 Unauthorized

    Why it matters: a desktop application in the field reads
    HttpWebResponse.StatusDescription to decide which error occurred. Those
    clients are installed on customer machines and cannot be updated, so the
    phrase is a fixed part of the contract.

    """,
    "text/plain; charset=utf-8"));

app.Run();
