using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace ProductCatalogToSheets
{
    internal partial class Program
    {
        internal static async Task Main()
        {
            // Load configuration
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var proxyHost = config["Proxy:Host"] ?? throw new InvalidOperationException("Proxy host not configured");
            var proxyUsername = config["Proxy:Username"] ?? throw new InvalidOperationException("Proxy username not configured");
            var proxyPassword = config["Proxy:Password"] ?? throw new InvalidOperationException("Proxy password not configured");
            var credentialPath = config["GoogleSheets:CredentialPath"] ?? throw new InvalidOperationException("Credential path not configured");
            var spreadsheetId = config["GoogleSheets:SpreadsheetId"] ?? throw new InvalidOperationException("Spreadsheet ID not configured");
            var baseUrl = config["Scraper:BaseUrl"] ?? throw new InvalidOperationException("Target URL not configured in appsettings.json");
            var requestDelayMs = int.Parse(config["Scraper:RequestDelayMs"] ?? "250");
            var retryDelayMs = int.Parse(config["Scraper:RetryDelayMs"] ?? "10000");

            var row = int.Parse(config["GoogleSheets:StartRow"] ?? "11");
            var doorHandlesUrls = new HashSet<string>();
            var locksUrls = new HashSet<string>();
            var doorHingesUrls = new HashSet<string>();
            var hardwareUrls = new HashSet<string>();
            var slidingSystemsUrls = new HashSet<string>();
            var smartSystemsUrls = new HashSet<string>();
            var rowLock = new object();

            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy(proxyHost)
                {
                    Credentials = new NetworkCredential(proxyUsername, proxyPassword)
                }
            };
            var client = new HttpClient(handler);
            var userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                client.DefaultRequestHeaders.Add("User-Agent", userAgent);
            }

            async Task GetUrlsFromPage(string pageUrl, HashSet<string> urlCollection, string xpathSelector)
            {
                var html = await GetHtmlAsync(pageUrl);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var hrefs = htmlDoc.DocumentNode.SelectNodes(xpathSelector);
                if (hrefs == null || hrefs.Count == 0)
                {
                    Console.WriteLine($"No URLs found with XPath: {xpathSelector}");
                    return;
                }

                var hrefList = hrefs
                    .Select(n => n.GetAttributeValue("href", string.Empty))
                    .Where(v => !string.IsNullOrEmpty(v));

                Console.WriteLine($"Found {hrefList.Count()} URLs");
                foreach (var href in hrefList)
                {
                    urlCollection.Add(new Uri(new Uri(baseUrl), href).AbsoluteUri);
                }
            }

            async Task GetSmartSystemsUrlsFromPage(string pageUrl)
            {
                await GetUrlsFromPage(pageUrl, smartSystemsUrls, "//div[@class='similar_items_block']//picture/a[@href]");

                var html = await GetHtmlAsync(pageUrl);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var hrefs2 = htmlDoc.DocumentNode.SelectNodes("//noindex//ul//a");
                if (hrefs2 != null)
                {
                    foreach (var href in hrefs2
                        .Select(n => n.GetAttributeValue("href", string.Empty))
                        .Where(v => !string.IsNullOrEmpty(v)))
                    {
                        smartSystemsUrls.Add(new Uri(new Uri(baseUrl), href).AbsoluteUri);
                    }
                    Console.WriteLine(hrefs2.Count);
                }
            }

            async Task GetSlidingSystemsUrlsFromPage(string pageUrl)
            {
                await GetUrlsFromPage(pageUrl, slidingSystemsUrls, "//div[@class='similar_items_block']//picture/a[@href]");

                var html = await GetHtmlAsync(pageUrl);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var hrefs2 = htmlDoc.DocumentNode.SelectNodes("//noindex//ul//a");
                if (hrefs2 != null)
                {
                    foreach (var href in hrefs2
                        .Select(n => n.GetAttributeValue("href", string.Empty))
                        .Where(v => !string.IsNullOrEmpty(v)))
                    {
                        slidingSystemsUrls.Add(new Uri(new Uri(baseUrl), href).AbsoluteUri);
                    }
                    Console.WriteLine(hrefs2.Count);
                }
            }

            async Task GetHardwareUrlsFromPage(string pageUrl)
            {
                await GetUrlsFromPage(pageUrl, hardwareUrls, "//div[@class='similar_items_block']//picture/a[@href]");

                var html = await GetHtmlAsync(pageUrl);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var hrefs2 = htmlDoc.DocumentNode.SelectNodes("//noindex//ul//a");
                if (hrefs2 != null)
                {
                    foreach (var href in hrefs2
                        .Select(n => n.GetAttributeValue("href", string.Empty))
                        .Where(v => !string.IsNullOrEmpty(v)))
                    {
                        hardwareUrls.Add(new Uri(new Uri(baseUrl), href).AbsoluteUri);
                    }
                    Console.WriteLine(hrefs2.Count);
                }
            }

            async Task GetDoorHingesUrlsFromPage(string pageUrl)
            {
                await GetUrlsFromPage(pageUrl, doorHingesUrls, "//div[@class='similar_items_block']//picture/a[@href]");

                var html = await GetHtmlAsync(pageUrl);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var hrefs2 = htmlDoc.DocumentNode.SelectNodes("//noindex//ul//a");
                if (hrefs2 != null)
                {
                    foreach (var href in hrefs2
                        .Select(n => n.GetAttributeValue("href", string.Empty))
                        .Where(v => !string.IsNullOrEmpty(v)))
                    {
                        doorHingesUrls.Add(new Uri(new Uri(baseUrl), href).AbsoluteUri);
                    }
                    Console.WriteLine(hrefs2.Count);
                }
            }

            async Task ParseSmartSystemPage(string pageUrl)
            {
                var html = await GetHtmlAsync(pageUrl);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var h1Node = htmlDoc.DocumentNode.SelectSingleNode("//main//h1");
                if (h1Node == null)
                {
                    Console.WriteLine("H1 tag not found on smart system page");
                    return;
                }

                var productName = HttpUtility.HtmlDecode(h1Node.InnerText.Trim());

                string? description = null;
                var vHtmlValue = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'ql-editor')]")?.GetAttributeValue("v-html", string.Empty);
                if (vHtmlValue != null)
                {
                    var textInsideQuotes = GeneratedRegexes.Quoted().Match(vHtmlValue).Value;
                    var decoded = HttpUtility.HtmlDecode(textInsideQuotes);
                    decoded = HttpUtility.HtmlDecode(decoded);
                    var htmlDoc2 = new HtmlDocument();
                    htmlDoc2.LoadHtml(decoded);
                    description = htmlDoc2.DocumentNode.InnerText.Trim(' ', '"');
                }

                var colorName = GeneratedRegexes.ColorAfterCvet().Match(productName).Value.Trim(' ', '-');
                var collection = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Коллекция')]/following-sibling::*")?.InnerText.Trim();
                var openingSystemType = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Тип системы открывания')]/following-sibling::span")?.InnerText.Trim();
                var size = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Размер')]/following-sibling::div//a[contains(@class, 'active')]")?.InnerText.Trim();
                var sideliness = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Сторонность')]/following-sibling::div//a[contains(@class, 'active')]")?.InnerText.Trim();
                var height = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Высота')]/following-sibling::div//a[contains(@class, 'active')]")?.InnerText.Trim();
                var width = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Ширина')]/following-sibling::div//a[contains(@class, 'active')]")?.InnerText.Trim();
                var typeOfStopperCloser = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Тип стопора/доводчика')]/following-sibling::div//a[contains(@class, 'active')]")?.InnerText.Trim();
                var webThickness = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Толщина полотна')]/following-sibling::div//a[contains(@class, 'active')]")?.InnerText.Trim();
                var imgSrcs = htmlDoc.DocumentNode.SelectNodes("//product-images-swiper//img[@use-lens]")?
                    .Take(3)
                    .Select(n =>
                    {
                        var src = n.GetAttributeValue("src", string.Empty);
                        if (!string.IsNullOrWhiteSpace(src))
                        {
                            return new Uri(new Uri(baseUrl), src).AbsoluteUri;
                        }
                        else
                        {
                            return null;
                        }
                    });

                var rowData = new List<object?>
                {
                    productName,
                    pageUrl,
                    null,
                    description,
                    colorName,
                    collection,
                    openingSystemType,
                    null,
                    size,
                    sideliness,
                    height,
                    width,
                    typeOfStopperCloser,
                    webThickness
                };
                if (imgSrcs != null)
                {
                    rowData.AddRange(imgSrcs);
                }
                await WriteRowToSheets("Умные системы", rowData);
            }

            async Task ParseSlidingSystemPage(string pageUrl)
            {
                var html = await GetHtmlAsync(pageUrl);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var h1Node = htmlDoc.DocumentNode.SelectSingleNode("//h1");
                if (h1Node == null)
                {
                    Console.WriteLine("H1 tag not found on sliding system page");
                    return;
                }

                var productName = HttpUtility.HtmlDecode(h1Node.InnerText.Trim());

                string? description = null;
                var vHtmlValue = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'ql-editor')]")?.GetAttributeValue("v-html", string.Empty);
                if (vHtmlValue != null)
                {
                    var textInsideQuotes = GeneratedRegexes.Quoted().Match(vHtmlValue).Value;
                    var decoded = HttpUtility.HtmlDecode(textInsideQuotes);
                    decoded = HttpUtility.HtmlDecode(decoded);
                    var htmlDoc2 = new HtmlDocument();
                    htmlDoc2.LoadHtml(decoded);
                    description = htmlDoc2.DocumentNode.InnerText.Trim(' ', '"');
                }

                var colorName = GeneratedRegexes.ColorAfterCvet().Match(productName).Value.Trim(' ', '-');
                var collection = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Коллекция')]/following-sibling:*")?.InnerText.Trim();
                var weight = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Вес')]/following-sibling::div//a[contains(@class, 'active')]")?.InnerText.Trim();
                var typeOfFastening = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Тип крепления')]/following-sibling::div//a[contains(@class, 'active')]")?.InnerText.Trim();
                var numberOfValves = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Количество створок')]/following-sibling::div//a[contains(@class, 'active')]")?.InnerText.Trim();
                var width = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Ширина')]/following-sibling::div//a[contains(@class, 'active')]")?.InnerText.Trim();
                var typeOfStopperCloser = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Тип стопора/доводчика')]/following-sibling::div//a[contains(@class, 'active')]")?.InnerText.Trim();
                var imgSrcs = htmlDoc.DocumentNode.SelectNodes("//product-images-swiper//img[@use-lens]")?
                    .Take(3)
                    .Select(n =>
                    {
                        var src = n.GetAttributeValue("src", string.Empty);
                        if (!string.IsNullOrWhiteSpace(src))
                        {
                            return new Uri(new Uri(baseUrl), src).AbsoluteUri;
                        }
                        else
                        {
                            return null;
                        }
                    });

                var rowData = new List<object?>
                {
                    productName,
                    pageUrl,
                    null,
                    description,
                    colorName,
                    collection,
                    null,
                    weight,
                    typeOfFastening,
                    numberOfValves,
                    width,
                    typeOfStopperCloser
                };
                if (imgSrcs != null)
                {
                    rowData.AddRange(imgSrcs);
                }
                await WriteRowToSheets("Раздвижные системы", rowData);
            }

            async Task ParseHardwarePage(string pageUrl)
            {
                var html = await GetHtmlAsync(pageUrl);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var h1Node = htmlDoc.DocumentNode.SelectSingleNode("//h1");
                if (h1Node == null)
                {
                    Console.WriteLine("H1 tag not found on hardware page");
                    return;
                }

                var productName = h1Node.InnerText.Trim();

                string? description = null;
                var vHtmlValue = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'ql-editor')]")?.GetAttributeValue("v-html", string.Empty);
                if (vHtmlValue != null)
                {
                    var inner = GeneratedRegexes.Quoted().Match(vHtmlValue).Value;
                    var decoded = System.Web.HttpUtility.HtmlDecode(inner);
                    decoded = System.Web.HttpUtility.HtmlDecode(decoded);
                    var htmlDoc2 = new HtmlDocument();
                    htmlDoc2.LoadHtml(decoded);
                    description = htmlDoc2.DocumentNode.InnerText.Trim(' ', '"');
                }

                var colorName = GeneratedRegexes.ColorAfterCvet().Match(productName).Value.Trim(' ', '-');
                var collection = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Коллекция')]/following-sibling:*")?.InnerText.Trim();
                var material = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Материал')]/following-sibling::span")?.InnerText.Trim();
                var typeOfHardware = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Тип скобяных изделий')]/following-sibling::span")?.InnerText.Trim();
                var size = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Размер')]/following-sibling::div//a[contains(@class, 'active')]")?.InnerText.Trim();
                var imgSrcs = htmlDoc.DocumentNode.SelectNodes("//product-images-swiper//img[@use-lens]")?
                    .Take(3)
                    .Select(n => new Uri(new Uri(baseUrl), n.GetAttributeValue("src", string.Empty)).AbsoluteUri);

                var rowData = new List<object?>
                {
                    productName,
                    pageUrl,
                    null,
                    description,
                    colorName,
                    null,
                    collection,
                    material,
                    typeOfHardware,
                    size
                };
                if (imgSrcs != null)
                {
                    rowData.AddRange(imgSrcs);
                }
                await WriteRowToSheets("Скобяные изделия", rowData);
            }

            async Task ParseDoorHingePage(string url)
            {
                var html = await GetHtmlAsync(url);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var h1Node = htmlDoc.DocumentNode.SelectSingleNode("//h1");
                if (h1Node == null)
                {
                    Console.WriteLine("H1 tag not found on hinge page");
                    return;
                }

                var productName = h1Node.InnerText.Trim();

                string? description = null;
                var vHtmlValue = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'ql-editor')]")?.GetAttributeValue("v-html", string.Empty);
                if (vHtmlValue != null)
                {
                    var inner = GeneratedRegexes.Quoted().Match(vHtmlValue).Value;
                    var decoded = System.Web.HttpUtility.HtmlDecode(inner);
                    decoded = System.Web.HttpUtility.HtmlDecode(decoded);
                    var htmlDoc2 = new HtmlDocument();
                    htmlDoc2.LoadHtml(decoded);
                    description = htmlDoc2.DocumentNode.InnerText.Trim(' ', '"');
                }

                var colorName = GeneratedRegexes.ColorAfterCvet().Match(productName).Value.Trim(' ', '-');
                var hingeType = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Тип петли')]/following-sibling::span")?.InnerText.Trim();
                var collection = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Коллекция')]/following-sibling:*")?.InnerText.Trim();
                var material = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Материал')]/following-sibling::span")?.InnerText.Trim();
                var insertType = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Тип врезки')]/following-sibling::span")?.InnerText.Trim();
                var imgSrcs = htmlDoc.DocumentNode.SelectNodes("//product-images-swiper//img[@use-lens]")?
                    .Take(3)
                    .Select(n => new Uri(new Uri(baseUrl), n.GetAttributeValue("src", string.Empty)).AbsoluteUri);

                var rowData = new List<object?>
                {
                    productName,
                    url,
                    null,
                    description,
                    colorName,
                    hingeType,
                    collection,
                    material,
                    insertType
                };
                if (imgSrcs != null)
                {
                    rowData.AddRange(imgSrcs);
                }
                await WriteRowToSheets("Петли", rowData);
            }

            async Task GetDoorHandlesUrlsFromPage(string pageUrl)
            {
                var html = await GetHtmlAsync(pageUrl);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var hrefs = htmlDoc.DocumentNode.SelectNodes("//div[@class='similar_items_block']//picture/a[@href]");
                if (hrefs != null)
                {
                    foreach (var href in hrefs
                        .Select(n => n.GetAttributeValue("href", string.Empty))
                        .Where(v => !string.IsNullOrEmpty(v)))
                    {
                        doorHandlesUrls.Add(new Uri(new Uri(baseUrl), href).AbsoluteUri);
                    }
                }

                var hrefs2 = htmlDoc.DocumentNode.SelectNodes("//a[@rel='nofollow']");
                if (hrefs2 != null)
                {
                    foreach (var href in hrefs2
                        .Select(n => n.GetAttributeValue("href", string.Empty))
                        .Where(v => !string.IsNullOrEmpty(v) && v.Contains("/catalog/")))
                    {
                        doorHandlesUrls.Add(new Uri(new Uri(baseUrl), href).AbsoluteUri);
                    }
                }
            }

            async Task ParseLocksPage(string url)
            {
                var html = await GetHtmlAsync(url);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);
                var aTags = htmlDoc.DocumentNode.SelectNodes("//div[@class='similar_items_block']//picture/a");
                if (aTags == null || aTags.Count == 0)
                {
                    Console.WriteLine("No lock URLs found on page");
                    return;
                }

                var lockUrls = new List<string>();
                foreach (var aTag in aTags)
                {
                    var href = aTag.GetAttributeValue("href", string.Empty);
                    if (!string.IsNullOrEmpty(href))
                    {
                        lockUrls.Add(new Uri(new Uri(baseUrl), href).AbsoluteUri);
                    }
                }
                foreach (var lockUrl in lockUrls)
                {
                    await ParseLockPage(lockUrl, true);
                }
            }

            async Task ParseLockPage(string url, bool parseColors)
            {
                Console.WriteLine($"parse lock page {url}");
                string html = string.Empty;
                try
                {
                    html = await GetHtmlAsync(url);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception on getting html from url {url}: {ex.Message}");
                    return;
                }
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var h1Node = htmlDoc.DocumentNode.SelectSingleNode("//h1");
                if (h1Node == null)
                {
                    Console.WriteLine("H1 tag not found on lock page");
                    return;
                }

                var productName = h1Node.InnerText.Trim();

                var vHtmlAttribute = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'ql-editor')]")?.GetAttributeValue("v-html", string.Empty) ?? string.Empty;
                var decoded = System.Web.HttpUtility.HtmlDecode(vHtmlAttribute);
                decoded = System.Web.HttpUtility.HtmlDecode(decoded) ?? string.Empty;
                var spanHtml = GeneratedRegexes.SpanOrP().Match(decoded ?? string.Empty).Value;
                var htmlDoc2 = new HtmlDocument();
                htmlDoc2.LoadHtml(spanHtml);
                var description = htmlDoc2.DocumentNode.SelectSingleNode("//span")?.InnerText.Trim() ?? htmlDoc2.DocumentNode.SelectSingleNode("//p")?.InnerText.Trim();

                var colorName = GeneratedRegexes.ColorAfterCvetPlus().Match(productName).Value.Replace("-", "").Trim();
                var collection = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Коллекция')]/following-sibling::span")?.InnerText.Trim();
                var type = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Тип замка/защелки')]/following-sibling::span")?.InnerText.Trim();
                var material = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Материал язычка(защелки)')]/following-sibling::span")?.InnerText.Trim();
                var centerDistance = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Межосевое расстояние')]/following-sibling::span")?.InnerText.Trim();
                var imgSrcs = htmlDoc.DocumentNode.SelectNodes("//product-images-swiper//img[@use-lens]")?
                    .Take(3)
                    .Select(n => new Uri(new Uri(baseUrl), n.GetAttributeValue("src", string.Empty)).AbsoluteUri);

                var rowData = new List<object?>
                {
                    productName,
                    url,
                    null,
                    description,
                    colorName,
                    collection,
                    type,
                    material,
                    centerDistance
                };
                if (imgSrcs != null)
                {
                    rowData.AddRange(imgSrcs);
                }
                await WriteRowToSheets("Замки и защелки", rowData);

                if (parseColors)
                {
                    var colors = htmlDoc.DocumentNode.SelectNodes("//fieldset[@id='color-picker']//a");
                    if (colors != null && colors.Count > 1)
                    {
                        foreach (var href in colors.Skip(1).Select(c => c.GetAttributeValue("href", string.Empty)).Where(h => !string.IsNullOrEmpty(h)))
                        {
                            await ParseLockPage(new Uri(new Uri(baseUrl), href).AbsoluteUri, false);
                        }
                    }
                }
            }

            async Task ParseDoorHandlePage(string url)
            {
                var html = await GetHtmlAsync(url);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var h1Node = htmlDoc.DocumentNode.SelectSingleNode("//h1");
                if (h1Node == null)
                {
                    Console.WriteLine("H1 tag not found on door handle page");
                    return;
                }

                var productName = h1Node.InnerText.Trim();

                string? description = null;
                var vHtmlValue = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'ql-editor')]")?.GetAttributeValue("v-html", string.Empty);
                if (vHtmlValue != null)
                {
                    var inner = GeneratedRegexes.Quoted().Match(vHtmlValue).Value;
                    var decoded = System.Web.HttpUtility.HtmlDecode(inner);
                    decoded = System.Web.HttpUtility.HtmlDecode(decoded);
                    var htmlDoc2 = new HtmlDocument();
                    htmlDoc2.LoadHtml(decoded);
                    description = htmlDoc2.DocumentNode.InnerText.Trim(' ', '"');
                }

                var colorName = GeneratedRegexes.ColorAfterCvet().Match(productName).Value.Trim(' ', '-');
                var rosetteShape = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(), 'Форма розетки')]/following-sibling::span")?.InnerText.Trim();
                var collection = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Коллекция')]/following-sibling::span")?.InnerText.Trim();
                var material = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Материал')]/following-sibling::span")?.InnerText.Trim();
                var type = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(),'Тип:')]/following-sibling::span")?.InnerText.Trim();
                var socketType = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(text(),'Тип розетки')]/following-sibling::div")?.InnerText.Trim();
                var imgSrcs = htmlDoc.DocumentNode.SelectNodes("//product-images-swiper//img[@use-lens]")?
                    .Take(3)
                    .Select(n => new Uri(new Uri(baseUrl), n.GetAttributeValue("src", string.Empty)).AbsoluteUri);

                var rowData = new List<object?>
                {
                    productName,
                    url,
                    null,
                    description,
                    colorName,
                    rosetteShape,
                    collection,
                    material,
                    type,
                    null,
                    socketType
                };
                if (imgSrcs != null)
                {
                    rowData.AddRange(imgSrcs);
                }
                await WriteRowToSheets("Ручки", rowData);
            }

            async Task<string> GetHtmlAsync(string url)
            {
                Console.WriteLine("get html from " + url);
                await Task.Delay(requestDelayMs);
                try
                {
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    Console.WriteLine("success");
                    return await response.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("exception on getting html from url " + url);
                    Console.WriteLine(ex.Message);
                    Console.WriteLine("waiting for retry");
                    await Task.Delay(retryDelayMs);
                    return await GetHtmlAsync(url);
                }
            }

            async Task WriteRowToSheets(string sheetName, List<object?> rowData)
            {
                string range = $"{sheetName}!A{row}";
                var values = new List<IList<object>> { rowData.Cast<object>().ToList() };

                GoogleCredential credential;
                using (var stream = File.OpenRead(credentialPath))
                {
                    credential = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets);
                }

                var service = new SheetsService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "ProductCatalogToSheets",
                });

                var body = new ValueRange { Values = values };
                var request = service.Spreadsheets.Values.Update(body, spreadsheetId, range);
                request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;

                var response = await request.ExecuteAsync();
                Console.WriteLine("Данные успешно записаны!");
                lock (rowLock)
                {
                    row++;
                }
            }

            for (int i = 1; i <= 5; i++)
            {
                await ParseLocksPage($"{baseUrl.TrimEnd('/')}/catalog/zamki-i-zashchelki/page/{i}");
            }

            await ParseLocksPage($"{baseUrl.TrimEnd('/')}/catalog/zamki-i-zashchelki");

            for (int i = 1; i <= 25; i++)
            {
                await GetDoorHandlesUrlsFromPage($"{baseUrl.TrimEnd('/')}/catalog/ruchki/page/{i}");
            }
            var ordered = doorHandlesUrls.Order();
            using (StreamWriter writer = new("doorHandlesUrls2.txt"))
            {
                foreach (var d in ordered)
                {
                    writer.WriteLine(d);
                }
            }

            foreach (var line in File.ReadLines("doorHandlesUrls2.txt"))
            {
                await ParseDoorHandlePage(line);
            }

            await ParseDoorHandlePage($"{baseUrl.TrimEnd('/')}/catalog/ruchki/all/2109");

            for (int i = 1; i <= 4; i++)
            {
                await GetDoorHingesUrlsFromPage($"{baseUrl.TrimEnd('/')}/catalog/petli/page/{i}");
            }
            var ordered2 = doorHingesUrls.Order();
            using (StreamWriter writer = new("doorHingesUrls.txt"))
            {
                foreach (var d in ordered2)
                {
                    writer.WriteLine(d);
                }
            }

            await ParseDoorHingePage($"{baseUrl.TrimEnd('/')}/catalog/petli/all/92");

            foreach (var line in File.ReadLines("doorHingesUrls.txt"))
            {
                await ParseDoorHingePage(line);
            }

            for (int i = 1; i <= 11; i++)
            {
                await GetHardwareUrlsFromPage($"{baseUrl.TrimEnd('/')}/catalog/skobyanye-izdeliya/page/{i}");
            }
            var ordered3 = hardwareUrls.Order();
            using (StreamWriter writer = new("hardwareUrls.txt"))
            {
                foreach (var url in ordered3)
                {
                    writer.WriteLine(url);
                }
            }

            await ParseHardwarePage($"{baseUrl.TrimEnd('/')}/catalog/skobyanye-izdeliya/all/577");

            foreach (var line in File.ReadLines("hardwareUrls.txt"))
            {
                await ParseHardwarePage(line);
            }

            for (int i = 1; i <= 2; i++)
            {
                await GetSlidingSystemsUrlsFromPage($"{baseUrl.TrimEnd('/')}/catalog/razdvizhnye-sistemy/page/{i}");
            }
            var ordered4 = slidingSystemsUrls.Order();
            using (StreamWriter writer = new("slidingSystemsUrls.txt"))
            {
                foreach (var url in ordered4)
                {
                    writer.WriteLine(url);
                }
            }

            foreach (var line in File.ReadLines("slidingSystemsUrls.txt"))
            {
                await ParseSlidingSystemPage(line);
            }

            for (int i = 1; i <= 2; i++)
            {
                await GetSmartSystemsUrlsFromPage($"{baseUrl.TrimEnd('/')}/catalog/umnye-sistemy-otkryvaniya/page/{i}");
            }
            var ordered5 = smartSystemsUrls.Order();
            using (StreamWriter writer = new("smartSystemsUrls.txt"))
            {
                foreach (var url in ordered5)
                {
                    writer.WriteLine(url);
                }
            }

            foreach (var line in File.ReadLines("smartSystemsUrls.txt"))
            {
                await ParseSmartSystemPage(line);
            }

            Console.Read();
        }

        internal static partial class GeneratedRegexes
        {
            [GeneratedRegex("\"([^\"]*)\"")]
            internal static partial Regex Quoted();

            [GeneratedRegex("(?<=цвет\\s)([^,]*)(?=,*)")]
            internal static partial Regex ColorAfterCvet();

            [GeneratedRegex("(?<=цвет\\s+)(.+)")]
            internal static partial Regex ColorAfterCvetPlus();

            [GeneratedRegex("<span.*<\\/span>|<p.*<\\/p>")]
            internal static partial Regex SpanOrP();
        }
    }
}
