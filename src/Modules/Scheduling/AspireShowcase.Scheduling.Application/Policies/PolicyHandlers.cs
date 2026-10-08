using AspireShowcase.BuildingBlocks.Domain;

namespace AspireShowcase.Scheduling.Application.Policies;

// The owner's cancellation policy (user story V1-3). New bookings get it; bookings already made
// keep the one they were made under.

sealed class GetCancellationPolicy(IBusinessDirectory directory, ISchedulingDbContext db, BusinessScope scope)
{
    public async Task<Result<CancellationPolicyBody>> HandleAsync(string ownerId, CancellationToken cancellation) =>
        await directory.FindOwnedAsync(ownerId, scope, cancellation) is { } business
            ? ToBody(await db.PolicyOfAsync(business.Id, cancellation))
            : Result.NotFound();

    public static CancellationPolicyBody ToBody(CancellationPolicy policy) => new((int)policy.Notice.TotalHours);
}

sealed class SetCancellationPolicy(
    IBusinessDirectory directory, ISchedulingDbContext db, BusinessScope scope, SchedulingTelemetry telemetry,
    TimeProvider time)
{
    public async Task<Result<CancellationPolicyBody>> HandleAsync(
        CancellationPolicyBody request, string ownerId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("cancellation_policy.set");

        if (await directory.FindOwnedAsync(ownerId, scope, cancellation) is not { } business)
        {
            return Result.NotFound();
        }

        // A business's first policy is new; later ones change the one it has.
        var policy = await db.CancellationPolicies.FindAsync([business.Id], cancellation);
        var isNew = policy is null;
        policy ??= CancellationPolicy.None(business.Id);
        try
        {
            policy.Change(request.NoticeHours, time.GetUtcNow());
        }
        catch (DomainValidationException exception)
        {
            telemetry.PolicyChanged(activity, null, "invalid");
            return Result.Invalid(exception);
        }

        if (isNew)
        {
            db.CancellationPolicies.Add(policy);
        }
        await db.SaveChangesAsync(cancellation);
        telemetry.PolicyChanged(activity, policy, "saved");
        return GetCancellationPolicy.ToBody(policy);
    }
}
