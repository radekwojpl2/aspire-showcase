using AspireShowcase.BusinessSetup;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.BuildingBlocks.Domain;

namespace AspireShowcase.BusinessSetup.Tests;

/// <summary>The rules of user story MVP-10, on the Service aggregate and the Money value object.</summary>
public class ServiceTests
{
    static readonly BusinessId BusinessId = BusinessId.New();

    static Service Haircut() => Service.Add(BusinessId, "Haircut", 45, null, 120m, "PLN", DateTimeOffset.UtcNow);

    static IReadOnlyDictionary<string, string[]> ErrorsOf(Action change) =>
        Assert.Throws<DomainValidationException>(change).Errors;

    [Fact]
    public void A_new_service_has_its_name_duration_and_price_and_is_offered()
    {
        var service = Service.Add(BusinessId, "  Haircut ", 45, null, 120m, "PLN", DateTimeOffset.UtcNow);

        Assert.Equal("Haircut", service.Name);
        Assert.Equal(TimeSpan.FromMinutes(45), service.Duration);
        Assert.Equal(Money.Create(120m, "PLN"), service.Price);
        Assert.False(service.IsHidden);
        Assert.Equal(BusinessId, service.BusinessId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(8 * 60 + 5)]
    public void Durations_come_in_5_minute_steps_up_to_8_hours(int minutes)
    {
        var errors = ErrorsOf(() => Service.Add(BusinessId, "Haircut", minutes, null, 120m, "PLN", DateTimeOffset.UtcNow));

        Assert.True(errors.ContainsKey("durationMinutes"));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(90)]
    [InlineData(8 * 60)]
    public void Durations_on_the_steps_are_accepted(int minutes)
    {
        var service = Service.Add(BusinessId, "Haircut", minutes, null, 120m, "PLN", DateTimeOffset.UtcNow);

        Assert.Equal(TimeSpan.FromMinutes(minutes), service.Duration);
    }

    [Fact]
    public void A_service_has_no_buffer_unless_given_one()
    {
        Assert.Equal(TimeSpan.Zero, Haircut().Buffer);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(2 * 60)]
    public void Buffers_come_in_5_minute_steps_up_to_2_hours(int minutes)
    {
        var service = Service.Add(BusinessId, "Haircut", 45, minutes, 120m, "PLN", DateTimeOffset.UtcNow);

        Assert.Equal(TimeSpan.FromMinutes(minutes), service.Buffer);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(7)]
    [InlineData(2 * 60 + 5)]
    public void Other_buffers_are_refused(int minutes)
    {
        var errors = ErrorsOf(() => Service.Add(BusinessId, "Haircut", 45, minutes, 120m, "PLN", DateTimeOffset.UtcNow));

        Assert.True(errors.ContainsKey("bufferMinutes"));
    }

    [Fact]
    public void A_free_service_costs_0()
    {
        var service = Service.Add(BusinessId, "Consultation", 15, null, 0m, "EUR", DateTimeOffset.UtcNow);

        Assert.Equal(0m, service.Price.Amount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(12.345)]
    [InlineData(100_000.01)]
    public void A_price_is_from_0_with_at_most_two_decimals(double amount)
    {
        var errors = ErrorsOf(() => Service.Add(BusinessId, "Haircut", 45, null, (decimal)amount, "PLN", DateTimeOffset.UtcNow));

        Assert.True(errors.ContainsKey("price"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pln")]
    [InlineData("XYZ")]
    public void The_currency_has_to_be_an_ISO_4217_code(string? currency)
    {
        var errors = ErrorsOf(() => Service.Add(BusinessId, "Haircut", 45, null, 120m, currency, DateTimeOffset.UtcNow));

        Assert.True(errors.ContainsKey("currency"));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var errors = ErrorsOf(() => Service.Add(BusinessId, "", 7, null, null, "XYZ", DateTimeOffset.UtcNow));

        Assert.Equal(["currency", "durationMinutes", "name", "price"], errors.Keys.Order());
    }

    [Fact]
    public void Changing_a_service_replaces_its_name_duration_and_price()
    {
        var service = Haircut();

        service.Change("Long haircut", 60, null, 150m, "PLN");

        Assert.Equal("Long haircut", service.Name);
        Assert.Equal(TimeSpan.FromMinutes(60), service.Duration);
        Assert.Equal(Money.Create(150m, "PLN"), service.Price);
    }

    [Fact]
    public void A_refused_change_leaves_the_service_as_it_was()
    {
        var service = Haircut();

        ErrorsOf(() => service.Change("Long haircut", 61, null, 150m, "PLN"));

        Assert.Equal("Haircut", service.Name);
        Assert.Equal(TimeSpan.FromMinutes(45), service.Duration);
    }

    [Fact]
    public void A_service_can_be_hidden_without_deleting_it_and_shown_again()
    {
        var service = Haircut();

        service.Hide();
        Assert.True(service.IsHidden);
        Assert.Equal("Haircut", service.Name);

        service.Show();
        Assert.False(service.IsHidden);
    }

    [Fact]
    public void Money_with_the_same_amount_and_currency_is_equal()
    {
        Assert.Equal(Money.Create(120m, "PLN"), Money.Create(120.00m, "PLN"));
        Assert.NotEqual(Money.Create(120m, "PLN"), Money.Create(120m, "EUR"));
    }
}
