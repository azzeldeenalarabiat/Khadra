using System.Collections.Concurrent;
using System.Reflection;
using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.Common.Behaviors;

// Runs every FluentValidation validator registered for the request. On failure it short-circuits with
// a Result failure carrying field-level details, so handlers only ever see well-formed input.
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly ConcurrentDictionary<Type, Func<Error, object>?> FailureFactories = new();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (!validators.Any())
            return await next(cancellationToken);

        var context = new ValidationContext<TRequest>(request);
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            if (!result.IsValid)
                failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
            return await next(cancellationToken);

        var details = failures
            .GroupBy(failure => ToCamelCase(failure.PropertyName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var error = Error.ValidationDetails(details);
        var factory = FailureFactories.GetOrAdd(typeof(TResponse), CreateFailureFactory)
            ?? throw new InvalidOperationException(
                $"{typeof(TRequest).Name} failed validation but {typeof(TResponse).Name} is not a Result type.");

        return (TResponse)factory(error);
    }

    private static Func<Error, object>? CreateFailureFactory(Type responseType)
    {
        if (responseType == typeof(UnitResult<Error>))
            return error => UnitResult.Failure(error);

        if (responseType.IsGenericType &&
            responseType.GetGenericTypeDefinition() == typeof(Result<,>) &&
            responseType.GetGenericArguments()[1] == typeof(Error))
        {
            var valueType = responseType.GetGenericArguments()[0];
            var method = typeof(Result)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(candidate =>
                    candidate.Name == nameof(Result.Failure) &&
                    candidate.GetGenericArguments().Length == 2 &&
                    candidate.GetParameters().Length == 1 &&
                    candidate.GetParameters()[0].ParameterType.IsGenericParameter)
                .MakeGenericMethod(valueType, typeof(Error));
            return error => method.Invoke(null, [error])!;
        }

        return null;
    }

    private static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName) || char.IsLower(propertyName[0]))
            return propertyName;
        return char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
    }
}
