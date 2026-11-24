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

    // Audit 2026-11-24, photo-check-bypass-by-presigned-put-reuse: the upload URL writes to an
    // upload key; a checked upload is copied to the photo key that reads use, and the upload deleted.
    private const string Upload = "profiles/u1/upload/a.jpg";
    private const string Published = "profiles/u1/photo/a.jpg";

    [Fact]
    public async Task A_checked_upload_is_published_under_a_key_the_upload_url_cannot_reach()
    {
        _s3.GetObjectMetadataAsync("photos", Upload, Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse { ContentLength = 10 });

        (await CreateSut().IsWithinLimitsAsync(Upload, TestContext.Current.CancellationToken)).Should().BeTrue();

        await _s3.Received(1).CopyObjectAsync("photos", Upload, "photos", Published, Arg.Any<CancellationToken>());
        await _s3.Received(1).DeleteObjectAsync("photos", Upload, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reads_of_an_upload_key_are_served_from_the_published_photo()
    {
        _s3.GetPreSignedURLAsync(Arg.Any<GetPreSignedUrlRequest>()).Returns(c => "signed:" + c.Arg<GetPreSignedUrlRequest>().Key);

        (await CreateSut().GetReadUrlAsync(Upload, TestContext.Current.CancellationToken)).Should().Be("signed:" + Published);
    }

    [Fact]
    public async Task Saving_the_same_upload_key_again_checks_the_published_photo()
    {
        _s3.GetObjectMetadataAsync("photos", Upload, Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound });
        _s3.GetObjectMetadataAsync("photos", Published, Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse { ContentLength = 10 });

        (await CreateSut().IsWithinLimitsAsync(Upload, TestContext.Current.CancellationToken)).Should().BeTrue();
        await _s3.DidNotReceiveWithAnyArgs().CopyObjectAsync(default!, default!, default!, default!, TestContext.Current.CancellationToken);
    }
}
