using System.Text.Json;
using DSO.Core.Evoker.Commands;
using DSO.Core.Evoker.Description;
using Microsoft.AspNetCore.Mvc;

namespace DSO.Core.Evoker.Api.Controllers
{
    /// <summary>
    /// Katalogdaki KAYITLI hedefler (kodda kaydedilen tipler/nesneler ve plugin'ler) - Guid anahtarla:
    /// <code>
    /// GET  api/evoker/targets                                  liste (?kind=Plugin|Type|Instance, ?search=)
    /// GET  api/evoker/targets/{key}?samples=true&amp;values=true    tanım (+ hazır komut şablonları, o anki değerler)
    /// POST api/evoker/targets/{key}/execute                    JSON komut çalıştır
    /// </code>
    /// </summary>
    [Route("api/evoker/targets")]
    public sealed class EvokerTargetsController : EvokerApiControllerBase
    {
        private readonly EvokerCatalog _catalog;
        private readonly EvokerApiOptions _options;

        public EvokerTargetsController(EvokerCatalog catalog, EvokerApiOptions options)
        {
            _catalog = catalog;
            _options = options;
        }

        [HttpGet]
        public IActionResult List([FromQuery] string? kind = null, [FromQuery] string? search = null)
        {
            var items = _catalog.Entries
                .Where(e => string.IsNullOrWhiteSpace(kind) || string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase))
                .Where(e => string.IsNullOrWhiteSpace(search)
                            || (e.Name ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                            || e.TypeFullName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(EvokerTargetView.From)
                .ToList();
            return Envelope(items, $"{items.Count} hedef");
        }

        [HttpGet("{key:guid}")]
        public async Task<IActionResult> Describe(Guid key, [FromQuery] bool samples = false, [FromQuery] bool values = true,
            [FromQuery] bool? nonPublic = null)
        {
            var entry = _catalog.Find(key);
            if (entry == null) return Fail(EvokerErrorCodes.TargetNotFound, $"'{key}' anahtarlı hedef yok.");
            var descriptor = await entry.Target.DescribeAsync(new EvokerDescribeOptions
            {
                IncludeSamples = samples,
                IncludeValues = values,
                IncludeNonPublic = nonPublic ?? _options.DescribeNonPublicByDefault
            }, HttpContext.RequestAborted);
            return Envelope(new { target = EvokerTargetView.From(entry), type = descriptor });
        }

        [HttpPost("{key:guid}/execute")]
        public async Task<IActionResult> Execute(Guid key, [FromBody] JsonElement command)
        {
            if (!TryReadCommand(command, out var cmd, out var error)) return error!;
            var result = await _catalog.ExecuteAsync(key, cmd, HttpContext.RequestAborted);
            return CommandResult(result);
        }
    }

    /// <summary>Katalog kaydının liste görünümü.</summary>
    public sealed class EvokerTargetView
    {
        public Guid Key { get; init; }
        public string? Name { get; init; }
        /// <summary>"Type" (tip; ömür Lifetime'da), "Instance" (hazır nesne), "Plugin".</summary>
        public string Kind { get; init; } = "";
        public string TypeFullName { get; init; } = "";
        public EvokerLifetime? Lifetime { get; init; }
        public DateTime RegisteredUtc { get; init; }

        public static EvokerTargetView From(EvokerCatalogEntry e) => new()
        {
            Key = e.Key,
            Name = e.Name,
            Kind = e.Kind,
            TypeFullName = e.TypeFullName,
            Lifetime = e.Kind == "Type" ? e.Lifetime : null,
            RegisteredUtc = e.RegisteredUtc
        };
    }
}