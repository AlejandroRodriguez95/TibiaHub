using Microsoft.AspNetCore.Mvc;
using TibiaHub.External.TibiaData;
using TibiaHub.Routes;

namespace TibiaHub.Controllers;

[ApiController]
[Route(ApiRoutes.Character)]
public sealed class CharacterController(TibiaDataClient tibiaDataClient) : ControllerBase
{
    [HttpGet(ApiRoutes.CharacterRoutes.ByName)]
    public async Task<IResult> GetCharacter(
        string name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Results.BadRequest(new
            {
                error = "Character name is required."
            });
        }

        var response = await tibiaDataClient.GetCharacterAsync(
            name,
            cancellationToken);

        if (!response.IsSuccess)
        {
            return Results.Problem(
                title: "TibiaData request failed.",
                detail: response.ErrorMessage,
                statusCode: response.StatusCode);
        }

        return Results.Json(response.Payload);
    }
}
