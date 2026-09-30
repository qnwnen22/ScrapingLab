using ScrapingLab.Models;

namespace ScrapingLab.Collects;

public interface ICollect
{
    Product GetProduct(string url, string? html = null);
}
