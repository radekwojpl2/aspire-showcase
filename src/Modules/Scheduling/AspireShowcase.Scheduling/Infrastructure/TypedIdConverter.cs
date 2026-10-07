using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AspireShowcase.Scheduling;

/// <summary>
/// Stores a typed ID, such as <c>BusinessId</c>, as the <see cref="Guid"/> it wraps: the column
/// stays a plain uuid. Works for any record struct with a <c>Value</c> property and a constructor
/// that takes the Guid. A copy of Business Setup's: modules don't share infrastructure.
/// </summary>
sealed class TypedIdConverter<TId>() : ValueConverter<TId, Guid>(ToGuid(), FromGuid())
    where TId : struct
{
    // Built as expressions, so EF Core can also translate them into SQL parameters.
    static Expression<Func<TId, Guid>> ToGuid()
    {
        var id = Expression.Parameter(typeof(TId), "id");
        return Expression.Lambda<Func<TId, Guid>>(Expression.Property(id, "Value"), id);
    }

    static Expression<Func<Guid, TId>> FromGuid()
    {
        var value = Expression.Parameter(typeof(Guid), "value");
        var constructor = typeof(TId).GetConstructor([typeof(Guid)])
            ?? throw new InvalidOperationException($"{typeof(TId).Name} has no constructor that takes a Guid.");
        return Expression.Lambda<Func<Guid, TId>>(Expression.New(constructor, value), value);
    }
}
