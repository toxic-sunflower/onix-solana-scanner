using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Auth;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/settings")]
[Authorize]
[CheckTokenVersion]
public class SettingsController : ControllerBase
{
    private readonly IUserSettingsRepository _settingsRepo;
    private readonly IUserRepository _userRepo;

    public SettingsController(IUserSettingsRepository settingsRepo, IUserRepository userRepo)
    {
        _settingsRepo = settingsRepo;
        _userRepo = userRepo;
    }

    [HttpGet]
    public async Task<ActionResult<UserSettings>> Get(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null) return Unauthorized();

        var settings = await _settingsRepo.GetByTelegramIdAsync(user.TelegramId, ct);
        if (settings is null)
        {
            settings = new UserSettings
            {
                Id = Guid.NewGuid(),
                TelegramId = user.TelegramId,
                Role = user.Role,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _settingsRepo.UpsertAsync(settings, ct);
        }

        return Ok(settings);
    }

    [HttpPatch]
    public async Task<ActionResult> Patch([FromBody] PatchSettingsRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null) return Unauthorized();

        var settings = await _settingsRepo.GetByTelegramIdAsync(user.TelegramId, ct);
        if (settings is null)
        {
            settings = new UserSettings
            {
                Id = Guid.NewGuid(),
                TelegramId = user.TelegramId,
                Role = user.Role,
                CreatedAt = DateTime.UtcNow,
            };
        }
        else
        {
            settings.Role = user.Role;
        }

        if (request.MinimalSpreadPct.HasValue) settings.MinimalSpreadPct = request.MinimalSpreadPct.Value;
        if (request.TelegramNotificationsEnabled.HasValue) settings.TelegramNotificationsEnabled = request.TelegramNotificationsEnabled.Value;
        if (request.CooldownSeconds.HasValue) settings.CooldownSeconds = request.CooldownSeconds.Value;
        if (request.Timezone is not null) settings.Timezone = request.Timezone;

        settings.UpdatedAt = DateTime.UtcNow;
        await _settingsRepo.UpsertAsync(settings, ct);
        return NoContent();
    }

    public class PatchSettingsRequest
    {
        public decimal? MinimalSpreadPct { get; set; }
        public bool? TelegramNotificationsEnabled { get; set; }
        public int? CooldownSeconds { get; set; }
        public string? Timezone { get; set; }
    }
}
