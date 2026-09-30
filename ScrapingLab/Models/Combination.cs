namespace ScrapingLab.Models;

public class Combination
{
    /// <summary>
    /// 옵션 조합 고유 식별자
    /// </summary>
    public string? Code { get; set; }
    /// <summary>
    /// 옵션명
    /// <example>
    /// 예: "색상", "크기"
    /// </example>
    /// </summary>
    public string? Name { get; set; }
    public List<OptionValue>? OptionValues { get; set; } = null;
}
