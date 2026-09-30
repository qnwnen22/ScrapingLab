namespace ScrapingLab.Models;

public class Product
{
    /// <summary>
    /// 상품 고유 식별자
    /// </summary>
    public string? Code { get; set; }
    /// <summary>
    /// 상품명
    /// </summary>
    public string? Title { get; set; }
    /// <summary>
    /// 브랜드 명
    /// </summary>
    public string? Brand { get; set; }
    /// <summary>
    /// 상품 URL
    /// </summary>
    public string ItemUrl { get; set; } = "";
    /// <summary>
    /// 상품 이미지 URL
    /// </summary>
    public List<string> ItemImages { get; set; } = new List<string>();
    /// <summary>
    /// 상품 설명
    /// </summary>
    public List<string>? Description { get; set; } = null;
    /// <summary>
    /// 상품 상세페이지 HTML
    /// </summary>
    public string? DetailHtml { get; set; } = null;
    /// <summary>
    /// 상품 가격
    /// </summary>
    public Price? Price { get; set; }
    public Option? Option { get; set; }
}
