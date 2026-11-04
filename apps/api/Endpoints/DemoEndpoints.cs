using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Microsoft.Extensions.Options;

namespace Careeroc.Api.Endpoints;

public sealed record DemoWorkspaceDto(bool Created, int EmailsImported, int ApplicationsCreated, int RemindersCreated);

public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost(
                "/api/demo/workspace",
                async (
                    HttpRequest request,
                    DemoWorkspaceService demoWorkspaceService,
                    CareerOrchestratorService careerOrchestratorService,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var userEmail = RequestUserContext.GetUserEmail(request);
                        var result = await demoWorkspaceService.EnsureWorkspaceAsync(userEmail, cancellationToken);
                        if (result.Created)
                        {
                            // A first multi-agent review gives the visitor a real run, trace, and memory proposals to inspect.
                            await careerOrchestratorService.ReviewResumeMailboxFitAsync(userEmail, cancellationToken);
                        }

                        return Results.Ok(
                            new DemoWorkspaceDto(
                                result.Created,
                                result.EmailsImported,
                                result.ApplicationsCreated,
                                result.RemindersCreated));
                    }
                    catch (DemoWorkspaceCapacityException exception)
                    {
                        return Results.Json(
                            new { error = "demo_capacity", message = exception.Message },
                            statusCode: StatusCodes.Status429TooManyRequests);
                    }
                    catch (DemoWorkspaceUnavailableException exception)
                    {
                        return Results.Json(
                            new { error = "demo_unavailable", message = exception.Message },
                            statusCode: StatusCodes.Status403Forbidden);
                    }
                })
            .WithName("EnsureDemoWorkspace")
            .WithTags("Demo")
            .Produces<DemoWorkspaceDto>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    // Demo identities stay inside their fictional inbox: they cannot attach a real mailbox.
    public static RouteHandlerBuilder RejectDemoIdentities(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter(
            async (context, next) =>
            {
                var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<DemoOptions>>().Value;
                if (options.Enabled && options.IsDemoEmail(RequestUserContext.GetUserEmail(context.HttpContext.Request)))
                {
                    return Results.Json(
                        new { error = "demo_read_only", message = "The demo workspace uses a fictional inbox and cannot connect a real mailbox." },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                return await next(context);
            });
    }
}
