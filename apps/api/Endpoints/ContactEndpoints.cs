using Careeroc.Application.Services;
using Careeroc.Api.Security;
using Careeroc.Contracts.Contacts;

namespace Careeroc.Api.Endpoints;

public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/recruiting-contacts",
                async (
                    HttpRequest request,
                    int? limit,
                    RecruitingContactQueryService contactQueryService,
                    CancellationToken cancellationToken) =>
                {
                    var response = await contactQueryService.GetAsync(
                        RequestUserContext.GetUserEmail(request),
                        limit,
                        cancellationToken);

                    return TypedResults.Ok(response);
                })
            .WithName("GetRecruitingContacts")
            .WithTags("Contacts")
            .Produces<RecruitingContactListResponse>();

        endpoints
            .MapPatch(
                "/api/recruiting-contacts/{contactId:guid}",
                async (
                    Guid contactId,
                    UpdateRecruitingContactRequest updateRequest,
                    HttpRequest request,
                    RecruitingContactQueryService contactQueryService,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var response = await contactQueryService.UpdateAsync(
                            RequestUserContext.GetUserEmail(request),
                            contactId,
                            updateRequest,
                            cancellationToken);

                        return response is null
                            ? Results.NotFound()
                            : Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("UpdateRecruitingContact")
            .WithTags("Contacts")
            .Produces<RecruitingContactDto>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        return endpoints;
    }
}
