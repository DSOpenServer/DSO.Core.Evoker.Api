using System.Text.Json;
using DSO.Core.Evoker.Commands;
using DSO.Core.Evoker.Description;
using Microsoft.AspNetCore.Mvc;

namespace DSO.Core.Evoker.Api.Controllers
{
    /// <summary>
    /// İSİMLE erişilen tipler - sadece izin listesindekiler (EvokerApiOptions.AllowTypesFrom; varsayılan boş = kapalı).
    /// Kayıt gerekmez; her komut kendi nesnesini oluşturur (Scoped; constructor argümanları komuttaki "constructorArgs"),
    /// static üyeler / static sınıflar için nesne oluşturulmaz.
    /// <code>
    /// GET  api/evoker/types?search=Math                      izin listesi + izinli tipler
    /// GET  api/evoker/types/System.Math?samples=true         tanım + hazır komut şablonları
    /// POST api/evoker/types/System.Math/execute              { "op":"invoke", "member":"Max", "args":[3,7] }
    /// </code>
    /// Tip adı route yerine ?name= ile de verilebilir (generic adlar gibi özel karakterli adlar için).
    /// </summary>
    [Route("api/evoker/types")]
    public sealed class EvokerTypesController : EvokerApiControllerBase
    {
        private readonly EvokerCatalog _catalog;
        private readonly EvokerApiOptions _options;

        public EvokerTypesController(EvokerCatalog catalog, EvokerApiOptions options)
        {
            _catalog = catalog;
            _options = options;
        }

        [HttpGet]
        public IActionResult List([FromQuery] string? search = null, [FromQuery] int max = 200)
        {
            var patterns = _catalog.AllowedPatterns;
            var types = _catalog.ListAllowedTypes(search, Math.Clamp(max, 1, 2000))
                .Select(t => new
                {
                    fullName = t.FullName ?? t.Name,
                    assembly = t.Assembly.GetName().Name,
                    isStatic = t.IsAbstract && t.IsSealed
                })
                .ToList();
            return Envelope(new { allowTypesFrom = patterns, types },
                patterns.Count == 0 ? "İsimle erişim kapalı (AllowTypesFrom boş)." : $"{types.Count} tip");
        }

        [HttpGet("{typeName}")]
        public Task<IActionResult> Describe(string typeName, [FromQuery] bool samples = false, [FromQuery] bool? nonPublic = null) =>
            DescribeCore(typeName, samples, nonPublic);

        [HttpGet("describe")]
        public Task<IActionResult> DescribeByQuery([FromQuery] string name, [FromQuery] bool samples = false, [FromQuery] bool? nonPublic = null) =>
            DescribeCore(name, samples, nonPublic);

        [HttpPost("{typeName}/execute")]
        public Task<IActionResult> Execute(string typeName, [FromBody] JsonElement command) => ExecuteCore(typeName, command);

        [HttpPost("execute")]
        public Task<IActionResult> ExecuteByQuery([FromQuery] string name, [FromBody] JsonElement command) => ExecuteCore(name, command);

        private async Task<IActionResult> DescribeCore(string typeName, bool samples, bool? nonPublic)
        {
            bool np = nonPublic ?? _options.DescribeNonPublicByDefault;
            var d = await _catalog.DescribeTypeAsync(typeName, new EvokerDescribeOptions
            {
                IncludeSamples = samples,
                IncludeNonPublic = np
            }, includeNonPublic: false);
            return Envelope(d);
        }

        private async Task<IActionResult> ExecuteCore(string typeName, JsonElement command)
        {
            if (!TryReadCommand(command, out var cmd, out var error)) return error!;
            var result = await _catalog.ExecuteOnTypeAsync(typeName, cmd, includeNonPublic: false, HttpContext.RequestAborted);
            return CommandResult(result);
        }
    }
}