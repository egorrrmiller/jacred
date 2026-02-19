using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Api.Controllers;

[ApiController]
public class SubscribeController : ControllerBase
{
    private readonly ISubscribeService _subscribeService;

    public SubscribeController(ISubscribeService subscribeService)
    {
        _subscribeService = subscribeService;
    }

    /// <summary>
    ///     Начать отслеживание сериала
    /// </summary>
    /// <param name="tmdb">идентификатор сериала</param>
    /// <param name="uid">уникальный идентификатор пользователя</param>
    [HttpPost("[action]")]
    public async Task Subscribe(long tmdb, string uid)
    {
        await _subscribeService.SubscribeAsync(tmdb, uid);
    }

    /// <summary>
    ///     Прекратить отслеживание сериала
    /// </summary>
    /// <param name="tmdb">идентификатор сериала</param>
    /// <param name="uid">уникальный идентификатор пользователя</param>
    [HttpPost("[action]")]
    public async Task UnSubscribe(long tmdb, string uid)
    {
        await _subscribeService.UnSubscribeAsync(tmdb, uid);
    }

    /// <summary>
    ///     Проверить наличие отслеживания сериала
    /// </summary>
    /// <param name="tmdb">идентификатор сериала</param>
    /// <param name="uid">уникальный идентификатор пользователя</param>
    [HttpPost("check-subscribe")]
    public async Task<IActionResult> CheckSubscribe(long tmdb, string uid)
    {
        return Ok(
            new
            {
                result = await _subscribeService.CheckSubscribeAsync(tmdb, uid)
            });
    }
}