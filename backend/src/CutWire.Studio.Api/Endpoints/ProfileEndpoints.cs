using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Accounts;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Api.Endpoints;

public sealed record UpdateProfileRequest(string? Handle, string? DisplayName, string? Bio);

public static class ProfileEndpoints
{
    public static void MapProfile(this RouteGroupBuilder account)
    {
        var profile = account.MapGroup("/profile").WithTags("Profile").RequireAuthorization(AuthPolicies.Session)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        profile.MapGet("", async (HttpContext http, IQueryHandler<GetProfileQuery, ProfileDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetProfileQuery(http.User.GetUserId()), ct)).ToHttpResult(Results.Ok))
            .WithName("GetProfile")
            .Produces<ProfileDto>();

        profile.MapPut("", async (UpdateProfileRequest body, HttpContext http,
                    ICommandHandler<UpdateProfileCommand, ProfileDto> handler, CancellationToken ct) =>
                (await handler.Handle(new UpdateProfileCommand(http.User.GetUserId(), body.Handle, body.DisplayName, body.Bio), ct))
                .ToHttpResult(Results.Ok))
            .WithName("UpdateProfile")
            .Produces<ProfileDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        profile.MapPost("/avatar", async (HttpContext http, ICommandHandler<SetAvatarCommand, ProfileDto> handler, CancellationToken ct) =>
            {
                var buffer = new MemoryStream();
                var limit = SetAvatarHandler.MaxBytes + 1;
                var chunk = new byte[16 * 1024];
                int read;
                while (buffer.Length < limit && (read = await http.Request.Body.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length)), ct)) > 0)
                    buffer.Write(chunk, 0, read);

                buffer.Position = 0;
                return (await handler.Handle(new SetAvatarCommand(http.User.GetUserId(), buffer), ct)).ToHttpResult(Results.Ok);
            })
            .WithName("SetAvatar")
            .Accepts<byte[]>("image/webp", "image/png", "image/jpeg")
            .Produces<ProfileDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        profile.MapDelete("/avatar", async (HttpContext http, ICommandHandler<RemoveAvatarCommand, ProfileDto> handler, CancellationToken ct) =>
                (await handler.Handle(new RemoveAvatarCommand(http.User.GetUserId()), ct)).ToHttpResult(Results.Ok))
            .WithName("RemoveAvatar")
            .Produces<ProfileDto>();
    }
}
