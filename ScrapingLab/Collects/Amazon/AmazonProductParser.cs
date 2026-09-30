using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ScrapingLab.Collects.Amazon.Models;

namespace ScrapingLab.Collects.Amazon;

/// <summary>Parses a captured page without JavaScript execution or additional network requests.</summary>
public sealed class AmazonProductParser
{
    private static readonly Regex AsinInUrl = new(@"/(?:dp|gp/product)/([A-Z0-9]{10})(?:[/?]|$)", RegexOptions.IgnoreCase);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public AmazonProduct Parse(string html, Uri requestedUrl, Uri finalUrl, DateTimeOffset fetchedAtUtc, int? httpStatusCode)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(requestedUrl);
        ArgumentNullException.ThrowIfNull(finalUrl);
        var document = new HtmlParser().ParseDocument(html);
        var product = new AmazonProduct
        {
            RequestedUrl = requestedUrl.AbsoluteUri,
            FinalUrl = finalUrl.AbsoluteUri,
            FetchedAtUtc = fetchedAtUtc.ToUniversalTime(),
            HttpStatusCode = httpStatusCode,
            RequestedAsin = ExtractAsin(requestedUrl.AbsoluteUri)
        };

        // Some Amazon pages contain a second #productTitle that is a hidden input.
        var titleElement = document.QuerySelector("span#productTitle, h1#title #productTitle");
        product.Title = Text(titleElement) ?? "";
        if (product.Title.Length == 0)
        {
            var isChallenge = document.QuerySelector("#captchacharacters, form[action*='validateCaptcha']") is not null
                || Regex.IsMatch(document.Title + " " + Text(document.Body), @"robot check|enter the characters you see below|type the characters you see|To discuss automated access", RegexOptions.IgnoreCase);
            throw new AmazonProductParsingException(isChallenge
                ? "Amazon returned a robot/CAPTCHA challenge instead of a product page. Preserve this response and retry after the challenge is resolved."
                : "The response does not contain a readable product title (#productTitle). It cannot be treated as a successfully extracted product.", isChallenge);
        }

        Evidence(product, nameof(product.Title), "span#productTitle", titleElement!.TextContent);
        product.TitleDifferentiators = Read(document, product, nameof(product.TitleDifferentiators), "#titleSection .dp-title-differentiators");
        product.FullTitle = product.TitleDifferentiators is null ? product.Title : product.Title + " | " + product.TitleDifferentiators;
        Evidence(product, nameof(product.FullTitle), "#titleSection #productTitle; #titleSection .dp-title-differentiators (combined display fields)", product.FullTitle);

        ParseIdentity(document, product, finalUrl);
        var byline = document.QuerySelector("#bylineInfo");
        var brandRaw = Text(byline);
        if (brandRaw is not null)
        {
            product.Brand = Regex.Replace(Regex.Replace(brandRaw, @"^Visit the\s+|^Brand:\s*", "", RegexOptions.IgnoreCase), @"\s+Store$", "", RegexOptions.IgnoreCase);
            product.BrandStoreUrl = AbsoluteUrl(finalUrl, byline?.GetAttribute("href"));
            Evidence(product, nameof(product.Brand), "#bylineInfo", byline!.TextContent);
            if (product.BrandStoreUrl is not null) Evidence(product, nameof(product.BrandStoreUrl), "#bylineInfo[href]", byline.GetAttribute("href")!);
        }

        product.CurrentPrice = ReadPrice(document, product, nameof(product.CurrentPrice),
            "#corePriceDisplay_desktop_feature_div #apex-pricetopay-accessibility-label",
            "#corePriceDisplay_desktop_feature_div .priceToPay .a-offscreen",
            "#corePriceDisplay_desktop_feature_div .priceToPay",
            "#corePriceDisplay_desktop_feature_div .a-price:not(.a-text-price) .a-offscreen",
            "#corePrice_feature_div .a-price:not(.a-text-price) .a-offscreen",
            "#apex_desktop #priceblock_ourprice", "#priceblock_ourprice", "#priceblock_dealprice",
            "#buybox #price_inside_buybox", "#buybox #newBuyBoxPrice");
        product.ListPrice = ReadPrice(document, product, nameof(product.ListPrice),
            "#corePriceDisplay_desktop_feature_div .basisPrice .a-offscreen",
            "#corePriceDisplay_desktop_feature_div .a-text-price .a-offscreen",
            "#corePrice_feature_div .a-text-price .a-offscreen", "#price .priceBlockStrikePriceString");
        if (product.CurrentPrice?.Currency is null && product.CurrentPrice is not null)
            product.Warnings.Add("Displayed price currency could not be identified unambiguously. The amazon.com host does not imply USD.");
        ParseOfferCharges(document, product);

        ParseVariants(document, product, finalUrl);
        product.SelectedColor ??= Read(document, product, nameof(product.SelectedColor), "#inline-twister-expanded-dimension-text-color_name", "#variation_color_name .selection");
        product.SelectedSize ??= Read(document, product, nameof(product.SelectedSize), "#inline-twister-expanded-dimension-text-size_name", "#variation_size_name .selection", "#native_dropdown_selected_size_name option:checked");

        product.RatingRawText = ReadAttributeOrText(document, product, nameof(product.Rating), "#averageCustomerReviews #acrPopover", "title")
            ?? ReadAttributeOrText(document, product, nameof(product.Rating), "#acrPopover", "title");
        product.Rating = NumberAtStart(product.RatingRawText);
        if (product.Rating is < 0 or > 5) { product.Rating = null; product.Warnings.Add("Rating text was outside the expected 0–5 range."); }
        product.ReviewCountRawText = ReadAttributeOrText(document, product, nameof(product.ReviewCount), "#averageCustomerReviews #acrCustomerReviewText", "aria-label")
            ?? ReadAttributeOrText(document, product, nameof(product.ReviewCount), "#acrCustomerReviewText", "aria-label");
        var reviewMatch = Regex.Match(product.ReviewCountRawText ?? "", @"[\d,]+(?:\.\d+)?");
        if (reviewMatch.Success && int.TryParse(reviewMatch.Value.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var count)) product.ReviewCount = count;

        ParseFeaturesAndDetails(document, product);
        foreach (var link in document.QuerySelectorAll("#wayfinding-breadcrumbs_feature_div a"))
        {
            if (Text(link) is { } name) product.Categories.Add(new AmazonCategory { Name = name, Url = AbsoluteUrl(finalUrl, link.GetAttribute("href")) });
        }
        if (product.Categories.Count > 0) Evidence(product, nameof(product.Categories), "#wayfinding-breadcrumbs_feature_div a", string.Join(" > ", product.Categories.Select(x => x.Name)));

        ParseImages(document, product, finalUrl);
        product.Description = Read(document, product, nameof(product.Description), "#productDescription", "#dp_productDescription_container_div");
        ParseAplusDescription(document, product, finalUrl);

        product.Availability = Read(document, product, nameof(product.Availability), "#availability .primary-availability-message", "#availability span", "#outOfStock .a-color-price");
        product.Seller = Read(document, product, nameof(product.Seller), "#merchantInfoFeature_feature_div #sellerProfileTriggerId", "#sellerProfileTriggerId", "#tabular-buybox [tabular-attribute-name='Sold by'] .tabular-buybox-text");
        var sellerLink = document.QuerySelector("#merchantInfoFeature_feature_div #sellerProfileTriggerId, #sellerProfileTriggerId");
        product.SellerUrl = AbsoluteUrl(finalUrl, sellerLink?.GetAttribute("href"));
        if (product.SellerUrl is not null) Evidence(product, nameof(product.SellerUrl), "#sellerProfileTriggerId[href]", sellerLink!.GetAttribute("href")!);
        product.ShipsFrom = Read(document, product, nameof(product.ShipsFrom), "#fulfillerInfoFeature_feature_div .offer-display-feature-text-message", "#tabular-buybox [tabular-attribute-name='Ships from'] .tabular-buybox-text");
        ParseDelivery(document, product);
        AddMissingWarnings(product);
        return product;
    }

    private static void ParseIdentity(IDocument document, AmazonProduct product, Uri finalUrl)
    {
        var asinInput = document.QuerySelector("input#ASIN");
        product.SelectedAsin = ValidAsin(asinInput?.GetAttribute("value"));
        if (product.SelectedAsin is not null) Evidence(product, nameof(product.SelectedAsin), "input#ASIN[value]", asinInput!.GetAttribute("value")!);
        else
        {
            var asinElement = document.QuerySelector("#averageCustomerReviews[data-asin]");
            product.SelectedAsin = ValidAsin(asinElement?.GetAttribute("data-asin"));
            if (product.SelectedAsin is not null) Evidence(product, nameof(product.SelectedAsin), "#averageCustomerReviews[data-asin]", asinElement!.GetAttribute("data-asin")!);
        }
        // Limit embedded-state searches to the product's twister script, never unrelated recommendation ASINs.
        var twister = document.QuerySelectorAll("script").FirstOrDefault(x => x.TextContent.Contains("twister-js-init-dpx-data", StringComparison.Ordinal));
        if (twister is not null)
        {
            var parent = Regex.Match(twister.TextContent, "\"parentAsin\"\\s*:\\s*\"([A-Z0-9]{10})\"");
            if (parent.Success) { product.ParentAsin = parent.Groups[1].Value; Evidence(product, nameof(product.ParentAsin), "script: twister-js-init-dpx-data.parentAsin", parent.Value); }
            if (product.SelectedAsin is null)
            {
                var selected = Regex.Match(twister.TextContent, "\"currentAsin\"\\s*:\\s*\"([A-Z0-9]{10})\"");
                if (selected.Success) { product.SelectedAsin = selected.Groups[1].Value; Evidence(product, nameof(product.SelectedAsin), "script: twister-js-init-dpx-data.currentAsin", selected.Value); }
            }
        }
        var canonical = document.QuerySelector("link[rel='canonical']");
        product.CanonicalUrl = AbsoluteUrl(finalUrl, canonical?.GetAttribute("href"));
        if (product.CanonicalUrl is not null) Evidence(product, nameof(product.CanonicalUrl), "link[rel='canonical'][href]", canonical!.GetAttribute("href")!);
        else if (product.SelectedAsin is not null)
        {
            product.CanonicalUrl = new Uri(finalUrl, "/dp/" + product.SelectedAsin).AbsoluteUri;
            Evidence(product, nameof(product.CanonicalUrl), "derived from final host and SelectedAsin", product.CanonicalUrl);
        }
        if (product.RequestedAsin is not null) Evidence(product, nameof(product.RequestedAsin), "requested URL /dp/ or /gp/product/ path", product.RequestedAsin);
        if (product.RequestedAsin is not null && product.SelectedAsin is not null && product.RequestedAsin != product.SelectedAsin)
            product.Warnings.Add($"Requested ASIN {product.RequestedAsin} differs from the selected page ASIN {product.SelectedAsin}.");
    }

    private static AmazonPrice? ReadPrice(IDocument document, AmazonProduct product, string field, params string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var element = document.QuerySelector(selector);
            var raw = Text(element);
            if (raw is null || !Regex.IsMatch(raw, @"\d")) continue;
            Evidence(product, field, selector, element!.TextContent);
            return ParsePriceText(raw, product, field);
        }
        return null;
    }

    private static AmazonPrice ParsePriceText(string raw, AmazonProduct product, string field)
    {
        var price = new AmazonPrice { DisplayText = raw };
        // ISO code wins. Symbols such as $ or ¥ alone are intentionally ambiguous.
        var iso = Regex.Match(raw, @"(?<![A-Z])(USD|KRW|EUR|GBP|CAD|AUD|JPY|CNY|INR|MXN|BRL|SGD|AED)(?![A-Z])", RegexOptions.IgnoreCase);
        price.Currency = iso.Success ? iso.Value.ToUpperInvariant() : raw.Contains('₩') ? "KRW" : raw.Contains('€') ? "EUR" : raw.Contains('£') ? "GBP" : null;
        var number = Regex.Match(raw, @"\d[\d, .\u00a0]*\d|\d");
        var normalized = number.Value.Replace(" ", "").Replace("\u00a0", "");
        // Amazon US display uses commas for grouping. Do not apply that rule to unknown/localized formats.
        if (Regex.IsMatch(normalized, @"^\d{1,3}(?:,\d{3})+(?:\.\d+)?$")) normalized = normalized.Replace(",", "");
        if (Regex.IsMatch(normalized, @"^\d+(?:\.\d+)?$") && decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)) price.Amount = amount;
        else product.Warnings.Add($"{field} was observed but its number format could not be parsed safely: {raw}");
        return price;
    }

    private static void ParseOfferCharges(IDocument document, AmazonProduct product)
    {
        if (ValidAsin(product.SelectedAsin) is not { } selectedAsin) return;
        // The page repeats AmazonGlobal and popover IDs. Match the selected ASIN inside the qualified buy box.
        var selector = $"#desktop_qualifiedBuyBox #amazonGlobal_feature_div[data-csa-c-asin='{selectedAsin}'] #a-popover-NEW_0 table.a-lineitem";
        var table = document.QuerySelector(selector);
        if (table is null) return;
        var acceptedLabels = new HashSet<string>(StringComparer.Ordinal)
        {
            "Price", "AmazonGlobal Shipping", "Estimated Import Charges", "Total"
        };
        var rows = table.QuerySelectorAll("tr");
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var cells = row.QuerySelectorAll("td");
            if (cells.Length < 2 || Text(cells[0]) is not { } label || !acceptedLabels.Contains(label)) continue;
            if (Text(cells[^1]) is not { } rawPrice || !Regex.IsMatch(rawPrice, @"\d")) continue;
            if (product.OfferCharges.ContainsKey(label))
            {
                product.Warnings.Add($"Duplicate offer-charge label {label}; kept the first observed row.");
                continue;
            }
            product.OfferCharges[label] = ParsePriceText(rawPrice, product, "OfferCharges." + label);
            Evidence(product, "OfferCharges." + label, selector + $" tr:nth-of-type({index + 1}); first td label, last td value", Text(row) ?? "");
        }
        if (product.OfferCharges.Count > 0)
        {
            Evidence(product, nameof(product.OfferCharges), selector, string.Join(Environment.NewLine, product.OfferCharges.Select(x => x.Key + ": " + x.Value.DisplayText)));
            product.Warnings.Add("Displayed shipping/import charges and total depend on this delivery context and are not a checkout confirmation. The displayed total is preserved exactly rather than recomputed from rounded line items.");
        }
    }

    private static void ParseVariants(IDocument document, AmazonProduct product, Uri finalUrl)
    {
        var state = ReadState(document, product, "desktop-twister-sort-filter-data");
        if (state is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty("sortedDimValuesForAllDims", out var dimensions) && dimensions.ValueKind == JsonValueKind.Object)
        {
            foreach (var dimension in dimensions.EnumerateObject())
            {
                if (dimension.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var option in dimension.Value.EnumerateArray())
                {
                    if (option.ValueKind != JsonValueKind.Object) continue;
                    var label = JsonText(option, "dimensionValueDisplayText");
                    if (label is null) continue;
                    var optionState = JsonText(option, "dimensionValueState");
                    var selected = optionState == "SELECTED";
                    string? imageUrl = null;
                    if (option.TryGetProperty("imageAttribute", out var image)) imageUrl = AbsoluteUrl(finalUrl, JsonText(image, "url"));
                    product.Variants.Add(new AmazonVariantOption
                    {
                        Dimension = dimension.Name,
                        Label = label,
                        Asin = ValidAsin(JsonText(option, "defaultAsin")),
                        IsSelected = selected,
                        State = optionState,
                        Url = AbsoluteUrl(finalUrl, JsonText(option, "pageLoadURL")),
                        ImageUrl = imageUrl
                    });
                    if (selected)
                    {
                        if (dimension.Name == "color_name") product.SelectedColor = label;
                        if (dimension.Name == "size_name") product.SelectedSize = label;
                        var field = dimension.Name == "color_name" ? nameof(product.SelectedColor) : dimension.Name == "size_name" ? nameof(product.SelectedSize) : "SelectedVariant:" + dimension.Name;
                        Evidence(product, field, $"a-state:desktop-twister-sort-filter-data.sortedDimValuesForAllDims.{dimension.Name}[SELECTED]", option.GetRawText());
                    }
                }
            }
            Evidence(product, nameof(product.Variants), "a-state:desktop-twister-sort-filter-data.sortedDimValuesForAllDims", dimensions.GetRawText());
        }

        // Older DOMs and partial responses may omit a-state. Capture only observable labels/links.
        if (product.Variants.Count == 0)
        {
            foreach (var dimension in new[] { "color_name", "size_name" })
            {
                foreach (var item in document.QuerySelectorAll($"#inline-twister-row-{dimension} li[data-asin], #variation_{dimension} li"))
                {
                    var label = Text(item.QuerySelector(".swatch-title-text")) ?? Clean(item.QuerySelector("img")?.GetAttribute("alt"))
                        ?? Clean(item.GetAttribute("title")) ?? Text(item.QuerySelector(".a-button-text"));
                    if (label is null) continue;
                    label = Regex.Replace(label, "^Click to select\\s+", "", RegexOptions.IgnoreCase);
                    var selected = item.GetAttribute("data-initiallySelected") == "true" || item.QuerySelector(".a-button-selected, [aria-checked='true']") is not null;
                    product.Variants.Add(new AmazonVariantOption
                    {
                        Dimension = dimension, Label = label,
                        Asin = ValidAsin(item.GetAttribute("data-asin")) ?? ExtractAsin(item.QuerySelector("a[href]")?.GetAttribute("href")),
                        IsSelected = selected,
                        State = item.GetAttribute("data-initiallyUnavailable") == "true" ? "UNAVAILABLE" : selected ? "SELECTED" : "OBSERVED",
                        Url = AbsoluteUrl(finalUrl, item.QuerySelector("a[href]")?.GetAttribute("href")),
                        ImageUrl = AbsoluteUrl(finalUrl, item.QuerySelector("img")?.GetAttribute("src"))
                    });
                    if (selected && dimension == "color_name") product.SelectedColor = label;
                    if (selected && dimension == "size_name") product.SelectedSize = label;
                }
            }
            if (product.Variants.Count > 0) Evidence(product, nameof(product.Variants), "#inline-twister-row-* li[data-asin], #variation_* li", JsonSerializer.Serialize(product.Variants));
        }
        if (product.Variants.Count > 0)
            product.Warnings.Add("Variants are the dimension options exposed in this response. Their ASINs are observed default/link targets; prices, stock, and every color/size combination have not been independently fetched.");
    }

    private static void ParseFeaturesAndDetails(IDocument document, AmazonProduct product)
    {
        foreach (var selector in new[] { "#productFactsDesktopExpander ul > li > .a-list-item", "#feature-bullets li > .a-list-item", "#pqv-feature-bullets li > .a-list-item" })
        {
            var features = document.QuerySelectorAll(selector).Select(Text).OfType<string>().Where(x => x != "Make sure this fits by entering your model number.").Distinct().ToList();
            if (features.Count == 0) continue;
            product.Features = features;
            Evidence(product, nameof(product.Features), selector, string.Join(Environment.NewLine, features));
            break;
        }
        foreach (var row in document.QuerySelectorAll("#productFactsDesktopExpander .product-facts-detail"))
            AddDetail(product, Text(row.QuerySelector(".a-col-left")), Text(row.QuerySelector(".a-col-right")), "#productFactsDesktopExpander .product-facts-detail", row);
        foreach (var row in document.QuerySelectorAll("#productDetails_techSpec_section_1 tr, #productDetails_detailBullets_sections1 tr"))
            AddDetail(product, Text(row.QuerySelector("th")), Text(row.QuerySelector("td")), "#productDetails_* tr", row);
        foreach (var item in document.QuerySelectorAll("#detailBullets_feature_div > ul > li"))
        {
            var label = item.QuerySelector(".a-text-bold");
            var key = Text(label)?.TrimEnd(':', ' ');
            var valueContainer = item.Clone(true) as IElement;
            valueContainer?.QuerySelector(".a-text-bold")?.Remove();
            AddDetail(product, key, Text(valueContainer), "#detailBullets_feature_div > ul > li", item);
        }
        if (product.ParentAsin is null && product.ProductDetails.TryGetValue("ASIN", out var detailAsin) && ValidAsin(detailAsin) is { } parsedAsin && parsedAsin != product.SelectedAsin)
        {
            // Product details can show the parent. A difference alone does not prove that relationship.
            product.Warnings.Add($"Product-details ASIN {parsedAsin} differs from selected ASIN {product.SelectedAsin}; no explicit parent relationship was found.");
        }
        if (product.ParentAsin is not null && product.ProductDetails.TryGetValue("ASIN", out var details) && ValidAsin(details) == product.ParentAsin)
            product.Warnings.Add("The product-details ASIN is the parent ASIN. SelectedAsin remains the separately observed purchase variant.");
    }

    private static void AddDetail(AmazonProduct product, string? key, string? value, string selector, IElement row)
    {
        key = key?.TrimEnd(':', ' ');
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;
        if (product.ProductDetails.TryGetValue(key, out var existing) && existing != value)
        {
            product.Warnings.Add($"Conflicting product-detail values for {key}; kept the first value.");
            return;
        }
        product.ProductDetails[key] = value;
        Evidence(product, "ProductDetails." + key, selector + " [label=" + key + "]", Text(row) ?? "");
    }

    private static void ParseImages(IDocument document, AmazonProduct product, Uri finalUrl)
    {
        var main = document.QuerySelector("#landingImage, #imgBlkFront");
        product.MainImageUrl = ImageUrl(main, finalUrl);
        if (product.MainImageUrl is not null) Evidence(product, nameof(product.MainImageUrl), "#landingImage[data-old-hires|data-a-dynamic-image|src]", product.MainImageUrl);
        foreach (var element in document.QuerySelectorAll("#imageBlock .media-block-image-tag, #imageBlock #landingImage, #imgBlkFront"))
            if (ImageUrl(element, finalUrl) is { } url && !product.ImageUrls.Contains(url)) product.ImageUrls.Add(url);
        if (product.ImageUrls.Count == 0 && product.MainImageUrl is not null) product.ImageUrls.Add(product.MainImageUrl);
        if (product.ImageUrls.Count > 0) Evidence(product, nameof(product.ImageUrls), "#imageBlock .media-block-image-tag[data-old-hires|data-a-dynamic-image|src]", string.Join(Environment.NewLine, product.ImageUrls));
    }

    private static string? ImageUrl(IElement? element, Uri finalUrl)
    {
        if (element is null) return null;
        if (AbsoluteUrl(finalUrl, element.GetAttribute("data-old-hires")) is { } hires) return hires;
        if (element.GetAttribute("data-a-dynamic-image") is { } dynamicImage)
        {
            try
            {
                using var json = JsonDocument.Parse(dynamicImage);
                if (json.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var best = json.RootElement.EnumerateObject().Select(x => new
                    {
                        Url = x.Name,
                        Area = x.Value.ValueKind == JsonValueKind.Array && x.Value.GetArrayLength() >= 2
                            && x.Value[0].ValueKind == JsonValueKind.Number && x.Value[1].ValueKind == JsonValueKind.Number
                            && x.Value[0].TryGetInt32(out var height) && x.Value[1].TryGetInt32(out var width) ? (long)height * width : 0
                    }).OrderByDescending(x => x.Area).FirstOrDefault();
                    if (best is not null && AbsoluteUrl(finalUrl, best.Url) is { } url) return url;
                }
            }
            catch (JsonException) { /* src remains a usable fallback. */ }
        }
        return AbsoluteUrl(finalUrl, element.GetAttribute("src"));
    }

    private static void ParseAplusDescription(IDocument document, AmazonProduct product, Uri finalUrl)
    {
        const string moduleSelector = "#aplus_feature_div .aplus-module";
        // A+ may append a shopping comparison table or a brand carousel below the actual description.
        // Filter complete modules before collecting either text or images from them.
        var modules = document.QuerySelectorAll(moduleSelector).Where(x => IsProductDescriptionModule(x, product)).ToList();
        if (product.Description is null)
        {
            var paragraphs = modules.SelectMany(x => x.QuerySelectorAll("h1, h2, h3, h4, p"))
                .Where(x => x.Closest("noscript") is null)
                .Select(Text).OfType<string>().Distinct().ToList();
            if (paragraphs.Count > 0)
            {
                product.Description = string.Join(Environment.NewLine, paragraphs);
                Evidence(product, nameof(product.Description), moduleSelector + " h1/h2/h3/h4/p; primary modules only, comparison/brand/recommendation modules excluded", product.Description);
                product.Warnings.Add("Description is extracted from the product A+ text; image-only content is not transcribed.");
            }
        }

        foreach (var image in modules.SelectMany(x => x.QuerySelectorAll("img")))
        {
            // Lazy-loaded descriptions use data-src while src still points to a grey placeholder.
            // A noscript image repeats the same asset and is not an additional description image.
            if (image.Closest("noscript") is not null) continue;
            var attribute = Clean(image.GetAttribute("data-src")) is not null ? "data-src" : "src";
            var rawUrl = image.GetAttribute(attribute);
            var url = AbsoluteUrl(finalUrl, rawUrl);
            if (url is null || Regex.IsMatch(url, @"grey-pixel|transparent-pixel|/x-locale/common/|(?:^|/)pixel\.gif", RegexOptions.IgnoreCase)) continue;
            if (product.DescriptionImageUrls.Contains(url)) continue;
            var index = product.DescriptionImageUrls.Count;
            product.DescriptionImageUrls.Add(url);
            Evidence(product, $"DescriptionImageUrls[{index}]", moduleSelector + $" img[{attribute}]; primary modules only, noscript excluded", rawUrl!);
        }
        if (product.DescriptionImageUrls.Count > 0)
            Evidence(product, nameof(product.DescriptionImageUrls), moduleSelector + " img[data-src|src]; comparison tables, brand story, recommendation modules and noscript excluded", string.Join(Environment.NewLine, product.DescriptionImageUrls));
    }

    private static bool IsProductDescriptionModule(IElement module, AmazonProduct product)
    {
        var identity = module.ClassName + " " + module.GetAttribute("cel_widget_id");
        if (module.ClassList.Contains("module-5") || Regex.IsMatch(identity, @"comparison|compare|brand[-_]?story|recommend|shoppable", RegexOptions.IgnoreCase)) return false;
        if (module.QuerySelector(".apm-tablemodule, .apm-tablemodule-table, .aplus-chart, [class*='comparison'], [class*='brand-story'], [class*='recommend']") is not null) return false;
        if (module.Closest("#aplusBrandStory_feature_div, [class*='brand-story'], [class*='recommend']") is not null) return false;
        // Explicit links to other ASINs identify a shopping module even when its CSS name changes.
        return !module.QuerySelectorAll("a[href]").Any(link => ExtractAsin(link.GetAttribute("href")) is { } asin
            && asin != product.SelectedAsin && asin != product.ParentAsin);
    }

    private static void ParseDelivery(IDocument document, AmazonProduct product)
    {
        product.DeliveryLocation = Read(document, product, nameof(product.DeliveryLocation), "#glow-ingress-line2");
        foreach (var element in document.QuerySelectorAll("#deliveryBlockMessage [data-csa-c-delivery-time]"))
        {
            if (Text(element) is not { } text) continue;
            product.DeliveryMessages.Add(new AmazonDeliveryMessage
            {
                Text = text,
                EstimatedDateText = Clean(element.GetAttribute("data-csa-c-delivery-time")),
                Destination = Clean(element.GetAttribute("data-csa-c-delivery-destination")),
                PriceText = Clean(element.GetAttribute("data-csa-c-delivery-price")),
                Condition = Clean(element.GetAttribute("data-csa-c-delivery-condition")),
                CutoffText = Clean(element.GetAttribute("data-csa-c-delivery-cutoff"))
            });
        }
        if (product.DeliveryMessages.Count == 0 && Read(document, product, nameof(product.DeliveryMessages), "#mir-layout-DELIVERY_BLOCK", "#deliveryBlockMessage") is { } raw)
            product.DeliveryMessages.Add(new AmazonDeliveryMessage { Text = raw });
        else if (product.DeliveryMessages.Count > 0)
            Evidence(product, nameof(product.DeliveryMessages), "#deliveryBlockMessage [data-csa-c-delivery-time]", string.Join(Environment.NewLine, product.DeliveryMessages.Select(x => x.Text)));
    }

    private static JsonElement? ReadState(IDocument document, AmazonProduct product, string key)
    {
        foreach (var script in document.QuerySelectorAll("script[type='a-state'][data-a-state]"))
        {
            try
            {
                using var metadata = JsonDocument.Parse(script.GetAttribute("data-a-state")!);
                if (JsonText(metadata.RootElement, "key") != key) continue;
                using var content = JsonDocument.Parse(script.TextContent);
                return content.RootElement.Clone();
            }
            catch (JsonException)
            {
                if (script.GetAttribute("data-a-state")?.Contains(key, StringComparison.Ordinal) == true)
                    product.Warnings.Add($"Embedded state {key} was invalid JSON; DOM fallback was used.");
            }
        }
        return null;
    }

    private static string? JsonText(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? Clean(value.GetString()) : null;

    private static string? Read(IDocument document, AmazonProduct product, string field, params string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var element = document.QuerySelector(selector);
            if (Text(element) is not { } value) continue;
            Evidence(product, field, selector, element!.TextContent);
            return value;
        }
        return null;
    }

    private static string? ReadAttributeOrText(IDocument document, AmazonProduct product, string field, string selector, string attribute)
    {
        var element = document.QuerySelector(selector);
        var raw = Clean(element?.GetAttribute(attribute));
        if (raw is not null) { Evidence(product, field, selector + "[" + attribute + "]", element!.GetAttribute(attribute)!); return raw; }
        if (Text(element) is not { } value) return null;
        Evidence(product, field, selector, element!.TextContent);
        return value;
    }

    private static void Evidence(AmazonProduct product, string field, string source, string raw) => product.Evidence[field] = new AmazonFieldEvidence { Source = source, RawText = raw };
    private static string? ExtractAsin(string? url) { var match = AsinInUrl.Match(url ?? ""); return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null; }
    private static string? ValidAsin(string? value) => value is not null && Regex.IsMatch(value.Trim(), @"^[A-Z0-9]{10}$", RegexOptions.IgnoreCase) ? value.Trim().ToUpperInvariant() : null;
    private static string? AbsoluteUrl(Uri baseUri, string? value) => !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(baseUri, value, out var uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
    private static decimal? NumberAtStart(string? value) => decimal.TryParse(Regex.Match(value ?? "", @"^\d+(?:\.\d+)?").Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null;
    private static string? Clean(string? value)
    {
        if (value is null) return null;
        value = Whitespace.Replace(value.Replace("\u200e", "").Replace("\u200f", "").Replace("\u200b", ""), " ").Trim();
        return value.Length == 0 ? null : value;
    }

    private static string? Text(IElement? element)
    {
        if (element is null) return null;
        var clone = (IElement)element.Clone(true);
        foreach (var excluded in clone.QuerySelectorAll("script, style, noscript")) excluded.Remove();
        return Clean(clone.TextContent);
    }

    private static void AddMissingWarnings(AmazonProduct product)
    {
        if (product.SelectedAsin is null) product.Warnings.Add("Selected ASIN was not present in the product DOM/state.");
        if (product.Brand is null) product.Warnings.Add("Brand was not present in #bylineInfo.");
        if (product.CurrentPrice is null) product.Warnings.Add("No displayed price was found in the main product offer. Prices elsewhere in the page were ignored.");
        if (product.ListPrice is null) product.Warnings.Add("List price was not displayed in the main product offer.");
        if (product.Rating is null) product.Warnings.Add("Product rating could not be extracted.");
        if (product.ReviewCount is null) product.Warnings.Add("Product review/rating count could not be parsed as an integer.");
        if (product.Features.Count == 0) product.Warnings.Add("No product feature bullets were present in the response.");
        if (product.MainImageUrl is null) product.Warnings.Add("A main product image was not present.");
        if (product.Description is null) product.Warnings.Add("No product description text was present; description images are not OCR processed.");
        if (product.Availability is null) product.Warnings.Add("Availability was not displayed in the main product offer.");
        if (product.Seller is null) product.Warnings.Add("Seller was not displayed in the main product offer.");
        if (product.ShipsFrom is null) product.Warnings.Add("Ships-from value was not displayed in the main product offer.");
        if (product.DeliveryLocation is null) product.Warnings.Add("Delivery location was not present; offer context could not be identified.");
    }
}

public sealed class AmazonProductParsingException(string message, bool isChallenge) : InvalidOperationException(message)
{
    public bool IsChallenge { get; } = isChallenge;
}
