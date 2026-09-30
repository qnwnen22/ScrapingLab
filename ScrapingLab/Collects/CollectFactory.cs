using ScrapingLab.Collects.Amazon;

namespace ScrapingLab.Collects;

public static class CollectFactory
{
    public static ICollect Create(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("수집할 상품의 http 또는 https URL을 입력하세요.", nameof(url));

        if (AmazonUrl.GetAsin(uri) is not null) return new AmazonCollect();

        throw new NotSupportedException("현재는 Amazon US 상품 URL을 지원합니다.");
    }
}
