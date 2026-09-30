namespace ScrapingLab.Models;

public class Independency
{
    /// <summary>
    /// 상품 옵션 독립 조합 고유 식별자
    /// </summary>
    public string? Codes { get; set; }
    /// <summary>
    /// 상품 옵션 독립 조합 이름
    /// <example>빨강|S, 파랑|X</example>
    /// </summary>
    public string? Names { get; set; }
    /// <summary>
    /// 상품 가격
    /// </summary>
    public Price? Price { get; set; }
}
