using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class ContactRequestValidator : AbstractValidator<ContactRequest>
{
    private static readonly HashSet<string> ValidSubjects = ["demo", "sales", "support", "security", "other"];

    public ContactRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320).EmailAddress();
        RuleFor(x => x.Subject).NotEmpty().Must(s => ValidSubjects.Contains(s))
            .WithMessage("Subject must be one of: demo, sales, support, security, other");
        RuleFor(x => x.Message).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.Company).MaximumLength(200).When(x => x.Company is not null);
    }
}
