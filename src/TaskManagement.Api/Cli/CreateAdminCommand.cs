using TaskManagement.Application.Members;
using TaskManagement.Domain.Access;
using TaskManagement.Infrastructure.Email;

namespace TaskManagement.Api.Cli;

/// <summary>
/// One-time setup of the first Admin (FR-ADM-03), run by an operator — there is no public endpoint for it:
/// <c>dotnet TaskManagement.Api.dll create-admin --email admin@example.com --name "Ada Admin" [--time-zone Asia/Kolkata]</c>.
/// The Admin receives an invite email to set their password; the link is never printed.
/// </summary>
internal static class CreateAdminCommand
{
    public const string Name = "create-admin";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var email = Option(args, "--email");
        var name = Option(args, "--name");
        var timeZone = Option(args, "--time-zone") ?? "UTC";
        if (email is null || name is null)
        {
            await Console.Error.WriteLineAsync("Usage: create-admin --email <email> --name \"<full name>\" [--time-zone <IANA id>]");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<MemberCommands>();
        var result = await commands.CreateAsync(new CreateMemberCommand(email, name, SystemRoles.AdminId, timeZone, null), CancellationToken.None);
        if (result.IsFailure)
        {
            await Console.Error.WriteLineAsync($"Could not create the Admin: {result.Error!.Message}");
            foreach (var (field, messages) in result.Error.FieldErrors ?? new Dictionary<string, string[]>())
            {
                await Console.Error.WriteLineAsync($"  {field}: {string.Join(" ", messages)}");
            }

            return 1;
        }

        var sent = await services.GetRequiredService<EmailDeliveryWorker>().ProcessDueAsync(CancellationToken.None);
        Console.WriteLine($"Admin {result.Value.Email} created. Invite emails processed: {sent}. Check the email delivery status if it did not arrive.");
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
