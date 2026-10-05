/// <summary>A business on the platform, booked at /book/{Slug}.</summary>
sealed class Business
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>The business's part of the booking link: lowercase letters, digits and hyphens.</summary>
    public required string Slug { get; set; }

    /// <summary>The Logto user ID (sub) of the owner. One business per owner for now.</summary>
    public required string OwnerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
