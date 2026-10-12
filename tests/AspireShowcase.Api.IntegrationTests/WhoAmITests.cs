using System.Net;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// GET /me says who's signed in, for clients such as the MCP server whose tokens carry only the
/// user's ID: their name and email from their account, and the business they own.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class WhoAmITests(ApiFactory api)
{
    [Fact]
    public async Task A_client_gets_their_name_and_email_and_no_business()
    {
        var client = TestUser.Client();

        var me = await TestBusiness.ReadAsync<Me>(await api.CreateClient(client).GetAsync("/api/me"));

        Assert.Equal($"User {client.Id}", me.Name);
        Assert.Equal(FakeUserProfiles.EmailOf(client.Id), me.Email);
        Assert.Null(me.Business);
    }

    [Fact]
    public async Task An_owner_also_gets_the_business_they_own()
    {
        var business = await TestBusiness.StartAsync(api);

        var me = await TestBusiness.ReadAsync<Me>(await api.CreateClient(business.Owner).GetAsync("/api/me"));

        Assert.Equal(new OwnedBusiness("Test salon", business.Slug), me.Business);
    }

    [Fact]
    public async Task Nobody_is_told_who_they_are_without_signing_in()
    {
        var response = await api.CreateClient().GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
