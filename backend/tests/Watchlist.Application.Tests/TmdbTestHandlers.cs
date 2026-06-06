using System.Net;
using FluentAssertions;

namespace Watchlist.Application.Tests;

internal sealed class StaticTmdbHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
{
    public List<string> RequestedPathAndQueries { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization.Should().NotBeNull();
        string key = request.RequestUri!.PathAndQuery;
        RequestedPathAndQueries.Add(key);
        key.Should().BeOneOf(responses.Keys);
        if (!responses.TryGetValue(key, out string? content))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        if (content == "__404__")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        if (content == "__401__")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }

        if (content == "__403__")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        }

        if (content == "__429__")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        }

        if (content == "__500__")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        });
    }
}

internal sealed class ThrowingTmdbHandler(Exception exception) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromException<HttpResponseMessage>(exception);
    }
}
