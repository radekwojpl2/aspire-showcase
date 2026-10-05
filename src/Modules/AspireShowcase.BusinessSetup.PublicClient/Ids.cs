namespace AspireShowcase.BusinessSetup.PublicClient;

// The IDs of Business Setup's aggregates. Other aggregates refer to them only by these, never by
// object, and a typed ID can't be mixed up with another: a ServiceId doesn't fit where a
// StaffMemberId belongs. They're public because other modules (Scheduling) refer to the same
// aggregates. Each wraps a version 7 GUID, which sorts by creation time.

/// <summary>The ID of a business.</summary>
public readonly record struct BusinessId(Guid Value)
{
    public static BusinessId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>The ID of a service a business offers.</summary>
public readonly record struct ServiceId(Guid Value)
{
    public static ServiceId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>The ID of someone on a business's staff.</summary>
public readonly record struct StaffMemberId(Guid Value)
{
    public static StaffMemberId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
