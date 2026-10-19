using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quadra.Infrastructure.Storage;

namespace Quadra.UnitTests.Infrastructure;

/// <summary>
/// SEC-13: a presigned PUT cannot cap the upload size, so the API checks the uploaded object
/// before saving its key, and deletes an oversized one.
/// </summary>
public sealed class PhotoSizeLimitTests
{
    private const string Key = "profiles/u1/photo.jpg";

    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    private S3ProfilePhotoStorage CreateSut() => new(
        _s3,
        Options.Create(new ProfilePhotoStorageOptions { ProfilePhotosBucket = "photos", MaxPhotoBytes = 1000 }),
        TimeProvider.System);

    private void GivenObjectOf(long bytes) =>
        _s3.GetObjectMetadataAsync("photos", Key, Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse { ContentLength = bytes });

    [Fact]
    public async Task A_photo_within_the_limit_is_accepted()
    {
        GivenObjectOf(1000);

        (await CreateSut().IsWithinLimitsAsync(Key, TestContext.Current.CancellationToken)).Should().BeTrue();
        await _s3.DidNotReceiveWithAnyArgs().DeleteObjectAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_oversized_photo_is_refused_and_deleted()
    {
        GivenObjectOf(1001);

        (await CreateSut().IsWithinLimitsAsync(Key, TestContext.Current.CancellationToken)).Should().BeFalse();
        await _s3.Received(1).DeleteObjectAsync("photos", Key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_key_with_no_uploaded_object_is_refused()
    {
        _s3.GetObjectMetadataAsync("photos", Key, Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound });

        (await CreateSut().IsWithinLimitsAsync(Key, TestContext.Current.CancellationToken)).Should().BeFalse();
    }
}
