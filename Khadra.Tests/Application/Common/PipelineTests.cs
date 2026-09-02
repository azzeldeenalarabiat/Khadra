using System.Reflection;
using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Behaviors;
using Khadra.Application.IdentityAccess.Login;
using Khadra.Application.IdentityAccess.VerifyEmail;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Tests.Application.Common;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Invalid_command_short_circuits_with_field_details_for_result_responses()
    {
        var behavior = new ValidationBehavior<LoginCommand, Result<string, Error>>([new LoginCommandValidator()]);
        var command = new LoginCommand(string.Empty, string.Empty, ClientInfo.Unknown);
        var handlerCalled = false;

        var result = await behavior.Handle(command, _ =>
        {
            handlerCalled = true;
            return Task.FromResult(Result.Success<string, Error>("handled"));
        }, CancellationToken.None);

        Assert.False(handlerCalled);
        Assert.True(result.IsFailure);
        Assert.Equal("validation.failed", result.Error.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("email", result.Error.Details!.Keys);
        Assert.Contains("password", result.Error.Details!.Keys);
    }

    [Fact]
    public async Task Invalid_command_short_circuits_for_unit_result_responses()
    {
        var behavior = new ValidationBehavior<VerifyEmailCommand, UnitResult<Error>>([new VerifyEmailCommandValidator()]);

        var result = await behavior.Handle(
            new VerifyEmailCommand(string.Empty),
            _ => Task.FromResult(UnitResult.Success<Error>()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("token", result.Error.Details!.Keys);
    }

    [Fact]
    public async Task Valid_command_reaches_the_handler()
    {
        var behavior = new ValidationBehavior<VerifyEmailCommand, UnitResult<Error>>([new VerifyEmailCommandValidator()]);

        var result = await behavior.Handle(
            new VerifyEmailCommand("token"),
            _ => Task.FromResult(UnitResult.Success<Error>()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}

public sealed class CqrsWiringTests
{
    [Fact]
    public void Every_command_and_query_has_exactly_one_handler()
    {
        var assembly = typeof(ICommand<>).Assembly;
        var requestTypes = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType &&
                (contract.GetGenericTypeDefinition() == typeof(ICommand<>) || contract.GetGenericTypeDefinition() == typeof(IQuery<>))))
            .ToList();
        var handlerContracts = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
            .ToList();

        Assert.NotEmpty(requestTypes);
        foreach (var requestType in requestTypes)
        {
            var handlers = handlerContracts.Count(contract => contract.GetGenericArguments()[0] == requestType);
            Assert.True(handlers == 1, $"{requestType.Name} has {handlers} handlers.");
        }
    }

    [Fact]
    public void Every_command_with_user_input_has_a_validator()
    {
        var assembly = typeof(ICommand<>).Assembly;
        var validated = assembly.GetTypes()
            .Where(type => type.BaseType is { IsGenericType: true } baseType && baseType.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
            .Select(type => type.BaseType!.GetGenericArguments()[0])
            .ToHashSet();
        var commands = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => type.GetInterfaces().Any(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommand<>)))
            .Where(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(property => property.PropertyType == typeof(string)))
            .ToList();

        foreach (var command in commands)
            Assert.True(validated.Contains(command), $"{command.Name} has no validator.");
    }
}
