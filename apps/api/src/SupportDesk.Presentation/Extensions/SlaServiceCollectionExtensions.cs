using Microsoft.Extensions.Options;
using SupportDesk.Domain.Triage;

namespace SupportDesk.Presentation.Extensions;

public static class SlaServiceCollectionExtensions
{
    /// <summary>
    /// Binds the <c>Sla</c> configuration section and registers the <see cref="SlaPolicy"/>
    /// built from it. Invalid or missing settings stop the API at startup, naming the bad key,
    /// rather than failing the first ticket that is raised.
    /// </summary>
    public static IServiceCollection AddSlaPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SlaSettings>()
            .Bind(configuration.GetSection(SlaSettings.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<SlaSettings>, SlaSettingsValidator>();

        services.AddSingleton(sp => new SlaPolicy(sp.GetRequiredService<IOptions<SlaSettings>>().Value));

        return services;
    }

    /// <summary>Reports the domain's own checks through the options pipeline.</summary>
    private sealed class SlaSettingsValidator : IValidateOptions<SlaSettings>
    {
        public ValidateOptionsResult Validate(string? name, SlaSettings options)
        {
            var errors = options.Validate();

            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }
    }
}
