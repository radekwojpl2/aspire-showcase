using AspireShowcase.BusinessSetup;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.BusinessSetup.Tests;

/// <summary>The rules of starting a business (MVP-8) and of its contact email (V1-3).</summary>
public class BusinessTests
{
    static Business Start(string? contactEmail) =>
        Business.Start("Anna's Hair", "anna-hair", "Europe/Warsaw", contactEmail, "owner-1", DateTimeOffset.UtcNow);

    [Fact]
    public void A_new_business_has_the_contact_email_it_was_started_with()
    {
        Assert.Equal("hello@anna-hair.example", Start("  hello@anna-hair.example ").ContactEmail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an email")]
    public void A_business_cannot_be_started_without_a_contact_email(string? contactEmail)
    {
        var errors = Assert.Throws<DomainValidationException>(() => Start(contactEmail)).Errors;

        Assert.True(errors.ContainsKey("contactEmail"));
    }

    [Fact]
    public void The_contact_email_can_be_changed_but_not_removed()
    {
        var business = Start("hello@anna-hair.example");

        business.ChangeContactEmail("bookings@anna-hair.example");
        Assert.Throws<DomainValidationException>(() => business.ChangeContactEmail(" "));

        Assert.Equal("bookings@anna-hair.example", business.ContactEmail);
    }
}
