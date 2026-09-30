using System.Text;
using Newtonsoft.Json;
using ScrapingLab.Collects;

namespace ScrapingLab;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            var url = args.FirstOrDefault() ?? Console.ReadLine() ?? string.Empty;
            var collect = CollectFactory.Create(url);
            var product = collect.GetProduct(url);
            Console.WriteLine(JsonConvert.SerializeObject(product, Formatting.Indented));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
