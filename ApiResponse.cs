using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DSO.Core.Evoker.Commands;

namespace DSO.Core.Evoker.Api
{
    /// <summary>
    /// Yönetim uçlarının (liste, kayıt, aktifleştirme, tanım...) cevap zarfı. Komut uçları (…/execute) çekirdeğin
    /// EvokerCommandResult'ını aynı şekilde döner ({ success, result | steps, error, elapsedMs, mode }), yani istemci
    /// tarafında TEK bir zarf tipi yeter:
    /// <code>
    /// { "success": true,  "result": { ... }, "message": "Siparis eklendi ve yüklendi.", "elapsedMs": 12.4 }
    /// { "success": false, "error": { "code": "TargetNotFound", "message": "..." }, "elapsedMs": 0.3 }
    /// </code>
    /// HTTP durum kodu error.code'dan gelir (EvokerErrorCodes.HttpStatus): 400, 403, 404, 409, 422, 499, 500, 504.
    /// </summary>
    public sealed class ApiResponse
    {
        public bool Success { get; set; }
        public object? Result { get; set; }
        /// <summary>İnsan için kısa açıklama (ör. "eklendi ama YÜKLENEMEDİ: ...").</summary>
        public string? Message { get; set; }
        public EvokerError? Error { get; set; }
        public double ElapsedMs { get; set; }

        public static ApiResponse Ok(object? result, string? message = null, double elapsedMs = 0) =>
            new() { Success = true, Result = result, Message = message, ElapsedMs = elapsedMs };

        public static ApiResponse Fail(string code, string message, double elapsedMs = 0, string? exceptionType = null) =>
            new() { Success = false, Error = new EvokerError { Code = code, Message = message, ExceptionType = exceptionType }, ElapsedMs = elapsedMs };
    }

    /// <summary>
    /// API'nin JSON ayarları - host'un genel MVC JSON ayarlarına DOKUNULMAZ; Evoker uçları kendi ayarlarıyla yazar/okur:
    /// camelCase, enum'lar metin ("Sandbox"; okurken sayı da kabul), null alanlar yazılmaz, Türkçe karakterler kaçışsız,
    /// okurken büyük/küçük harf duyarsız ve "5" gibi metin sayılar kabul.
    /// </summary>
    public static class EvokerApiJson
    {
        public static JsonSerializerOptions Options { get; } = Create();

        private static JsonSerializerOptions Create()
        {
            var o = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = null,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            o.Converters.Add(new JsonStringEnumConverter());
            return o;
        }

        /// <summary>İstek gövdesindeki JSON'u (JsonElement) bir DTO'ya çevirir; hatalı JSON'da ArgumentException (400).</summary>
        public static T Read<T>(JsonElement body) where T : new()
        {
            if (body.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return new T();
            if (body.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("İstek gövdesi bir JSON nesnesi olmalı.");
            try { return body.Deserialize<T>(Options) ?? new T(); }
            catch (JsonException ex) { throw new ArgumentException("İstek gövdesi okunamadı: " + ex.Message, ex); }
        }
    }
}