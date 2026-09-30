namespace ScrapingLab;

internal sealed record CommandLineOptions(Uri? Url, string? HtmlPath, string OutputRoot, bool Help, bool VariantPrices)
{
    public static CommandLineOptions Parse(string[] args)
    {
        string? urlText = null;
        string? htmlPath = null;
        var outputRoot = "artifacts";
        var help = false;
        var variantPrices = false;

        for (var index = 0; index < args.Length; index++)
        {
            string NextValue()
            {
                if (++index >= args.Length)
                {
                    throw new ArgumentException("옵션 값이 필요합니다.");
                }
                return args[index];
            }

            switch (args[index])
            {
                case "--url":
                    if (urlText is not null) throw new ArgumentException("URL은 한 번만 지정합니다.");
                    urlText = NextValue();
                    break;
                case "--html":
                    htmlPath = NextValue();
                    break;
                case "--output":
                    outputRoot = NextValue();
                    break;
                case "--help":
                case "-h":
                    help = true;
                    break;
                case "--variant-prices":
                    variantPrices = true;
                    break;
                default:
                    if (args[index].StartsWith('-') || urlText is not null)
                        throw new ArgumentException($"알 수 없는 인자: {args[index]}");
                    urlText = args[index];
                    break;
            }
        }

        Uri? url = null;
        if (urlText is not null && (!Uri.TryCreate(urlText, UriKind.Absolute, out url) ||
            (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException("http 또는 https URL이 필요합니다.");
        }
        if (htmlPath is not null && url is null)
        {
            throw new ArgumentException("저장된 HTML을 파싱하려면 --url도 지정합니다.");
        }
        if (variantPrices && (url is null || htmlPath is not null))
        {
            throw new ArgumentException("--variant-prices는 실제 URL 수집에서 사용합니다. HTML 재파싱과는 함께 사용할 수 없습니다.");
        }
        return new CommandLineOptions(url, htmlPath, outputRoot, help, variantPrices);
    }
}
