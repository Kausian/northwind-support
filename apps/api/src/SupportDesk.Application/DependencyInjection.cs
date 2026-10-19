using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SupportDesk.Application.Features.Agents.Queries.GetAgents;
using SupportDesk.Application.Features.Categories.Queries.GetCategories;
using SupportDesk.Application.Features.Customers.Queries.GetCustomer;
using SupportDesk.Application.Features.Customers.Queries.GetCustomers;
using SupportDesk.Application.Features.Tickets.Commands.AssignTicket;
using SupportDesk.Application.Features.Tickets.Commands.ChangeTicketStatus;
using SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Application.Features.Tickets.Queries.SearchTickets;
using SupportDesk.Domain.Triage;

namespace SupportDesk.Application;

/// <summary>
/// Registers the command and query handlers and the request validators.
/// </summary>
public static class DependencyInjection
{
    /// <remarks>
    /// Triage needs a <see cref="SlaPolicy"/>, built from configuration by the host.
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Stateless rules: one instance serves every request.
        services.AddSingleton<TicketTriage>();

        services.AddScoped<RaiseTicketCommandHandler>();
        services.AddScoped<ChangeTicketStatusCommandHandler>();
        services.AddScoped<AssignTicketCommandHandler>();
        services.AddScoped<SearchTicketsQueryHandler>();
        services.AddScoped<GetTicketQueryHandler>();

        services.AddScoped<GetCustomersQueryHandler>();
        services.AddScoped<GetCustomerQueryHandler>();

        services.AddScoped<GetAgentsQueryHandler>();

        services.AddScoped<GetCategoriesQueryHandler>();

        services.AddValidatorsFromAssemblyContaining<RaiseTicketCommandHandler>(ServiceLifetime.Singleton);

        return services;
    }
}
