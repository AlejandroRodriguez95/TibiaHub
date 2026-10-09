using Microsoft.AspNetCore.Mvc;
using TibiaHub.External.TibiaData;

namespace TibiaHub.Controllers;

[ApiController]
[Route("character")]
public sealed class CharacterController(TibiaDataClient tibiaDataClient) : ControllerBase
{
    [HttpGet("{name}")]
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
