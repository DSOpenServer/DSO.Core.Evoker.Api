using System.Diagnostics;
using System.Text.Json;
using DSO.Core.Evoker.Commands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DSO.Core.Evoker.Api
{
    /// <summary>
    /// Evoker uçlarının ortak tabanı: cevap zarfı, süre ölçümü, hata -> HTTP eşlemesi. Kendi controller'larınızı da
    /// bundan türetirseniz aynı zarfı ve hata davranışını alırsınız.
    /// </summary>
    [ApiController]
    [EvokerApiExceptionFilter]
    [Produces("application/json")]
    public abstract class EvokerApiControllerBase : ControllerBase, IActionFilter
    {
        internal const string StartedKey = "DSO.Evoker.Api.Started";
        private readonly long _started = Stopwatch.GetTimestamp();

        // Süre ölçümü exception filtresinde de görünsün diye başlangıç zamanı isteğe yazılır.
        [NonAction]
        public void OnActionExecuting(ActionExecutingContext context) => HttpContext.Items[StartedKey] = _started;

        [NonAction]
        public void OnActionExecuted(ActionExecutedContext context) { }

        /// <summary>İstek başından beri geçen süre (ms, 3 hane).</summary>
        protected internal double ElapsedMs => Math.Round((Stopwatch.GetTimestamp() - _started) * 1000.0 / Stopwatch.Frequency, 3);

        /// <summary>Başarılı cevap: { success:true, result, message, elapsedMs }.</summary>
        protected IActionResult Envelope(object? result, string? message = null, int statusCode = 200) =>
            new JsonResult(ApiResponse.Ok(result, message, ElapsedMs), EvokerApiJson.Options) { StatusCode = statusCode };

        /// <summary>Hatalı cevap: { success:false, error:{code,message}, elapsedMs }; HTTP kodu error.code'dan.</summary>
        protected IActionResult Fail(string code, string message, int? statusCode = null) =>
            new JsonResult(ApiResponse.Fail(code, message, ElapsedMs), EvokerApiJson.Options)
            { StatusCode = statusCode ?? EvokerErrorCodes.HttpStatus(code) };

        /// <summary>
        /// Komut sonucu - çekirdeğin kendi JSON'uyla yazılır (sonuç plugin tiplerinden nesneler içerebilir; host'un MVC
        /// ayarları bu tiplerin metadata'sını önbelleğe ALMAZ, in-process plugin'ler sorunsuz boşaltılabilir).
        /// </summary>
        protected IActionResult CommandResult(EvokerCommandResult result) =>
            new ContentResult
            {
                Content = result.ToJson(),
                ContentType = "application/json; charset=utf-8",
                StatusCode = result.Success ? 200 : EvokerErrorCodes.HttpStatus(result.Error?.Code)
            };

        /// <summary>Gövdeyi komuta çevirir; hatalıysa null + hazır 400 cevabı.</summary>
        protected bool TryReadCommand(JsonElement body, out EvokerCommand command, out IActionResult? error)
        {
            error = null;
            command = null!;
            try
            {
                command = EvokerCommand.FromElement(body);
                return true;
            }
            catch (EvokerCommandException ex) { error = CommandResult(EvokerCommandResult.Fail(ex.Code, ex.Message)); }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
            { error = CommandResult(EvokerCommandResult.Fail(EvokerErrorCodes.BadRequest, "Komut okunamadı: " + ex.Message)); }
            return false;
        }
    }

    /// <summary>
    /// Beklenmeyen ve beklenen exception'ları zarfa çevirir:
    ///   EvokerCommandException -> kendi kodu; KeyNotFoundException -> 404 TargetNotFound; ArgumentException / JsonException
    ///   -> 400 BadRequest; InvalidOperationException -> 422 InvalidOperation; TimeoutException -> 504; istek iptali -> 499;
    ///   diğerleri -> 500 InternalError.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class EvokerApiExceptionFilterAttribute : ExceptionFilterAttribute
    {
        public override void OnException(ExceptionContext context)
        {
            var ex = context.Exception;
            string code = ex switch
            {
                EvokerCommandException ece => ece.Code,
                KeyNotFoundException => EvokerErrorCodes.TargetNotFound,
                ArgumentException or JsonException or FormatException => EvokerErrorCodes.BadRequest,
                OperationCanceledException when context.HttpContext.RequestAborted.IsCancellationRequested => EvokerErrorCodes.Cancelled,
                TimeoutException => EvokerErrorCodes.Timeout,
                InvalidOperationException or ObjectDisposedException => EvokerErrorCodes.InvalidOperation,
                _ => EvokerErrorCodes.InternalError
            };
            double elapsed = context.HttpContext.Items.TryGetValue(EvokerApiControllerBase.StartedKey, out var st) && st is long started
                ? Math.Round((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency, 3) : 0;
            context.Result = new JsonResult(ApiResponse.Fail(code, ex.Message, elapsed,
                    code == EvokerErrorCodes.InternalError ? ex.GetType().FullName : null), EvokerApiJson.Options)
            { StatusCode = EvokerErrorCodes.HttpStatus(code) };
            context.ExceptionHandled = true;
        }
    }
}