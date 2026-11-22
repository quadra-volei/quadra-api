using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quadra.Infrastructure.Storage;

namespace Quadra.UnitTests.Infrastructure;

/// <summary>
/// SEC-14: deleting the account removes every upload of the person (their folder in the bucket),
/// page by page, and never anything outside that one folder.
/// </summary>
public sealed class PhotoFolderDeletionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    private S3ProfilePhotoStorage CreateSut() => new(
        _s3,
        Options.Create(new ProfilePhotoStorageOptions { ProfilePhotosBucket = "photos" }),
        TimeProvider.System);

    [Fact]
    public async Task Erasing_a_person_deletes_every_object_in_their_folder()
    {
        var userId = Guid.NewGuid();
        var folder = $"profiles/{userId:D}/";
        _s3.ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.ContinuationToken == null), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = folder + "photo/a.jpg" }],
                IsTruncated = true,
                NextContinuationToken = "next",
            });
        _s3.ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.ContinuationToken == "next"), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [new S3Object { Key = folder + "photo/b.jpg" }], IsTruncated = false });

        await new PhotosPersonalData(CreateSut()).EraseAsync(userId, Ct);

        await _s3.Received(1).DeleteObjectsAsync(
            Arg.Is<DeleteObjectsRequest>(r => r.BucketName == "photos"
                && r.Objects.Select(o => o.Key).SequenceEqual(new[] { folder + "photo/a.jpg", folder + "photo/b.jpg" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_folder_deletes_nothing()
    {
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { IsTruncated = false });

        (await CreateSut().DeleteAllAsync($"profiles/{Guid.NewGuid():D}/", Ct)).Should().Be(0);
        await _s3.DidNotReceiveWithAnyArgs().DeleteObjectsAsync(default!, Ct);
    }

    [Theory]
    [InlineData("")]
    [InlineData("profiles/")]
    [InlineData("profiles/../")]
    [InlineData("ads/")]
    public async Task Only_one_persons_folder_can_be_deleted(string prefix)
    {
        var act = () => CreateSut().DeleteAllAsync(prefix, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
        await _s3.DidNotReceiveWithAnyArgs().ListObjectsV2Async(default!, Ct);
    }
}
