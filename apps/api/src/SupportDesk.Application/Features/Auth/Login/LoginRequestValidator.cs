using FluentValidation;
using SupportDesk.Application.Contracts.Auth;

namespace SupportDesk.Application.Features.Auth.Login;

/// <summary>Only that both values are present: whether they are right is never revealed by a 400.</summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter your email address.");

        RuleFor(x => x.Password).NotEmpty().WithMessage("Enter your password.");
    }
}
