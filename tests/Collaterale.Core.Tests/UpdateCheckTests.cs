using System.Net;
using Collaterale.Core.Settings;
using Collaterale.Core.Support;

namespace Collaterale.Core.Tests;

public class UpdateCheckTests
{
    private sealed class FakeHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond());
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body) };

    private static readonly DateTime Now = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("1.2.3", "1.2.4", true)]
    [InlineData("1.2.3", "1.10.0", true)]            // numbers, not text
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.2.3", "1.2.2", false)]
    [InlineData("1.2", "1.2.1", true)]
    [InlineData("1.2.3", "v2.0.0", true)]            // a leading v is tolerated
    [InlineData("1.2.3", "banana", false)]           // an unreadable version is no news
    public void Versions_are_compared_as_numbers(string current, string offered, bool newer)
        => Assert.Equal(newer, UpdateCheck.IsNewer(current, offered));

    [Fact]
    public void A_check_is_due_only_when_enabled_configured_and_long_enough_ago()
    {
        var p = new UpdatePreferences { Check = true, FeedUrl = "https://example.org/v.json", EveryDays = 7, LastCheckUtc = Now.AddDays(-8) };
        Assert.True(UpdateCheck.IsDue(p, Now));
        p.LastCheckUtc = Now.AddDays(-3);
        Assert.False(UpdateCheck.IsDue(p, Now));
        p.LastCheckUtc = default;
        Assert.True(UpdateCheck.IsDue(p, Now));              // never checked
        p.Check = false;
        Assert.False(UpdateCheck.IsDue(p, Now));
        p.Check = true;
        p.FeedUrl = "";
        Assert.False(UpdateCheck.IsDue(p, Now));              // nowhere to ask
        p.FeedUrl = "file:///etc/passwd";
        Assert.False(UpdateCheck.IsDue(p, Now));              // only web addresses
    }

    [Fact]
    public async Task A_newer_version_in_the_feed_is_reported_with_its_address()
    {
        var handler = new FakeHandler(() => Json("{\"version\":\"2.0.0\",\"url\":\"https://example.org/get\"}"));
        var r = await UpdateCheck.RunAsync(new HttpClient(handler), "https://example.org/v.json", "1.0.0");
        Assert.True(r.Available);
        Assert.Equal("2.0.0", r.Version);
        Assert.Equal("https://example.org/get", r.Url);
        Assert.Contains("2.0.0", r.Message);
    }

    [Fact]
    public async Task The_same_version_an_error_or_garbage_means_no_update_and_never_throws()
    {
        Assert.False((await UpdateCheck.RunAsync(new HttpClient(new FakeHandler(() => Json("{\"version\":\"1.0.0\"}"))), "https://x/v.json", "1.0.0")).Available);
        Assert.False((await UpdateCheck.RunAsync(new HttpClient(new FakeHandler(() => Json("", HttpStatusCode.NotFound))), "https://x/v.json", "1.0.0")).Available);
        Assert.False((await UpdateCheck.RunAsync(new HttpClient(new FakeHandler(() => Json("<html>not json</html>"))), "https://x/v.json", "1.0.0")).Available);
        var down = new HttpClient(new FakeHandler(() => throw new HttpRequestException("offline")));
        var r = await UpdateCheck.RunAsync(down, "https://x/v.json", "1.0.0");
        Assert.False(r.Available);
        Assert.Contains("could not", r.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_feed_that_offers_a_non_web_address_is_not_trusted()
    {
        var handler = new FakeHandler(() => Json("{\"version\":\"9.0.0\",\"url\":\"file:///etc/passwd\"}"));
        var r = await UpdateCheck.RunAsync(new HttpClient(handler), "https://x/v.json", "1.0.0");
        Assert.True(r.Available);
        Assert.Equal("", r.Url);                         // the version is news, the address is dropped
    }
}
