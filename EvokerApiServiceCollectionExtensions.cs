using DSO.Core.Evoker.Commands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DSO.Core.Evoker.Api
{
    /// <summary>DSO.Core.Evoker.Api ayarları.</summary>
    public sealed class EvokerApiOptions
    {
        /// <summary>
        /// İsimle erişime (api/evoker/types/…) açılan assembly / namespace / tip kalıpları - bkz. EvokerCatalog.AllowTypesFrom.
        /// Varsayılan BOŞ: isimle hiçbir tipe erişilemez, sadece kodda kaydedilen hedefler kullanılır.
        /// </summary>
        public List<string> AllowTypesFrom { get; set; } = new();

        /// <summary>Tanım uçlarında private/protected/internal üyeler varsayılan olarak listelensin mi (?nonPublic= ile değişir).</summary>
        public bool DescribeNonPublicByDefault { get; set; }

        /// <summary>Evoker controller'larında model hataları da (ör. bozuk JSON) aynı zarfla dönsün (varsayılan: evet).</summary>
        public bool UseEnvelopeForModelErrors { get; set; } = true;
    }

    public static class EvokerApiServiceCollectionExtensions
    {
        /// <summary>
        /// Evoker ortak uçlarını ekler (api/evoker/targets, api/evoker/types). EvokerCatalog tekil (singleton) olarak
        /// kaydedilir - kendi kodunuzda inject edip hedef kaydedebilirsiniz:
        /// <code>
        /// builder.Services.AddControllers().AddEvokerApi(o => o.AllowTypesFrom.Add("Acme.*"));
        /// ...
        /// app.Services.GetRequiredService&lt;EvokerCatalog&gt;().Register(typeof(FaturaServisi), name: "Fatura");
        /// </code>
        /// </summary>
        public static IMvcBuilder AddEvokerApi(this IMvcBuilder mvc, Action<EvokerApiOptions>? configure = null)
        {
            var options = new EvokerApiOptions();
            configure?.Invoke(options);
            mvc.Services.TryAddSingleton(options);
            mvc.Services.TryAddSingleton(sp =>
            {
                var o = sp.GetRequiredService<EvokerApiOptions>();
                var catalog = new EvokerCatalog();
                if (o.AllowTypesFrom.Count > 0) catalog.AllowTypesFrom(o.AllowTypesFrom.ToArray());
                return catalog;
            });
            mvc.AddApplicationPart(typeof(EvokerApiControllerBase).Assembly);

            if (options.UseEnvelopeForModelErrors)
            {
                mvc.Services.PostConfigure<ApiBehaviorOptions>(o =>
                {
                    var previous = o.InvalidModelStateResponseFactory;
                    o.InvalidModelStateResponseFactory = ctx =>
                    {
                        if (ctx.ActionDescriptor is ControllerActionDescriptor cad
                            && typeof(EvokerApiControllerBase).IsAssignableFrom(cad.ControllerTypeInfo))
                        {
                            var errors = ctx.ModelState
                                .Where(kv => kv.Value != null && kv.Value.Errors.Count > 0)
                                .SelectMany(kv => kv.Value!.Errors.Select(e =>
                                    (string.IsNullOrEmpty(kv.Key) ? "" : kv.Key + ": ") +
                                    (string.IsNullOrEmpty(e.ErrorMessage) ? e.Exception?.Message : e.ErrorMessage)));
                            return new JsonResult(ApiResponse.Fail(EvokerErrorCodes.BadRequest,
                                "İstek geçersiz: " + string.Join(" | ", errors)), EvokerApiJson.Options)
                            { StatusCode = 400 };
                        }
                        return previous(ctx);
                    };
                });
            }
            return mvc;
        }
    }
}