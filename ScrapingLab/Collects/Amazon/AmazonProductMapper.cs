using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ScrapingLab.Models;
using ScrapingLab.Collects.Amazon.Models;

namespace ScrapingLab.Collects.Amazon;

/// <summary>Maps an observed Amazon response into the user's Product classes.</summary>
public sealed class AmazonProductMapper
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex AsinInUrl = new(@"/(?:dp|gp/product)/([A-Z0-9]{10})(?:[/?]|$)", RegexOptions.IgnoreCase);

    public AmazonProductMappingResult Map(AmazonProduct details, string html)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(html);
        var document = new HtmlParser().ParseDocument(html);
        var result = new AmazonProductMappingResult
        {
            Product = new Product
            {
                Code = details.SelectedAsin,
                Title = details.FullTitle,
                Brand = details.Brand,
                ItemUrl = details.CanonicalUrl ?? details.FinalUrl,
                ItemImages = details.ImageUrls.Distinct(StringComparer.Ordinal).ToList(),
                Price = MapPrice(details.CurrentPrice)
            }
        };

        if (result.Product.ItemImages.Count == 0 && details.MainImageUrl is { } mainImage)
            result.Product.ItemImages.Add(mainImage);

        var detailFragments = ReadDetailFragments(document, details);
        var descriptions = details.Features.Select(Clean).OfType<string>().ToList();
        if (details.Description is { } description)
            descriptions.AddRange(description.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(Clean).OfType<string>());
        descriptions.AddRange(detailFragments.SelectMany(x => x.QuerySelectorAll("h1, h2, h3, h4, p"))
            .Where(x => x.Closest("noscript") is null).Select(ReadText).OfType<string>());
        descriptions = descriptions.Distinct(StringComparer.Ordinal).ToList();
        result.Product.Description = descriptions.Count == 0 ? null : descriptions;
        result.Product.DetailHtml = BuildDetailHtml(document, detailFragments, descriptions, details.FinalUrl);

        MapOptions(document, details, result);
        return result;
    }

    public static Price? MapPrice(AmazonPrice? observed)
    {
        // Missing amount is unknown, not a zero-valued offer.
        return observed?.Amount is { } amount
            ? new Price { Amount = (double)amount, Currency = observed.Currency }
            : null;
    }

    private static void MapOptions(IDocument document, AmazonProduct details, AmazonProductMappingResult result)
    {
        var twister = document.QuerySelectorAll("script")
            .FirstOrDefault(x => x.TextContent.Contains("twister-js-init-dpx-data", StringComparison.Ordinal))?.TextContent;
        if (twister is null || ReadJsonProperty(twister, "dimensions") is not { ValueKind: JsonValueKind.Array } dimensions
            || ReadJsonProperty(twister, "variationValues") is not { ValueKind: JsonValueKind.Object } variationValues)
        {
            result.Warnings.Add("Full option inventory was not present in twister state; only the observed selected combination can be mapped.");
            MapObservedOptions(details, result);
            return;
        }

        var sourceOrder = dimensions.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!).ToList();
        if (sourceOrder.Count != dimensions.GetArrayLength() || sourceOrder.Count == 0
            || sourceOrder.Distinct(StringComparer.Ordinal).Count() != sourceOrder.Count)
        {
            result.Warnings.Add("Twister dimension order was invalid; full combinations could not be safely interpreted.");
            MapObservedOptions(details, result);
            return;
        }

        var outputOrder = sourceOrder.ToList();
        if (ReadJsonProperty(twister, "reorderedDimensionListKeys") is { ValueKind: JsonValueKind.Array } reordered)
        {
            var uiOrder = reordered.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!).ToList();
            if (uiOrder.Count == sourceOrder.Count && uiOrder.Distinct(StringComparer.Ordinal).Count() == sourceOrder.Count
                && uiOrder.All(sourceOrder.Contains)) outputOrder = uiOrder;
        }

        var labels = ReadJsonProperty(twister, "variationDisplayLabels");
        var valuesByDimension = new Dictionary<string, Dictionary<int, OptionValue>>(StringComparer.Ordinal);
        var combinations = new List<Combination>();
        foreach (var dimension in outputOrder)
        {
            if (!variationValues.TryGetProperty(dimension, out var values) || values.ValueKind != JsonValueKind.Array)
            {
                result.Warnings.Add($"Twister values for {dimension} were missing; full combinations could not be safely interpreted.");
                MapObservedOptions(details, result);
                return;
            }

            var valuesByIndex = new Dictionary<int, OptionValue>();
            for (var index = 0; index < values.GetArrayLength(); index++)
            {
                if (values[index].ValueKind != JsonValueKind.String || Clean(values[index].GetString()) is not { } name) continue;
                var observed = details.Variants.FirstOrDefault(x => x.Dimension == dimension && x.Label == name);
                valuesByIndex[index] = new OptionValue
                {
                    Code = dimension + ":" + index.ToString(CultureInfo.InvariantCulture),
                    Name = name,
                    ImageUrl = observed?.ImageUrl
                };
            }
            valuesByDimension[dimension] = valuesByIndex;
            combinations.Add(new Combination
            {
                Code = dimension,
                Name = JsonText(labels, dimension) ?? DimensionLabel(dimension),
                OptionValues = valuesByIndex.Values.ToList()
            });
        }
        result.Product.Option = new Option { Combinations = combinations, Independencies = [] };

        if (ReadJsonProperty(twister, "dimensionToAsinMap") is { ValueKind: JsonValueKind.Object } asinMap)
        {
            // Every entry below comes from a real ASIN mapping. Never generate a Cartesian product.
            foreach (var entry in asinMap.EnumerateObject())
            {
                var asin = entry.Value.ValueKind == JsonValueKind.String ? ValidAsin(entry.Value.GetString()) : null;
                var indexParts = entry.Name.Split('_');
                if (asin is null || indexParts.Length != sourceOrder.Count)
                {
                    result.Warnings.Add($"Skipped invalid twister combination {entry.Name}.");
                    continue;
                }
                var selectedValues = new Dictionary<string, OptionValue>(StringComparer.Ordinal);
                for (var position = 0; position < sourceOrder.Count; position++)
                {
                    if (!int.TryParse(indexParts[position], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                        || !valuesByDimension[sourceOrder[position]].TryGetValue(index, out var value)) break;
                    selectedValues[sourceOrder[position]] = value;
                }
                if (selectedValues.Count != sourceOrder.Count)
                {
                    result.Warnings.Add($"Skipped out-of-range twister combination {entry.Name}.");
                    continue;
                }
                AddTarget(result, details, asin, outputOrder.Select(x => selectedValues[x]));
            }
        }

        if (result.VariantTargets.Count == 0)
        {
            result.Warnings.Add("No valid child-ASIN combination map was present; retained only the observed selected combination.");
            AddSelectedFromDefinitions(details, result, combinations);
        }
        else if (ValidAsin(details.SelectedAsin) is { } selectedAsin && !result.VariantTargets.Any(x => x.Asin == selectedAsin))
        {
            result.Warnings.Add("The selected ASIN was absent from the child map; attempted to retain its explicitly selected dimension labels.");
            AddSelectedFromDefinitions(details, result, combinations);
        }

        var missingPrices = result.VariantTargets.Count(x => x.Independency.Price is null);
        if (missingPrices > 0)
            result.Warnings.Add($"The initial HTML has no price for {missingPrices} mapped combinations; their prices require ASIN-keyed bulk slot observations.");
    }

    private static void MapObservedOptions(AmazonProduct details, AmazonProductMappingResult result)
    {
        var groups = details.Variants.GroupBy(x => x.Dimension, StringComparer.Ordinal)
            .OrderBy(x => x.Key == "color_name" ? 0 : x.Key == "size_name" ? 1 : 2).ToList();
        var combinations = groups.Select(group => new Combination
        {
            Code = group.Key,
            Name = DimensionLabel(group.Key),
            OptionValues = group.DistinctBy(x => x.Label).Select(value => new OptionValue
            {
                // The original index is unavailable. Keep a distinguishable label-based fallback.
                Code = group.Key + ":label:" + Uri.EscapeDataString(value.Label),
                Name = value.Label,
                ImageUrl = value.ImageUrl
            }).ToList()
        }).ToList();
        if (combinations.Count == 0) return;
        result.Product.Option = new Option { Combinations = combinations, Independencies = [] };
        AddSelectedFromDefinitions(details, result, combinations);
    }

    private static void AddSelectedFromDefinitions(AmazonProduct details, AmazonProductMappingResult result, List<Combination> combinations)
    {
        if (ValidAsin(details.SelectedAsin) is not { } selectedAsin) return;
        var selected = new List<OptionValue>();
        foreach (var dimension in combinations)
        {
            var name = details.Variants.FirstOrDefault(x => x.Dimension == dimension.Code && x.IsSelected)?.Label
                ?? (dimension.Code == "color_name" ? details.SelectedColor : dimension.Code == "size_name" ? details.SelectedSize : null);
            var value = dimension.OptionValues?.FirstOrDefault(x => x.Name == name);
            if (value is null)
            {
                result.Warnings.Add($"Selected value for {dimension.Code} was not observed; a complete selected combination could not be formed.");
                return;
            }
            selected.Add(value);
        }
        AddTarget(result, details, selectedAsin, selected);
    }

    private static void AddTarget(AmazonProductMappingResult result, AmazonProduct details, string asin, IEnumerable<OptionValue> values)
    {
        var ordered = values.ToList();
        var codes = string.Join("|", ordered.Select(x => x.Code));
        if (result.VariantTargets.Any(x => x.Codes == codes)) return;
        var names = string.Join("|", ordered.Select(x => x.Name));
        var isSelected = asin == details.SelectedAsin;
        var combination = new Independency
        {
            Codes = codes,
            Names = names,
            Price = isSelected ? MapPrice(details.CurrentPrice) : null
        };
        result.Product.Option!.Independencies!.Add(combination);
        result.VariantTargets.Add(new AmazonVariantTarget
        {
            Asin = asin,
            Codes = codes,
            Names = names,
            IsSelected = isSelected,
            Independency = combination
        });
    }

    private static List<IElement> ReadDetailFragments(IDocument document, AmazonProduct details)
    {
        var fragments = new List<IElement>();
        var description = document.QuerySelector("#productDescription") ?? document.QuerySelector("#dp_productDescription_container_div");
        if (description is not null && (ReadText(description) is not null || description.QuerySelector("img") is not null))
            fragments.Add(description);
        fragments.AddRange(document.QuerySelectorAll("#aplus_feature_div .aplus-module")
            .Where(module => IsDescriptionModule(module, details)));
        return fragments;
    }

    private static bool IsDescriptionModule(IElement module, AmazonProduct details)
    {
        var identity = module.ClassName + " " + module.GetAttribute("cel_widget_id");
        if (module.ClassList.Contains("module-5")
            || Regex.IsMatch(identity, @"comparison|compare|brand[-_]?story|recommend|shoppable", RegexOptions.IgnoreCase)) return false;
        if (module.QuerySelector(".apm-tablemodule, .apm-tablemodule-table, .aplus-chart, [class*='comparison'], [class*='brand-story'], [class*='recommend']") is not null) return false;
        if (module.Closest("#aplusBrandStory_feature_div, [class*='brand-story'], [class*='recommend']") is not null) return false;
        return !module.QuerySelectorAll("a[href]").Any(link => ExtractAsin(link.GetAttribute("href")) is { } asin
            && asin != details.SelectedAsin && asin != details.ParentAsin);
    }

    private static string? BuildDetailHtml(IDocument document, List<IElement> fragments, List<string> descriptions, string finalUrl)
    {
        var container = document.CreateElement("div");
        container.SetAttribute("class", "product-detail");
        foreach (var fragment in fragments)
        {
            var clone = (IElement)fragment.Clone(true);
            foreach (var excluded in clone.QuerySelectorAll("script, style, noscript")) excluded.Remove();
            foreach (var element in clone.QuerySelectorAll("*"))
                foreach (var attribute in element.Attributes.Where(x => x.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)).ToList())
                    element.RemoveAttribute(attribute.Name);
            foreach (var image in clone.QuerySelectorAll("img"))
            {
                var raw = Clean(image.GetAttribute("data-src")) ?? Clean(image.GetAttribute("src"));
                if (AbsoluteUrl(finalUrl, raw) is { } url && !Regex.IsMatch(url, @"grey-pixel|transparent-pixel|/x-locale/common/|(?:^|/)pixel\.gif", RegexOptions.IgnoreCase))
                    image.SetAttribute("src", url);
                else image.Remove();
                image.RemoveAttribute("data-src");
                image.RemoveAttribute("srcset");
                image.SetAttribute("style", "max-width:100%;height:auto;");
            }
            container.AppendChild(clone);
        }
        if (container.ChildElementCount == 0)
            foreach (var text in descriptions)
            {
                var paragraph = document.CreateElement("p");
                paragraph.TextContent = text;
                container.AppendChild(paragraph);
            }
        return container.ChildElementCount == 0 ? null : container.OuterHtml;
    }

    private static JsonElement? ReadJsonProperty(string script, string property) =>
        AmazonEmbeddedJson.ReadProperty(script, property);

    private static string? JsonText(JsonElement? value, string key) => value is { ValueKind: JsonValueKind.Object } json
        && json.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? Clean(property.GetString()) : null;
    private static string DimensionLabel(string key) => key == "color_name" ? "Color" : key == "size_name" ? "Size" : key;
    private static string? ValidAsin(string? value) => value is not null && Regex.IsMatch(value.Trim(), @"^[A-Z0-9]{10}$", RegexOptions.IgnoreCase)
        ? value.Trim().ToUpperInvariant() : null;
    private static string? ExtractAsin(string? url)
    {
        var match = AsinInUrl.Match(url ?? "");
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }
    private static string? AbsoluteUrl(string baseUrl, string? value) => Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
        && !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(baseUri, value, out var uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
    private static string? ReadText(IElement element)
    {
        var clone = (IElement)element.Clone(true);
        foreach (var excluded in clone.QuerySelectorAll("script, style, noscript")) excluded.Remove();
        return Clean(clone.TextContent);
    }
    private static string? Clean(string? value)
    {
        if (value is null) return null;
        value = Whitespace.Replace(value.Replace("\u200e", "").Replace("\u200f", "").Replace("\u200b", ""), " ").Trim();
        return value.Length == 0 ? null : value;
    }
}
