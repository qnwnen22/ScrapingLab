namespace ScrapingLab.Models;

public class OptionValue
{
    /// <summary>
    /// 옵션값 고유 식별자
    /// </summary>
    public string? Code { get; set; }
    /// <summary>
    /// 옵션 값
    /// <example>
    /// 예: "빨간", "파랑", "초록", "S", "M", "L"
    /// </example>
    /// </summary>
    public string? Name { get; set; }
    /// <summary>
    /// 상품 이미지 URL
    /// </summary>
    public string? ImageUrl { get; set; }
}
