using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ScrapingLab.Collects.Amazon.Models;
using ScrapingLab.Models;

namespace ScrapingLab.Collects.Amazon;

/// <summary>ASIN별 JSON을 &&&로 구분한 Amazon AJAX 응답을 읽습니다.</summary>
public sealed class AmazonVariantBulkResponseParser
{
    public AmazonVariantBulkResponse Parse(string response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var result = new AmazonVariantBulkResponse();
        if (response.TrimStart().StartsWith('<'))
        {
            var document = new HtmlParser().ParseDocument(response);
            var challenge = document.QuerySelector("#captchacharacters, form[action*='validateCaptcha']") is not null
                || document.Title?.Contains("Robot Check", StringComparison.OrdinalIgnoreCase) == true;
            throw new AmazonProductParsingException("벌크 요청이 JSON 대신 HTML 응답을 반환했습니다.", challenge);
        }
        foreach (var fragment in Regex.Split(response, @"(?m)^\s*&&&\s*$"))
        {
            if (string.IsNullOrWhiteSpace(fragment)) continue;
            try
            {
                using var json = JsonDocument.Parse(fragment);
                ReadRecords(json.RootElement, result);
            }
            catch (JsonException exception) { result.Warnings.Add("Malformed bulk JSON fragment: " + exception.Message); }
        }
        return result;
    }

    private static void ReadRecords(JsonElement value, AmazonVariantBulkResponse result)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in value.EnumerateArray()) ReadRecords(record, result);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        var asin = AmazonVariantBulkRequestBuilder.NormalizeAsin(Text(value, "ASIN") ?? Text(value, "asin"));
        if (asin is null)
        {
            foreach (var child in value.EnumerateObject())
                if (child.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object) ReadRecords(child.Value, result);
            return;
        }
        var observation = new VariantPriceObservation { Asin = asin, ResponseAsin = asin };
        result.Observations.Add(observation);
        var content = Property(Property(value, "Value"), "content") ?? Property(value, "content") ?? value;
        var slot = Property(content, "twisterSlotJson");
        observation.IsAvailable = Boolean(slot, "isAvailable");
        observation.RawPriceAmount = Number(slot, "price");
        var markup = Text(content, "twisterSlotDiv") ?? Text(content, "html")
            ?? (content.ValueKind == JsonValueKind.String ? content.GetString() : null);
        var currency = Text(slot, "currencyCode") ?? Text(slot, "currency") ?? Text(slot, "currencySymbol");
        if (currency is not null && !Regex.IsMatch(currency, "^[A-Za-z]{3}$")) currency = null;
        currency = currency?.ToUpperInvariant();
        if (observation.IsAvailable == false)
        {
            observation.Error = "The bulk response reports this variant as unavailable.";
            return;
        }

        AmazonPrice? displayPrice = null;
        if (!string.IsNullOrWhiteSpace(markup))
        {
            var document = new HtmlParser().ParseDocument(markup);
            foreach (var selector in new[] {
                ".apex-pricetopay-accessibility-label", "#apex-pricetopay-accessibility-label",
                ".priceToPay .a-offscreen", ".apex-pricetopay-value", "#priceblock_ourprice",
                ".a-price:not(.a-text-price) .a-offscreen" })
            {
                var raw = document.QuerySelector(selector)?.TextContent.Trim();
                if (string.IsNullOrWhiteSpace(raw) || !Regex.IsMatch(raw, @"\d")) continue;
                displayPrice = AmazonProductParser.ParsePriceText(raw, new AmazonProduct(), "VariantBulkPrice");
                observation.PriceDisplayText = raw;
                observation.Evidence = new AmazonFieldEvidence { Source = "Value.content.twisterSlotDiv " + selector, RawText = raw };
                break;
            }
        }
        if (currency is not null && displayPrice?.Currency is { } displayedCurrency && currency != displayedCurrency)
        {
            observation.Error = "The structured and displayed bulk price currencies disagree.";
            return;
        }
        currency ??= displayPrice?.Currency;
        // Product의 페이지 가격과 같은 표시 금액을 사용하고 정밀 JSON 금액은 진단 모델에 보관합니다.
        var amount = displayPrice?.Amount ?? observation.RawPriceAmount;
        if (amount is null or < 0 || string.IsNullOrWhiteSpace(currency))
        {
            observation.Error = "The bulk response did not expose an unambiguous price and currency.";
            return;
        }
        observation.Price = new Price { Amount = (double)amount.Value, Currency = currency };
        observation.Evidence ??= new AmazonFieldEvidence
        {
            Source = "Value.content.twisterSlotJson.price + currency",
            RawText = amount.Value.ToString(CultureInfo.InvariantCulture) + " " + currency
        };
    }

    private static JsonElement? Property(JsonElement? element, string key) => element is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(key, out var property) ? property : null;
    private static string? Text(JsonElement? element, string key) => Property(element, key) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    private static decimal? Number(JsonElement? element, string key) => Property(element, key) is { ValueKind: JsonValueKind.Number } value
        && value.TryGetDecimal(out var amount) ? amount : null;
    private static bool? Boolean(JsonElement? element, string key) => Property(element, key) is { } value
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
