# Sliplane-ResponseStatus-Issue

A minimal reproduction: **custom HTTP/1.1 reason phrases are rewritten by the Sliplane HTTP router.**

An ASP.NET Core app sets the reason phrase on the status line. Reached directly, the phrase
arrives as sent. Reached through Sliplane, it is replaced with the canonical text for the status
code.

## The observation

```
# directly against the container
$ curl -i -s http://localhost:8080/reason-phrase | head -1
HTTP/1.1 401 ActivationNotFound

# the same container, deployed on Sliplane
$ curl -i -s https://reason-phrase-demo.sliplane.app/reason-phrase | head -1
HTTP/1.1 401 Unauthorized
```

Everything else about the response is identical — status code, headers (the upstream
`Server: Kestrel` passes through untouched), and body. Only the status line text changes.

The connection is HTTP/1.1 in both cases, so this is not HTTP/2 dropping the phrase.
It is not specific to 401 either: `/ok-phrase` sends `200 EverythingIsFine` and arrives as
`200 OK`.

## Why this matters to us

We are moving a licensing API from Azure App Service to Sliplane. It reports application error
codes in the reason phrase:

```
HTTP/1.1 401 ActivationNotFound
HTTP/1.1 401 ActivationExpired
HTTP/1.1 401 ActivationNotMatchingFingerPrint
HTTP/1.1 401 MaxRegistrationsPerActivation
```

The desktop client reads `HttpWebResponse.StatusDescription` and switches on it. That client is
installed on customer machines and **cannot be updated**, so the phrase is a fixed part of the
contract rather than something we can redesign. With the phrase rewritten, every distinct error
arrives as `Unauthorized` and the application cannot tell an expired licence from a wrong machine
from a seat-limit refusal.

## Endpoints

| Path | Sends |
| --- | --- |
| `/reason-phrase` | `HTTP/1.1 401 ActivationNotFound` |
| `/reason-phrase/{phrase}` | `HTTP/1.1 401 {phrase}` |
| `/ok-phrase` | `HTTP/1.1 200 EverythingIsFine` |
| `/echo` | JSON of what the container sees (protocol, scheme, headers) |
| `/health` | `ok` |

## Reproducing locally

```bash
dotnet run
curl -i -s http://localhost:5000/reason-phrase | head -1
# HTTP/1.1 401 ActivationNotFound
```

Or through the container, exactly as Sliplane builds it:

```bash
docker build -t reason-phrase-demo .
docker run --rm -p 8080:8080 reason-phrase-demo
curl -i -s http://localhost:8080/reason-phrase | head -1
# HTTP/1.1 401 ActivationNotFound
```

## What we are asking

1. Can the router be configured to pass the upstream status line through unchanged?
2. If not, can a service be exposed as raw TCP on port 443 with its own IP, so the container
   terminates TLS itself and no HTTP proxy is in the path?

We understand this may be structural: a router built on Go's `net/http` only ever receives a
status *code* from the upstream response object and writes the canonical text for it, with no API
for a custom phrase. If that is the situation, saying so plainly is a complete answer — we will
plan around it.
