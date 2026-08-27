using System.Buffers;
using System.Security.Cryptography;
using Flowspan.Desktop;
using Flowspan.Platform;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopRemoteWindowJpegCodecTests
{
    private const int FixtureLength = 397;
    private const string FixtureName = "remote-window-2x2.jpg";
    private const string FixtureSha256 =
        "f294e425eda6aea42373311b447ac5518eabe2a897304b5a90c9a25ae3c8095e";

    // Generated from deterministic pixels with libjpeg-turbo 3.2.0. These
    // samples exercise syntax that the production Skia encoder does not emit.
    private const string ProgressiveJpegBase64 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAYEBAUEBAYFBQUGBgYHCQ4JCQgICRINDQoO" +
        "FRIWFhUSFBQXGiEcFxgfGRQUHScdHyIjJSUlFhwpLCgkKyEkJST/2wBDAQYGBgkICREJ" +
        "CREkGBQYJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQk" +
        "JCQkJCQkJCT/wgARCAACAAIDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAb/" +
        "xAAUAQEAAAAAAAAAAAAAAAAAAAAF/9oADAMBAAIQAxAAAAG5A4H/xAAWEAEBAQAAAAAA" +
        "AAAAAAAAAAADBQL/2gAIAQEAAQUCnAWp/wD/xAAXEQADAQAAAAAAAAAAAAAAAAAAAgMz" +
        "/9oACAEDAQE/AbaMf//EABcRAAMBAAAAAAAAAAAAAAAAAAABAzL/2gAIAQIBAT8Bptn/" +
        "xAAaEAEAAgMBAAAAAAAAAAAAAAABAgMABEFh/9oACAEBAAY/AtZaoK1R55n/xAAWEAEB" +
        "AQAAAAAAAAAAAAAAAAABEQD/2gAIAQEAAT8hSdGqas7/2gAMAwEAAgADAAAAEAf/xAAX" +
        "EQADAQAAAAAAAAAAAAAAAAAAAaGx/9oACAEDAQE/EKHp/8QAFxEAAwEAAAAAAAAAAAAA" +
        "AAAAAAGhsf/aAAgBAgEBPxC16f/EABYQAQEBAAAAAAAAAAAAAAAAAAEAIf/aAAgBAQAB" +
        "PxBzDP6EVU1W/9k=";

    private const string RestartJpegBase64 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAYEBAUEBAYFBQUGBgYHCQ4JCQgICRINDQoO" +
        "FRIWFhUSFBQXGiEcFxgfGRQUHScdHyIjJSUlFhwpLCgkKyEkJST/wAALCAAIABABAREA" +
        "/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQA" +
        "AAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJico" +
        "KSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKT" +
        "lJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo" +
        "6erx8vP09fb3+Pn6/90ABAAB/9oACAEBAAA/AFtPBXhf4X6Vbah4ll+z+duFtbRxl5rp" +
        "lXcVRR+A3NhQWXLDIr//0I5PFHjHxldSWXhpZfDeieZiL7MNl5MgKlTJKCShypOIyvDl" +
        "SXHJ/9k=";

    private delegate void FrameFiller(
        Span<byte> pixels,
        int width,
        int height,
        int stride);

    [Fact]
    public void EncodingProfilesMatchTheFrozenFiniteLadder()
    {
        DesktopRemoteWindowJpegProfile[] profiles =
            DesktopRemoteWindowJpegCodec.EncodingProfiles.ToArray();

        Assert.Equal(
            [
                new DesktopRemoteWindowJpegProfile(1, 1, 82),
                new DesktopRemoteWindowJpegProfile(1, 1, 68),
                new DesktopRemoteWindowJpegProfile(1, 1, 54),
                new DesktopRemoteWindowJpegProfile(3, 4, 68),
                new DesktopRemoteWindowJpegProfile(3, 4, 54),
                new DesktopRemoteWindowJpegProfile(1, 2, 68),
                new DesktopRemoteWindowJpegProfile(1, 2, 54),
            ],
            profiles);
    }

    [Theory]
    [InlineData(0, 1, 1, 82, 20, 12)]
    [InlineData(1, 1, 1, 68, 20, 12)]
    [InlineData(2, 1, 1, 54, 20, 12)]
    [InlineData(3, 3, 4, 68, 15, 9)]
    [InlineData(4, 3, 4, 54, 15, 9)]
    [InlineData(5, 1, 2, 68, 10, 6)]
    [InlineData(6, 1, 2, 54, 10, 6)]
    public void ProfileRunnerStopsAtEachPossibleFirstFittingCandidate(
        int firstFittingIndex,
        int expectedScaleNumerator,
        int expectedScaleDenominator,
        int expectedQuality,
        int expectedWidth,
        int expectedHeight)
    {
        var attemptedProfiles = new List<DesktopRemoteWindowJpegProfile>();

        DesktopRemoteWindowJpegEncodingResult result =
            DesktopRemoteWindowJpegCodec.EncodeUsingProfiles(
                sourceWidth: 20,
                sourceHeight: 12,
                (profile, width, height) =>
                {
                    attemptedProfiles.Add(profile);
                    if (attemptedProfiles.Count - 1 != firstFittingIndex)
                    {
                        return DesktopRemoteWindowJpegEncodingResult.Failed(
                            DesktopRemoteWindowJpegEncodingStatus.PayloadLimitExceeded);
                    }

                    return DesktopRemoteWindowJpegEncodingResult.Encoded(
                        new DesktopRemoteWindowEncodedJpeg(
                            [0xff],
                            width,
                            height,
                            profile));
                });

        DesktopRemoteWindowJpegProfile[] expectedAttempts =
            DesktopRemoteWindowJpegCodec.EncodingProfiles
                .Take(firstFittingIndex + 1)
                .ToArray();
        Assert.Equal(expectedAttempts, attemptedProfiles);
        using DesktopRemoteWindowEncodedJpeg encoded = Assert.IsType<
            DesktopRemoteWindowEncodedJpeg>(result.Frame);
        Assert.Equal(expectedScaleNumerator, encoded.ScaleNumerator);
        Assert.Equal(expectedScaleDenominator, encoded.ScaleDenominator);
        Assert.Equal(expectedQuality, encoded.Quality);
        Assert.Equal(expectedWidth, encoded.Width);
        Assert.Equal(expectedHeight, encoded.Height);
    }

    [Theory]
    [InlineData(7, 3, 4, 5)]
    [InlineData(7, 1, 2, 3)]
    [InlineData(1, 1, 2, 1)]
    public void ScalingRoundsDownWithoutEverProducingAnEmptyDimension(
        int dimension,
        int numerator,
        int denominator,
        int expected)
    {
        int scaled = DesktopRemoteWindowJpegCodec.GetScaledDimension(
            dimension,
            new DesktopRemoteWindowJpegProfile(
                numerator,
                denominator,
                Quality: 54));

        Assert.Equal(expected, scaled);
    }

    [Fact]
    public void EncoderUsesTheFirstFittingProfileAndReturnsBoundedJpeg()
    {
        using NativeRemoteWindowFrame source = CreateFrame(
            width: 32,
            height: 24,
            stride: 32 * 4,
            static (pixels, width, height, stride) =>
                FillGradient(pixels, width, height, stride, alphaSeed: 17));

        DesktopRemoteWindowJpegEncodingResult result =
            DesktopRemoteWindowJpegCodec.Encode(source);

        Assert.True(result.Succeeded);
        Assert.Equal(DesktopRemoteWindowJpegEncodingStatus.Encoded, result.Status);
        using DesktopRemoteWindowEncodedJpeg encoded = Assert.IsType<
            DesktopRemoteWindowEncodedJpeg>(result.Frame);
        Assert.Equal(82, encoded.Quality);
        Assert.Equal(1, encoded.ScaleNumerator);
        Assert.Equal(1, encoded.ScaleDenominator);
        Assert.Equal(32, encoded.Width);
        Assert.Equal(24, encoded.Height);
        Assert.InRange(
            encoded.PayloadLength,
            1,
            DesktopRemoteWindowJpegCodec.MaximumEncodedBytes);
        Assert.Equal(0xff, encoded.Payload.Span[0]);
        Assert.Equal(0xd8, encoded.Payload.Span[1]);
        Assert.Equal(0xff, encoded.Payload.Span[^2]);
        Assert.Equal(0xd9, encoded.Payload.Span[^1]);

        DesktopRemoteWindowJpegDecodingResult decoded =
            DesktopRemoteWindowJpegCodec.Decode(encoded.Payload);

        Assert.True(decoded.Succeeded);
        using DesktopRemoteWindowBgraFrame frame = Assert.IsType<
            DesktopRemoteWindowBgraFrame>(decoded.Frame);
        Assert.Equal(32, frame.Width);
        Assert.Equal(24, frame.Height);
        Assert.Equal(32 * 4, frame.Stride);
        Assert.Equal(NativeRemoteWindowPixelFormat.Bgra8888, frame.PixelFormat);
        Assert.Equal(32 * 24 * 4, frame.Pixels.Length);
        AssertAllAlphaOpaque(frame.Pixels.Span);
    }

    [Fact]
    public void EncoderDiscardsSourceAlphaDeterministicallyWithinOneRuntime()
    {
        using NativeRemoteWindowFrame transparent = CreateFrame(
            width: 48,
            height: 36,
            stride: 48 * 4,
            static (pixels, width, height, stride) =>
                FillGradient(pixels, width, height, stride, alphaSeed: 0));
        using NativeRemoteWindowFrame opaque = CreateFrame(
            width: 48,
            height: 36,
            stride: 48 * 4,
            static (pixels, width, height, stride) =>
                FillGradient(pixels, width, height, stride, alphaSeed: 255));

        using DesktopRemoteWindowEncodedJpeg first = Assert.IsType<
            DesktopRemoteWindowEncodedJpeg>(
            DesktopRemoteWindowJpegCodec.Encode(transparent).Frame);
        using DesktopRemoteWindowEncodedJpeg second = Assert.IsType<
            DesktopRemoteWindowEncodedJpeg>(
            DesktopRemoteWindowJpegCodec.Encode(opaque).Frame);

        Assert.Equal(first.Profile, second.Profile);
        Assert.Equal(first.ExportPayload(), second.ExportPayload());
    }

    [Fact]
    public void EncoderAcceptsValidatedPaddedStrideAndIgnoresPaddingBytes()
    {
        using NativeRemoteWindowFrame first = CreateFrame(
            width: 7,
            height: 5,
            stride: 40,
            static (pixels, width, height, stride) =>
            {
                FillGradient(pixels, width, height, stride, alphaSeed: 23);
                FillPadding(pixels, width, height, stride, 0x11);
            });
        using NativeRemoteWindowFrame second = CreateFrame(
            width: 7,
            height: 5,
            stride: 40,
            static (pixels, width, height, stride) =>
            {
                FillGradient(pixels, width, height, stride, alphaSeed: 23);
                FillPadding(pixels, width, height, stride, 0xee);
            });

        using DesktopRemoteWindowEncodedJpeg firstEncoded = Assert.IsType<
            DesktopRemoteWindowEncodedJpeg>(
            DesktopRemoteWindowJpegCodec.Encode(first).Frame);
        using DesktopRemoteWindowEncodedJpeg secondEncoded = Assert.IsType<
            DesktopRemoteWindowEncodedJpeg>(
            DesktopRemoteWindowJpegCodec.Encode(second).Frame);

        Assert.Equal(firstEncoded.ExportPayload(), secondEncoded.ExportPayload());
        Assert.Equal(7, firstEncoded.Width);
        Assert.Equal(5, firstEncoded.Height);
    }

    [Fact]
    public void EncoderReturnsPayloadFreeDropWhenTheFrozenLadderCannotFit()
    {
        using NativeRemoteWindowFrame source = CreateFrame(
            width: 4096,
            height: 4096,
            stride: 4096 * 4,
            static (pixels, width, height, stride) =>
                FillHalfScaleLumaNoise(pixels, width, height, stride));

        DesktopRemoteWindowJpegEncodingResult result =
            DesktopRemoteWindowJpegCodec.Encode(source);

        Assert.False(result.Succeeded);
        Assert.Equal(
            DesktopRemoteWindowJpegEncodingStatus.PayloadLimitExceeded,
            result.Status);
        Assert.Null(result.Frame);
    }

    [Fact]
    public void FixedJpegFixtureDecodesToTightlyPackedOpaqueBgra()
    {
        byte[] fixture = ReadFixture();

        Assert.Equal(FixtureLength, fixture.Length);
        Assert.Equal(
            FixtureSha256,
            ComputeSha256(fixture));

        DesktopRemoteWindowJpegDecodingResult result =
            DesktopRemoteWindowJpegCodec.Decode(fixture);

        Assert.True(result.Succeeded);
        Assert.Equal(DesktopRemoteWindowJpegDecodingStatus.Decoded, result.Status);
        using DesktopRemoteWindowBgraFrame decoded = Assert.IsType<
            DesktopRemoteWindowBgraFrame>(result.Frame);
        Assert.Equal(2, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.Equal(8, decoded.Stride);
        Assert.Equal(16, decoded.Pixels.Length);
        AssertAllAlphaOpaque(decoded.Pixels.Span);
        Assert.Equal(FixtureSha256, ComputeSha256(fixture));
    }

    [Fact]
    public void DecoderRejectsEmptyAndOverBudgetPayloadsBeforeCodecUse()
    {
        DesktopRemoteWindowJpegDecodingResult empty =
            DesktopRemoteWindowJpegCodec.Decode(ReadOnlyMemory<byte>.Empty);
        DesktopRemoteWindowJpegDecodingResult oversized =
            DesktopRemoteWindowJpegCodec.Decode(
                new byte[DesktopRemoteWindowJpegCodec.MaximumEncodedBytes + 1]);

        Assert.Equal(DesktopRemoteWindowJpegDecodingStatus.EmptyPayload, empty.Status);
        Assert.Null(empty.Frame);
        Assert.Equal(
            DesktopRemoteWindowJpegDecodingStatus.PayloadLimitExceeded,
            oversized.Status);
        Assert.Null(oversized.Frame);
    }

    [Fact]
    public void DecoderRejectsOtherImageFormatsAndAnimatedContent()
    {
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        byte[] animatedGif = Convert.FromBase64String(
            "R0lGODlhAQABAIAAAAAAAP///yH/C05FVFNDQVBFMi4wAwEAAAAh+QQJCgAAACwAAAAAAQABAAACAkQBADs=");

        DesktopRemoteWindowJpegDecodingResult pngResult =
            DesktopRemoteWindowJpegCodec.Decode(png);
        DesktopRemoteWindowJpegDecodingResult gifResult =
            DesktopRemoteWindowJpegCodec.Decode(animatedGif);

        Assert.Equal(
            DesktopRemoteWindowJpegDecodingStatus.UnsupportedFormat,
            pngResult.Status);
        Assert.Equal(
            DesktopRemoteWindowJpegDecodingStatus.UnsupportedFormat,
            gifResult.Status);
        Assert.Null(pngResult.Frame);
        Assert.Null(gifResult.Frame);
    }

    [Fact]
    public void DecoderRejectsMalformedAndTruncatedJpegWithoutPixels()
    {
        byte[] fixture = ReadFixture();
        byte[] truncated = fixture[..^1];
        byte[] malformed = [0xff, 0xd8, 0xff, 0xd9];

        DesktopRemoteWindowJpegDecodingResult truncatedResult =
            DesktopRemoteWindowJpegCodec.Decode(truncated);
        DesktopRemoteWindowJpegDecodingResult malformedResult =
            DesktopRemoteWindowJpegCodec.Decode(malformed);

        Assert.Equal(
            DesktopRemoteWindowJpegDecodingStatus.IncompleteInput,
            truncatedResult.Status);
        Assert.Null(truncatedResult.Frame);
        Assert.Equal(
            DesktopRemoteWindowJpegDecodingStatus.InvalidData,
            malformedResult.Status);
        Assert.Null(malformedResult.Frame);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MarkerWalkerRejectsInvalidSegmentLengths(int segmentLength)
    {
        byte[] encoded =
        [
            0xff, 0xd8,
            0xff, 0xe0,
            (byte)(segmentLength >> 8),
            (byte)segmentLength,
            0xff, 0xd9,
        ];

        DesktopRemoteWindowJpegDecodingStatus status =
            DesktopRemoteWindowJpegCodec.ValidateJpegEnvelope(encoded);

        Assert.Equal(DesktopRemoteWindowJpegDecodingStatus.InvalidData, status);
    }

    [Fact]
    public void MarkerWalkerRejectsSegmentOverrunAsIncompleteInput()
    {
        byte[] encoded =
        [
            0xff, 0xd8,
            0xff, 0xe0,
            0x00, 0x04,
            0x00,
        ];

        DesktopRemoteWindowJpegDecodingStatus status =
            DesktopRemoteWindowJpegCodec.ValidateJpegEnvelope(encoded);

        Assert.Equal(
            DesktopRemoteWindowJpegDecodingStatus.IncompleteInput,
            status);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xd0)]
    [InlineData(0xd7)]
    public void MarkerWalkerRejectsScanOnlyMarkersOutsideEntropyData(int marker)
    {
        byte[] encoded =
        [
            0xff, 0xd8,
            0xff, checked((byte)marker),
            0xff, 0xd9,
        ];

        DesktopRemoteWindowJpegDecodingStatus status =
            DesktopRemoteWindowJpegCodec.ValidateJpegEnvelope(encoded);

        Assert.Equal(DesktopRemoteWindowJpegDecodingStatus.InvalidData, status);
    }

    [Fact]
    public void MarkerWalkerAcceptsFillBytesBetweenMarkers()
    {
        byte[] fixture = ReadFixture();
        byte[] withFill = new byte[fixture.Length + 1];
        fixture.AsSpan(0, 2).CopyTo(withFill);
        withFill[2] = 0xff;
        fixture.AsSpan(2).CopyTo(withFill.AsSpan(3));

        DesktopRemoteWindowJpegDecodingResult result =
            DesktopRemoteWindowJpegCodec.Decode(withFill);

        Assert.True(result.Succeeded);
        Assert.Equal(DesktopRemoteWindowJpegDecodingStatus.Decoded, result.Status);
    }

    [Fact]
    public void DecoderAcceptsProgressiveMultiScanJpegWithStuffedEntropyByte()
    {
        byte[] progressive = Convert.FromBase64String(ProgressiveJpegBase64);
        Assert.True(CountBytePair(progressive, 0xff, 0xda) > 1);
        Assert.True(CountBytePair(progressive, 0xff, 0x00) > 0);

        DesktopRemoteWindowJpegDecodingResult result =
            DesktopRemoteWindowJpegCodec.Decode(progressive);

        Assert.True(result.Succeeded);
        using DesktopRemoteWindowBgraFrame decoded = Assert.IsType<
            DesktopRemoteWindowBgraFrame>(result.Frame);
        Assert.Equal(2, decoded.Width);
        Assert.Equal(2, decoded.Height);
    }

    [Fact]
    public void DecoderAcceptsRestartMarkerAtDeclaredInterval()
    {
        byte[] restarted = Convert.FromBase64String(RestartJpegBase64);
        Assert.Equal(1, CountBytePair(restarted, 0xff, 0xdd));
        Assert.Equal(1, CountBytePair(restarted, 0xff, 0xd0));

        DesktopRemoteWindowJpegDecodingResult result =
            DesktopRemoteWindowJpegCodec.Decode(restarted);

        Assert.True(result.Succeeded);
        using DesktopRemoteWindowBgraFrame decoded = Assert.IsType<
            DesktopRemoteWindowBgraFrame>(result.Frame);
        Assert.Equal(16, decoded.Width);
        Assert.Equal(8, decoded.Height);
    }

    [Fact]
    public void DecoderRejectsASecondJpegAfterTheFirstEndMarker()
    {
        byte[] fixture = ReadFixture();
        byte[] concatenated = [.. fixture, .. fixture];

        DesktopRemoteWindowJpegDecodingResult result =
            DesktopRemoteWindowJpegCodec.Decode(concatenated);

        Assert.Equal(
            DesktopRemoteWindowJpegDecodingStatus.InvalidData,
            result.Status);
        Assert.Null(result.Frame);
    }

    [Fact]
    public void DecoderRejectsNonTopLeftExifOrientation()
    {
        byte[] rotated = AddExifOrientation(ReadFixture(), orientation: 6);

        DesktopRemoteWindowJpegDecodingResult result =
            DesktopRemoteWindowJpegCodec.Decode(rotated);

        Assert.Equal(
            DesktopRemoteWindowJpegDecodingStatus.UnsupportedOrientation,
            result.Status);
        Assert.Null(result.Frame);
    }

    [Fact]
    public void DecoderRejectsDimensionAndPixelBombsBeforePixelAllocation()
    {
        byte[] dimensionBomb = PatchJpegDimensions(
            ReadFixture(),
            width: NativeRemoteWindowFrame.MaximumDimension + 1,
            height: 1);
        byte[] pixelBomb = PatchJpegDimensions(
            ReadFixture(),
            width: 5000,
            height: 4000);

        DesktopRemoteWindowJpegDecodingResult dimensionResult =
            DesktopRemoteWindowJpegCodec.Decode(dimensionBomb);
        DesktopRemoteWindowJpegDecodingResult pixelResult =
            DesktopRemoteWindowJpegCodec.Decode(pixelBomb);

        Assert.Equal(
            DesktopRemoteWindowJpegDecodingStatus.InvalidDimensions,
            dimensionResult.Status);
        Assert.Equal(
            DesktopRemoteWindowJpegDecodingStatus.PixelLimitExceeded,
            pixelResult.Status);
        Assert.Null(dimensionResult.Frame);
        Assert.Null(pixelResult.Frame);
    }

    [Theory]
    [InlineData(1, true, 2, 2, (int)DesktopRemoteWindowJpegDecodingStatus.MultipleFrames)]
    [InlineData(0, false, 2, 2, (int)DesktopRemoteWindowJpegDecodingStatus.UnsupportedOrientation)]
    [InlineData(0, true, 0, 2, (int)DesktopRemoteWindowJpegDecodingStatus.InvalidDimensions)]
    [InlineData(0, true, 4096, 4096, (int)DesktopRemoteWindowJpegDecodingStatus.Decoded)]
    [InlineData(0, true, 4096, 4097, (int)DesktopRemoteWindowJpegDecodingStatus.PixelLimitExceeded)]
    [InlineData(0, true, 5000, 4000, (int)DesktopRemoteWindowJpegDecodingStatus.PixelLimitExceeded)]
    public void MetadataValidationRejectsUnsafeInputBeforeAllocation(
        int frameCount,
        bool topLeft,
        int width,
        int height,
        int expected)
    {
        DesktopRemoteWindowJpegDecodingStatus status =
            DesktopRemoteWindowJpegCodec.ValidateMetadata(
                frameCount,
                topLeft,
                width,
                height);

        Assert.Equal((DesktopRemoteWindowJpegDecodingStatus)expected, status);
    }

    [Fact]
    public void CodecBoundaryDoesNotExposeSkiaTypes()
    {
        Type[] boundaryTypes =
        [
            typeof(DesktopRemoteWindowJpegCodec),
            typeof(DesktopRemoteWindowJpegProfile),
            typeof(DesktopRemoteWindowJpegEncodingResult),
            typeof(DesktopRemoteWindowEncodedJpeg),
            typeof(DesktopRemoteWindowJpegDecodingResult),
            typeof(DesktopRemoteWindowBgraFrame),
        ];

        foreach (Type boundaryType in boundaryTypes)
        {
            Assert.DoesNotContain(
                boundaryType.GetMethods(),
                method => IsSkiaType(method.ReturnType)
                    || method.GetParameters().Any(
                        parameter => IsSkiaType(parameter.ParameterType)));
            Assert.DoesNotContain(
                boundaryType.GetProperties(),
                property => IsSkiaType(property.PropertyType));
        }
    }

    [Fact]
    public void BoundedEncodeStreamClearsItsInternalBufferOnDispose()
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;
        Type streamType = Assert.IsAssignableFrom<Type>(
            typeof(DesktopRemoteWindowJpegCodec).GetNestedType(
                "BoundedSkiaWriteStream",
                System.Reflection.BindingFlags.NonPublic));
        object stream = Assert.IsAssignableFrom<object>(
            Activator.CreateInstance(
                streamType,
                Flags,
                binder: null,
                args: [32],
                culture: null));
        Assert.Equal(streamType, stream.GetType());
        byte[] buffer = Assert.IsType<byte[]>(
            streamType.GetField("buffer", Flags)!.GetValue(stream));
        buffer.AsSpan().Fill(0x5a);

        Assert.IsAssignableFrom<IDisposable>(stream).Dispose();

        Assert.All(buffer, static value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void EncodedOwnerClearsBorrowedPayloadOnIdempotentDispose()
    {
        byte[] payload = Enumerable.Repeat((byte)0x5a, 32).ToArray();
        var encoded = new DesktopRemoteWindowEncodedJpeg(
            payload,
            width: 2,
            height: 2,
            new DesktopRemoteWindowJpegProfile(1, 1, 82));
        ReadOnlyMemory<byte> borrowed = encoded.Payload;

        encoded.Dispose();
        encoded.Dispose();

        Assert.All(borrowed.ToArray(), static value => Assert.Equal((byte)0, value));
        Assert.Throws<ObjectDisposedException>(() => encoded.ExportPayload());
        Assert.Throws<ObjectDisposedException>(() => encoded.Payload.ToArray());
    }

    [Fact]
    public void DecodedOwnerClearsBorrowedPixelsOnIdempotentDispose()
    {
        byte[] pixels = Enumerable.Repeat((byte)0x5a, 16).ToArray();
        var decoded = new DesktopRemoteWindowBgraFrame(
            pixels,
            width: 2,
            height: 2);
        ReadOnlyMemory<byte> borrowed = decoded.Pixels;

        decoded.Dispose();
        decoded.Dispose();

        Assert.All(borrowed.ToArray(), static value => Assert.Equal((byte)0, value));
        Assert.Throws<ObjectDisposedException>(() => decoded.Pixels.ToArray());
    }

    private static NativeRemoteWindowFrame CreateFrame(
        int width,
        int height,
        int stride,
        FrameFiller fill)
    {
        var owner = new ArrayMemoryOwner(checked(stride * height));
        fill(owner.Memory.Span, width, height, stride);
        return NativeRemoteWindowFrame.TakeOwnership(
            owner,
            payloadLength: stride * height,
            width,
            height,
            stride,
            NativeRemoteWindowPixelFormat.Bgra8888,
            ownerGeneration: 1,
            sessionGeneration: 1,
            sourceGeneration: 1,
            geometryRevision: 1,
            sequence: 1);
    }

    private static void FillGradient(
        Span<byte> pixels,
        int width,
        int height,
        int stride,
        byte alphaSeed)
    {
        for (int y = 0; y < height; y++)
        {
            Span<byte> row = pixels.Slice(y * stride, width * 4);
            for (int x = 0; x < width; x++)
            {
                int offset = x * 4;
                row[offset] = (byte)((x * 13 + y * 5) & 0xff);
                row[offset + 1] = (byte)((x * 3 + y * 17) & 0xff);
                row[offset + 2] = (byte)((x * 19 + y * 7) & 0xff);
                row[offset + 3] = alphaSeed == byte.MaxValue
                    ? byte.MaxValue
                    : (byte)((alphaSeed + x * 11 + y * 23) & 0xff);
            }
        }
    }

    private static void FillPadding(
        Span<byte> pixels,
        int width,
        int height,
        int stride,
        byte value)
    {
        int rowBytes = width * 4;
        for (int y = 0; y < height; y++)
        {
            pixels.Slice((y * stride) + rowBytes, stride - rowBytes).Fill(value);
        }
    }

    private static void FillHalfScaleLumaNoise(
        Span<byte> pixels,
        int width,
        int height,
        int stride)
    {
        var random = new Random(0x5f10);
        for (int y = 0; y < height; y += 2)
        {
            for (int x = 0; x < width; x += 2)
            {
                byte value = (byte)random.Next(256);
                for (int row = y; row < Math.Min(y + 2, height); row++)
                {
                    for (int column = x; column < Math.Min(x + 2, width); column++)
                    {
                        int offset = (row * stride) + (column * 4);
                        pixels[offset] = value;
                        pixels[offset + 1] = value;
                        pixels[offset + 2] = value;
                        pixels[offset + 3] = byte.MaxValue;
                    }
                }
            }
        }
    }

    private static void AssertAllAlphaOpaque(ReadOnlySpan<byte> pixels)
    {
        for (int offset = 3; offset < pixels.Length; offset += 4)
        {
            Assert.Equal(byte.MaxValue, pixels[offset]);
        }
    }

    private static int CountBytePair(
        ReadOnlySpan<byte> source,
        byte first,
        byte second)
    {
        int count = 0;
        for (int index = 0; index < source.Length - 1; index++)
        {
            if (source[index] == first && source[index + 1] == second)
            {
                count++;
            }
        }

        return count;
    }

    private static string ComputeSha256(ReadOnlySpan<byte> source) =>
        Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();

    private static byte[] AddExifOrientation(byte[] jpeg, ushort orientation)
    {
        Assert.True(jpeg.Length >= 2);
        Assert.Equal(0xff, jpeg[0]);
        Assert.Equal(0xd8, jpeg[1]);
        byte[] app1 =
        [
            0xff, 0xe1, 0x00, 0x22,
            0x45, 0x78, 0x69, 0x66, 0x00, 0x00,
            0x49, 0x49, 0x2a, 0x00,
            0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01,
            0x03, 0x00,
            0x01, 0x00, 0x00, 0x00,
            (byte)orientation, (byte)(orientation >> 8), 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        byte[] result = GC.AllocateUninitializedArray<byte>(
            checked(jpeg.Length + app1.Length));
        jpeg.AsSpan(0, 2).CopyTo(result);
        app1.CopyTo(result, 2);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + app1.Length));
        return result;
    }

    private static byte[] ReadFixture() =>
        File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureName));

    private static byte[] PatchJpegDimensions(
        byte[] source,
        int width,
        int height)
    {
        byte[] patched = source.ToArray();
        for (int index = 0; index <= patched.Length - 9; index++)
        {
            if (patched[index] != 0xff
                || patched[index + 1] is not (0xc0 or 0xc1 or 0xc2))
            {
                continue;
            }

            patched[index + 5] = checked((byte)(height >> 8));
            patched[index + 6] = (byte)(height & 0xff);
            patched[index + 7] = checked((byte)(width >> 8));
            patched[index + 8] = (byte)(width & 0xff);
            return patched;
        }

        throw new InvalidDataException("The fixed JPEG fixture has no SOF marker.");
    }

    private static bool IsSkiaType(Type type) =>
        type.Namespace?.StartsWith("SkiaSharp", StringComparison.Ordinal) == true;

    private sealed class ArrayMemoryOwner(int length) : IMemoryOwner<byte>
    {
        private byte[]? pixels = GC.AllocateUninitializedArray<byte>(length);

        public Memory<byte> Memory => pixels
            ?? throw new ObjectDisposedException(nameof(ArrayMemoryOwner));

        public void Dispose() => pixels = null;
    }
}
