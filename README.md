# DSO.Core.Evoker.Api

> **Sisteminizdeki her sınıf, iki satır kodla bir REST ucu. Controller yazmadan, DTO çizmeden, sürüm kırmadan.**

![.NET](https://img.shields.io/badge/.NET-6.0%20%7C%208.0-512BD4) ![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-controllers-blue)

`DSO.Core.Evoker.Api`, [DSO.Core.Evoker](https://github.com/DSOpenServer/DSO.Core.Evoker/blob/main/README.md)'ın `EvokerCatalog`'una kaydettiğiniz **her
hedefi** web'e açar:

- kendi servis sınıflarınızı,
- hazır nesnelerinizi,
- static yardımcı sınıflarınızı,
- [plugin'lerinizi](https://github.com/DSOpenServer/DSO.Core.Evoker.Plugins/blob/main/README.md).

Hepsi aynı JSON komut biçimiyle, aynı cevap zarfıyla ve aynı hata kodlarıyla çalışır. Tanım ucu her metot için
**doldurulmaya hazır komut şablonları** verir; bir yönetim ekranının ya da otomasyon aracının ihtiyaç duyduğu her şey
tek bir `GET` ile gelir.

---

## İçindekiler

- [Neden Evoker.Api?](#neden-evokerapi)
- [Kurulum](#kurulum)
- [60 saniyede Api](#60-saniyede-api)
- [Uçlar](#uçlar)
- [Cevap zarfı ve HTTP kodları](#cevap-zarfı-ve-http-kodları)
- [Güvenlik modeli: iki erişim seviyesi](#güvenlik-modeli-iki-erişim-seviyesi)
- [Kendi controller'larınızda kullanmak](#kendi-controllerlarınızda-kullanmak)
- [Seçenekler](#seçenekler)
- [Testler ve örnekler](#testler-ve-örnekler)

---

## Neden Evoker.Api?

| Yaygın yol | Bedeli | Evoker.Api |
|---|---|---|
| Her servis metodu için controller action + istek DTO'su + cevap DTO'su | Bir metot eklemek dört dosya değiştirmek demektir | Metodu sınıfa ekleyin; uç **zaten** var |
| "Genel amaçlı RPC" çatıları | Ayrı protokol, ayrı istemci, kod üretimi | Düz JSON + HTTP; her dil ve araç çağırabilir |
| Reflection ile kendi "invoke" ucunuzu yazmak | Overload, optional, enum, iç içe nesne, async, hata eşleme… her biri ayrı bir proje | Hepsi hazır ve testli: sıralı ve isimli argüman, overload puanlama, `params`, async, çok adımlı komut, timeout |
| Swagger'a bakarak istek elle hazırlamak | Her alan için tahmin | `?samples=true` her metot için hazır komut gövdesi döner |

- **Host'a saygılı:** host'un global JSON ayarlarına dokunmaz; Evoker uçları kendi ayarlarıyla yazar ve okur.
- **Unload güvenli:** komut sonuçları çekirdeğin kendi serializer'ıyla yazılır. Böylece MVC'nin JSON önbelleği plugin
  tiplerini tutmaz ve in-process plugin'ler bellekten temizce atılabilir.

---

## Kurulum

```xml
<ProjectReference Include="..\DSO.Core.Evoker.Api\DSO.Core.Evoker.Api.csproj" />
```

Paket `Microsoft.AspNetCore.App` paylaşılan framework'üne referans verir; ek NuGet paketi gerekmez. Hedefler:
`net6.0`, `net8.0`.

---

## 60 saniyede Api

```csharp
using DSO.Core.Evoker.Api;
using DSO.Core.Evoker.Commands;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers()
    .AddEvokerApi(o => o.AllowTypesFrom.Add("System.Math"));     // isimle erişim izin listesi (varsayılan boş)

var app = builder.Build();

var katalog = app.Services.GetRequiredService<EvokerCatalog>();
Guid fatura = katalog.Register(typeof(FaturaServisi), name: "Fatura servisi");
katalog.Register(typeof(Sepet), EvokerLifetime.Scoped, name: "Sepet", key: Guid.Parse("5a1ac000-0000-4000-8000-000000000002"));

app.MapControllers();
app.Run();
```

```http
POST /api/evoker/targets/{fatura}/execute
Content-Type: application/json

{ "op": "invoke", "member": "Kes", "args": { "musteriKodu": "C001", "tutar": 150.5 } }
```

```json
{ "success": true, "result": { "faturaNo": 1001, "tutar": 180.6 }, "elapsedMs": 0.42 }
```

---

## Uçlar

### Kayıtlı hedefler — `api/evoker/targets`

| Uç | Açıklama |
|---|---|
| `GET /api/evoker/targets?kind=&search=` | Katalogdaki tüm hedefler. Her hedef için `key`, `name`, `kind` (`Type` / `Instance` / `Plugin`), `typeFullName`, `lifetime`, `registeredUtc` |
| `GET /api/evoker/targets/{key}?samples=true&values=true&nonPublic=false` | Hedefin tam tanımı: constructor, metot, property, field ve event'ler; o anki değerler ve hazır komut şablonları |
| `POST /api/evoker/targets/{key}/execute` | JSON komut çalıştır |

### İsimle erişilen tipler — `api/evoker/types`

| Uç | Açıklama |
|---|---|
| `GET /api/evoker/types?search=&max=` | İzin listesi ve izinli tipler (`fullName`, `assembly`, `isStatic`) |
| `GET /api/evoker/types/{typeName}?samples=true&nonPublic=false` | İzinli bir tipin tanımı |
| `GET /api/evoker/types/describe?name=...` | Aynısı; özel karakterli adlar (generic vb.) için query ile |
| `POST /api/evoker/types/{typeName}/execute` | Kayıt gerektirmeden komut. Ömür Scoped'dır (komut başına bir nesne); constructor argümanları komuttaki `constructorArgs` alanından gelir |
| `POST /api/evoker/types/execute?name=...` | Aynısı; query ile |

### Komut örnekleri

```jsonc
// sıralı / isimli argüman
{ "op": "invoke", "member": "Max", "args": [3, 7] }
{ "op": "invoke", "member": "Ekle", "args": { "urun": "Kalem", "adet": 2, "fiyat": 7.5 } }

// property
{ "op": "get", "member": "Deger" }
{ "op": "set", "member": "Ad", "value": "yeni" }

// çok adımlı (Scoped hedefte adımlar aynı nesneyi paylaşır)
{ "steps": [
    { "op": "invoke", "member": "Ekle", "args": { "urun": "Kalem", "adet": 2, "fiyat": 7.5 } },
    { "op": "invoke", "member": "Ekle", "args": { "urun": "Defter", "fiyat": 20 } },
    { "op": "invoke", "member": "Toplam", "as": "toplam" }
  ], "stopOnError": true }

// aynı metodu çok argüman setiyle
{ "op": "batch", "member": "Fiyat", "argsList": [[1, 10], [2, 5]] }

// bekleme sınırı
{ "op": "invoke", "member": "YavasIs", "args": [5000], "timeoutMs": 500 }
```

Tüm kurallar (overload puanlama, `params`, `argTypes`, nesne ömürleri) için:
[DSO.Core.Evoker → Commands](https://github.com/DSOpenServer/DSO.Core.Evoker/blob/main/README.md#commands--json-komutlar).

---

## Cevap zarfı ve HTTP kodları

Tüm Evoker uçları tek bir zarf biçimi kullanır. İstemci tarafında tek bir tip yeter.

```jsonc
// yönetim / okuma uçları
{ "success": true,  "result": { ... }, "message": "4 hedef", "elapsedMs": 1.2 }
{ "success": false, "error": { "code": "TargetNotFound", "message": "..." }, "elapsedMs": 0.3 }

// komut uçları (EvokerCommandResult)
{ "success": true, "result": 7, "elapsedMs": 0.08, "mode": "Sandbox" }
{ "success": true, "steps": [ { "index": 0, "op": "invoke", "member": "Ekle", "success": true, "result": 1 }, ... ] }
{ "success": false, "error": { "code": "TargetException", "message": "...", "exceptionType": "System.DivideByZeroException" } }
```

| `error.code` | HTTP |
|---|---:|
| `BadRequest` | 400 |
| `NotAllowed` | 403 |
| `TargetNotFound`, `MemberNotFound` | 404 |
| `Inactive` | 409 |
| `InvalidArguments`, `AmbiguousMatch`, `InvalidOperation` | 422 |
| `Cancelled` (istemci bağlantıyı kesti) | 499 |
| `TargetException`, `InternalError` | 500 |
| `Timeout` | 504 |

Bozuk JSON gövdesi, ASP.NET'in varsayılan ProblemDetails'i yerine **aynı zarfla** 400 döner. Bu sadece Evoker
controller'larında geçerlidir; diğer controller'larınız etkilenmez.

---

## Güvenlik modeli: iki erişim seviyesi

1. **Kayıtlı hedefler:** kodda kaydettiğiniz şeyler, yani tipler, nesneler ve plugin'ler. Ne açılacağına uygulama karar
   verir.
2. **İsimle erişim:** kayıt gerektirmez ama sadece `AllowTypesFrom` kalıplarına uyan tipler açılır. Varsayılan liste
   **boştur**; bu durumda isimle erişim tamamen kapalıdır ve 403 döner.

Kalıp örnekleri:

| Kalıp | Eşleşen |
|---|---|
| `"Acme.*"` | `Acme` assembly'si veya `Acme` namespace'i ve alt namespace'leri |
| `"Acme.Billing"` | Birebir assembly adı, namespace (ve altları) ya da tam tip adı |
| `"System.Math"` | Tek bir tip |
| `"*"` | Her şey. **Önerilmez:** sunucuda keyfi kod çalıştırmak demektir |

> Paket kimlik doğrulama yapmaz. Uçları kendi yetkilendirme politikanızın arkasına alın: `[Authorize]` ile türetilmiş
> controller, endpoint filtresi ya da reverse proxy kullanabilirsiniz.

---

## Kendi controller'larınızda kullanmak

`EvokerApiControllerBase`'den türeyen her controller aynı zarfı, süre ölçümünü ve hata eşlemesini kendiliğinden alır.

```csharp
[Route("api/rapor")]
public sealed class RaporController : EvokerApiControllerBase
{
    private readonly EvokerCatalog _katalog;
    public RaporController(EvokerCatalog katalog) => _katalog = katalog;

    [HttpGet("ozet")]
    public IActionResult Ozet() =>
        Envelope(new { hedefSayisi = _katalog.Entries.Count }, "özet hazır");     // { success, result, message, elapsedMs }

    [HttpPost("calistir/{key:guid}")]
    public async Task<IActionResult> Calistir(Guid key, [FromBody] JsonElement govde)
    {
        if (!TryReadCommand(govde, out var komut, out var hata)) return hata!;    // bozuk komut -> 400 zarf
        return CommandResult(await _katalog.ExecuteAsync(key, komut, HttpContext.RequestAborted));
    }

    [HttpGet("hata")]
    public IActionResult Hata() => Fail(EvokerErrorCodes.NotAllowed, "Bu rapor kapalı.");   // 403
}
```

| Üye | Açıklama |
|---|---|
| `Envelope(result, message, statusCode = 200)` | Başarılı zarf |
| `Fail(code, message, statusCode?)` | Hatalı zarf; HTTP kodu `code`'dan türetilir |
| `CommandResult(EvokerCommandResult)` | Komut sonucunu çekirdeğin serializer'ıyla, hata koduna uygun HTTP koduyla yazar |
| `TryReadCommand(JsonElement, out EvokerCommand, out IActionResult?)` | Gövdeyi komuta çevirir; hatalıysa hazır 400 cevabı verir |
| `ElapsedMs` | İsteğin başından beri geçen süre (ms) |

**`EvokerApiExceptionFilterAttribute`** (taban sınıfta zaten uygulanmıştır) exception'ları zarfa çevirir:

| Exception | Kod |
|---|---|
| `EvokerCommandException` | Kendi kodu |
| `KeyNotFoundException` | 404 |
| `ArgumentException` / `JsonException` / `FormatException` | 400 |
| `InvalidOperationException` | 422 |
| `TimeoutException` | 504 |
| İstemci iptali | 499 |
| Diğerleri | 500 `InternalError` + `exceptionType` |

**Yardımcılar:**

```csharp
ApiResponse ok   = ApiResponse.Ok(sonuc, "tamam", elapsedMs: 1.2);
ApiResponse fail = ApiResponse.Fail(EvokerErrorCodes.BadRequest, "eksik alan");
JsonSerializerOptions opt = EvokerApiJson.Options;          // camelCase, enum metin, null yazılmaz, Türkçe kaçışsız
var istek = EvokerApiJson.Read<BenimIstegim>(govde);       // hatalı JSON -> ArgumentException (400)
```

---

## Seçenekler

```csharp
builder.Services.AddControllers().AddEvokerApi(o =>
{
    o.AllowTypesFrom = new List<string> { "Acme.*" };   // isimle erişim izin listesi (varsayılan boş)
    o.DescribeNonPublicByDefault = false;               // tanımlarda private üyeler (?nonPublic= ile değişir)
    o.UseEnvelopeForModelErrors = true;                 // bozuk gövde -> aynı zarf (sadece Evoker controller'ları)
});
```

`appsettings.json` üzerinden:

```json
{ "Evoker": { "AllowTypesFrom": [ "System.Math", "Acme.Services.*" ] } }
```

```csharp
.AddEvokerApi(o => o.AllowTypesFrom = builder.Configuration.GetSection("Evoker:AllowTypesFrom").Get<List<string>>() ?? new());
```

`AddEvokerApi` şu kayıtları yapar:

- `EvokerApiOptions` ve `EvokerCatalog`, ikisi de singleton (siz önceden kaydettiyseniz sizinki kullanılır);
- controller'lar `AddApplicationPart` ile eklenir;
- bozuk gövde zarfı için `ApiBehaviorOptions` sadece Evoker controller'ları için ayarlanır.

---

## Testler ve örnekler

- **`DSO.Core.Evoker.Plugins.DemoApi`:** çalışır bir örnek. Program.cs dört farklı ömürde örnek hedef kaydeder:
  - Sayaç (Singleton),
  - Sepet (Scoped),
  - Hesap (Transient),
  - SunucuSaati (Static).

  Tüm istekler `demo.http` dosyasında; Swagger `/swagger` adresinde.
- **`DSO.Core.Evoker.Plugins.DemoApi.E2ETest`:** çalışan API'ye HTTP ile bağlanan **81 kontrollük** uçtan uca test. Bu
  paketin kapsamındaki kontroller şunlar:
  - Singleton'a 20 paralel istek;
  - Scoped adımların aynı nesneyi paylaşması;
  - Transient'in her adımda yeni nesne üretmesi;
  - host sınıfında exception → 500 + `exceptionType`;
  - `params double[]`;
  - static sınıf + optional parametre;
  - tanım + şablon;
  - plugin'in genel katalog ucundan çalışması;
  - olmayan hedef → 404;
  - izin listesi boşken 403, doluyken `Math.Max(3,7) = 7`.
- **`CommandTests` (C1–C13):** komut motorunun kendisi, `DSO.Core.Evoker.TestRunner` içinde.

Plugin yönetim uçları için: [DSO.Core.Evoker.Plugins.Api](https://github.com/DSOpenServer/DSO.Core.Evoker.Plugins.Api/blob/main/README.md).
