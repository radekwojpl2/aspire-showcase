using AspireShowcase.BusinessSetup.Domain;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.BuildingBlocks.Domain;

namespace AspireShowcase.BusinessSetup.Tests;

/// <summary>What the booking page shows about a business (V1-6): its address, description and logo.</summary>
public class BusinessPageTests
{
    static Business Start() =>
        Business.Start("Anna's Hair", "anna-hair", "Europe/Warsaw", "hello@anna-hair.example", "owner-1", DateTimeOffset.UtcNow);

    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    [Fact]
    public void The_address_keeps_its_lines_and_blank_text_is_none()
    {
        var business = Start();

        business.ChangePage(" Main Street 1\r\n00-001 Warsaw ", "   ");

        Assert.Equal("Main Street 1\n00-001 Warsaw", business.Address);
        Assert.Null(business.Description);
    }

    [Fact]
    public void A_description_that_is_too_long_is_refused_and_nothing_changes()
    {
        var business = Start();
        business.ChangePage("Main Street 1", "Hair for everyone.");

        var errors = Assert.Throws<DomainValidationException>(
            () => business.ChangePage("Elsewhere", new string('a', Business.MaxDescriptionLength + 1))).Errors;

        Assert.True(errors.ContainsKey("description"));
        Assert.Equal(("Main Street 1", "Hair for everyone."), (business.Address, business.Description));
    }

    [Fact]
    public void Each_new_logo_gets_a_new_version_and_removing_it_leaves_none()
    {
        var business = Start();

        business.LogoChanged();
        business.LogoChanged();
        var second = business.LogoVersion;
        business.LogoRemoved();

        Assert.Equal(2, second);
        Assert.Null(business.LogoVersion);
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 0 }, "image/webp")]
    public void A_logo_is_served_as_the_format_its_bytes_are(byte[] image, string contentType)
    {
        Assert.Equal(contentType, BusinessLogo.For(BusinessId.New(), image).ContentType);
    }

    [Fact]
    public void An_svg_or_other_file_is_not_taken_as_a_logo()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

        var errors = Assert.Throws<DomainValidationException>(() => BusinessLogo.For(BusinessId.New(), svg)).Errors;

        Assert.True(errors.ContainsKey("logo"));
    }

    [Fact]
    public void A_logo_over_the_size_limit_is_refused()
    {
        var image = new byte[BusinessLogo.MaxBytes + 1];
        Png.CopyTo(image, 0);

        Assert.Throws<DomainValidationException>(() => BusinessLogo.For(BusinessId.New(), image));
    }
}
