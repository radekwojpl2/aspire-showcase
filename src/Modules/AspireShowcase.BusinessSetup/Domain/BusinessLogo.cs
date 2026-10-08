using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.BusinessSetup;

/// <summary>
/// The logo on a business's booking page (user story V1-6): a PNG, JPEG or WebP image of at most
/// <see cref="MaxBytes"/>. Part of the <see cref="Business"/> it belongs to, but stored on its
/// own, so the image is only read when someone asks for it.
/// </summary>
/// <remarks>
/// The format is read from the image's first bytes, not from what the browser said it is, and
/// it's served with that type. SVG isn't taken: it can carry script.
/// </remarks>
sealed class BusinessLogo
{
    public const int MaxBytes = 512 * 1024;

    static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    // For EF Core.
    BusinessLogo()
    {
    }

    public BusinessId BusinessId { get; private set; }

    public byte[] Image { get; private set; } = [];

    /// <summary>image/png, image/jpeg or image/webp.</summary>
    public string ContentType { get; private set; } = "";

    /// <exception cref="DomainValidationException">It's empty, too big, or not a PNG, JPEG or WebP image.</exception>
    public static BusinessLogo For(BusinessId businessId, byte[] image)
    {
        var logo = new BusinessLogo { BusinessId = businessId };
        logo.Replace(image);
        return logo;
    }

    /// <exception cref="DomainValidationException">It's empty, too big, or not a PNG, JPEG or WebP image.</exception>
    public void Replace(byte[] image)
    {
        var errors = new DomainErrors();
        var contentType = ContentTypeOf(image);
        if (image.Length > MaxBytes)
        {
            errors.Add("logo", $"Use an image of at most {MaxBytes / 1024} KB.");
        }
        else if (contentType is null)
        {
            errors.Add("logo", "Use a PNG, JPEG or WebP image.");
        }
        errors.ThrowIfAny();

        Image = image;
        ContentType = contentType!;
    }

    /// <summary>What the image is by its signature, or null when it's none of the formats taken.</summary>
    static string? ContentTypeOf(ReadOnlySpan<byte> image) =>
        image.StartsWith(PngSignature) ? "image/png"
        : image.StartsWith(JpegSignature) ? "image/jpeg"
        : image.Length >= 12 && image.StartsWith("RIFF"u8) && image[8..12].SequenceEqual("WEBP"u8) ? "image/webp"
        : null;
}
