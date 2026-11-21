using FluentValidation;

namespace Application.Identity;

public static class PasswordRules
{
    public const int MinLength = 12;
    public const int MaxLength = 128;
}

internal static class IdentityRuleExtensions
{
    public static IRuleBuilderOptions<T, string> StaffEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(256).EmailAddress().WithMessage("Enter a valid email address.");

    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(PasswordRules.MinLength).WithMessage($"Password must be at least {PasswordRules.MinLength} characters.")
            .MaximumLength(PasswordRules.MaxLength);
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // Only shape checks: never reveal anything about whether the account exists.
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength);
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(PasswordRules.MaxLength);
        RuleFor(x => x.NewPassword).NewPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must be different from the current one.");
    }
}

public sealed class UserListRequestValidator : AbstractValidator<UserListRequest>
{
    public static readonly string[] Sorts = ["name", "-name", "email", "-email", "lastLogin", "-lastLogin"];

    public UserListRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Sort).Must(x => x is null || Sorts.Contains(x)).WithMessage("Sort must be one of: " + string.Join(", ", Sorts) + ".");
        RuleFor(x => x.Q).MaximumLength(100);
    }
}

public sealed class UserCreateInputValidator : AbstractValidator<UserCreateInput>
{
    public UserCreateInputValidator()
    {
        RuleFor(x => x.Email).StaffEmail();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TemporaryPassword).NewPassword()
            .Must((input, password) => !string.Equals(password, input.Email, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Password cannot be the email address.");
        RuleFor(x => x.RoleIds).Must(x => x is null || x.Count <= 20 && x.Distinct().Count() == x.Count).WithMessage("Roles must be distinct.");
    }
}

public sealed class UserUpdateInputValidator : AbstractValidator<UserUpdateInput>
{
    public UserUpdateInputValidator()
    {
        RuleFor(x => x.Email).StaffEmail();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
    }
}

public sealed class ResetPasswordInputValidator : AbstractValidator<ResetPasswordInput>
{
    public ResetPasswordInputValidator() => RuleFor(x => x.TemporaryPassword).NewPassword();
}

public sealed class UserRolesInputValidator : AbstractValidator<UserRolesInput>
{
    public UserRolesInputValidator() =>
        RuleFor(x => x.RoleIds).NotNull().Must(x => x.Count <= 20 && x.Distinct().Count() == x.Count).WithMessage("Roles must be distinct.");
}

public sealed class UserPermissionsInputValidator : AbstractValidator<UserPermissionsInput>
{
    public UserPermissionsInputValidator()
    {
        RuleFor(x => x.Overrides).NotNull()
            .Must(x => x.Select(o => o.Key).Distinct().Count() == x.Count).WithMessage("Each permission can appear only once.");
        RuleForEach(x => x.Overrides).ChildRules(o =>
        {
            o.RuleFor(x => x.Key).Must(PermissionKeys.IsKnown).WithMessage("Unknown permission.");
            o.RuleFor(x => x.State).IsInEnum();
        });
    }
}

public sealed class RoleInputValidator : AbstractValidator<RoleInput>
{
    public RoleInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60).Matches("^[A-Za-z0-9 _-]+$").WithMessage("Use letters, numbers, spaces, - or _.");
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.Permissions).NotNull().Must(x => x.Distinct().Count() == x.Count).WithMessage("Each permission can appear only once.");
        RuleForEach(x => x.Permissions).Must(PermissionKeys.IsKnown).WithMessage("Unknown permission.");
    }
}
