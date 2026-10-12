using System.Net.Http.Json;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// The list of businesses, for finding one to book with: public like the booking pages, and
/// narrowed down by text in a business's name or description.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ListBusinessesTests(ApiFactory api)
{
    [Fact]
    public async Task Anyone_can_list_the_businesses_without_signing_in()
    {
        var business = await TestBusiness.StartAsync(api);

        var listed = await ListAsync(query: null);

        var found = Assert.Single(listed, listing => listing.Slug == business.Slug);
        Assert.Equal("Test salon", found.Name);
    }

    [Fact]
    public async Task A_query_finds_the_businesses_with_it_in_their_name_whatever_the_case()
    {
        var word = $"Barber{Guid.NewGuid():N}";
        var slug = await StartAsync($"The {word} Shop");
        await TestBusiness.StartAsync(api);

        var listed = await ListAsync(word.ToUpperInvariant());

        Assert.Equal([slug], listed.Select(listing => listing.Slug));
    }

    [Fact]
    public async Task A_query_also_finds_the_businesses_with_it_in_their_description()
    {
        var word = $"balayage{Guid.NewGuid():N}";
        var slug = await StartAsync("Colour studio", description: $"Cuts and {word} in the old town.");

        var listed = await ListAsync(word);

        Assert.Equal([slug], listed.Select(listing => listing.Slug));
    }

    async Task<List<BusinessListing>> ListAsync(string? query) =>
        await TestBusiness.ReadAsync<List<BusinessListing>>(await api.CreateClient()
            .GetAsync(query is null ? "/api/public/businesses" : $"/api/public/businesses?query={Uri.EscapeDataString(query)}"));

    async Task<string> StartAsync(string name, string? description = null)
    {
        var http = api.CreateClient(TestUser.Owner());
        var slug = $"test-{Guid.NewGuid():N}";
        await TestBusiness.ReadAsync<BusinessResponse>(await http.PostAsJsonAsync("/api/businesses",
            new { name, slug, timeZone = TestBusiness.TimeZone, contactEmail = "hello@salon.example", ownerName = "Olivia Owner" }));
        if (description is not null)
        {
            await TestBusiness.ReadAsync<object>(await http.PutAsJsonAsync("/api/businesses/mine/page", new { description }));
        }

        return slug;
    }
}
