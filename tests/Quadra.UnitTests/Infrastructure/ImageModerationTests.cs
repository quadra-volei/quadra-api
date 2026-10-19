using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quadra.Infrastructure.Storage;

namespace Quadra.UnitTests.Infrastructure;

/// <summary>DECISIONS #76: adult-content check of uploaded photos (Google Vision SafeSearch), fail closed.</summary>
public sealed class ImageModerationTests
{
    private static JsonElement Answer(string adult, string racy = "UNLIKELY", string violence = "UNLIKELY") =>
        JsonDocument.Parse(
            $$$"""{"responses":[{"safeSearchAnnotation":{"adult":"{{{adult}}}","racy":"{{{racy}}}","violence":"{{{violence}}}"}}]}""")
            .RootElement;

    [Theory]
    [InlineData("VERY_UNLIKELY", "UNLIKELY", "UNLIKELY", true)]
    [InlineData("POSSIBLE", "LIKELY", "LIKELY", true)]
    [InlineData("LIKELY", "UNLIKELY", "UNLIKELY", false)]
    [InlineData("VERY_LIKELY", "UNLIKELY", "UNLIKELY", false)]
    [InlineData("UNLIKELY", "VERY_LIKELY", "UNLIKELY", false)]
    [InlineData("UNLIKELY", "UNLIKELY", "VERY_LIKELY", false)]
    public void SafeSearch_ratings_decide(string adult, string racy, string violence, bool allowed) =>
        GoogleVisionImageModerator.IsAllowed(Answer(adult, racy, violence)).Should().Be(allowed);

    [Fact]
    public void An_answer_without_ratings_is_refused() =>
        GoogleVisionImageModerator.IsAllowed(JsonDocument.Parse("""{"responses":[{"error":{"code":7}}]}""").RootElement)
            .Should().BeFalse();

    private sealed class FakeVision : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string Body { get; init; } = "{}";
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }

    private static GoogleVisionImageModerator Sut(FakeVision vision) => new(
        Options.Create(new ImageModerationOptions { Provider = "GoogleVision", GoogleVisionApiKey = "test-key" }),
        NullLogger<GoogleVisionImageModerator>.Instance,
        new HttpClient(vision));

    [Fact]
    public async Task Vision_is_asked_with_the_read_url_and_its_verdict_is_used()
    {
        var vision = new FakeVision { Body = Answer("VERY_LIKELY").GetRawText() };

        (await Sut(vision).IsAllowedAsync("https://bucket/x.jpg?sig=1", TestContext.Current.CancellationToken)).Should().BeFalse();
        vision.Last!.RequestUri!.Query.Should().Contain("key=test-key");
        vision.LastBody.Should().Contain("https://bucket/x.jpg?sig=1").And.Contain("SAFE_SEARCH_DETECTION");
    }

    [Fact]
    public async Task Vision_down_refuses_the_photo() =>
        (await Sut(new FakeVision { Status = HttpStatusCode.ServiceUnavailable })
            .IsAllowedAsync("https://bucket/x.jpg", TestContext.Current.CancellationToken)).Should().BeFalse();

    [Fact]
    public async Task A_refused_photo_is_deleted_before_its_key_can_be_saved()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.GetObjectMetadataAsync("photos", "profiles/u1/p.jpg", Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse { ContentLength = 100 });
        var moderator = Substitute.For<IImageModerator>();
        moderator.IsAllowedAsync(default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(false);
        var storage = new S3ProfilePhotoStorage(
            s3, Options.Create(new ProfilePhotoStorageOptions { ProfilePhotosBucket = "photos" }), TimeProvider.System, moderator);

        (await storage.IsWithinLimitsAsync("profiles/u1/p.jpg", TestContext.Current.CancellationToken)).Should().BeFalse();
        await s3.Received(1).DeleteObjectAsync("photos", "profiles/u1/p.jpg", Arg.Any<CancellationToken>());
    }
}
