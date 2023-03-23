using System.Net;
using System.Text;
using System.Text.RegularExpressions;

internal abstract class WebNode
{
    public Uri address;
    
    public static WebNode InitRoot(Uri address)
    {
        return new PageNode()
        {
            address = address,
        };
    }

    public abstract Task<IEnumerable<WebNode>> Parse(WebNode root, HttpClient client, IEnumerable<WebNode> contained, int limit = -1);
}

internal class ResourceNode : WebNode
{
    public override Task<IEnumerable<WebNode>> Parse(WebNode root, HttpClient client, IEnumerable<WebNode> contained, int limit = -1)
    {
        return Task.FromResult(Enumerable.Empty<WebNode>());
    }

    public ResourceNode(Uri address)
    {
        this.address = address;
    }
}

internal class PageNode : WebNode
{
    private List<WebNode> _children = new();
    public override async Task<IEnumerable<WebNode>> Parse(WebNode root, HttpClient client, IEnumerable<WebNode> contained, int limit = -1)
    {
        client.DefaultRequestHeaders.Add("User-Agent", "Chrome/79");
        try
        {
            using (var response = await client.GetAsync(address))
            {
                if (!response
                        .Content
                        .Headers
                        .Contains("Content-Type") || 
                    (!response
                        .Content
                        .Headers
                        .GetValues("Content-Type")
                        .FirstOrDefault()?
                        .Contains("text") ?? true)
                    )
                    return Enumerable.Empty<WebNode>();

                using (var content = response.Content)
                {
                    var re = new Regex("<a(.*?)href=\"(?'href'.*?)\"|src=\"(?'src'.*?)\"");

                    var parsed = response.Content.Headers
                        .GetValues("Content-Type")
                        .First()
                        .Split("charset=");

                    String text;
                    if (parsed.Length == 2)
                    {
                        var encodeName = parsed.Last();
                        var bytes = await content.ReadAsByteArrayAsync();
                        var encoding = Encoding.GetEncoding(encodeName[0]);
                        text = encoding.GetString(bytes, 0, bytes.Length);
                    }
                    else
                    {
                        text = await content.ReadAsStringAsync();
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound)
                        return Enumerable.Empty<WebNode>();

                    int childrenCount = 0;
                    foreach (Match match in re.Matches(text))
                    {
                        if (limit > 0 && childrenCount >= limit)
                            break;
                        
                        var href = match.Groups["href"];
                        if (href.Success && Uri.IsWellFormedUriString(href.Value, UriKind.Absolute))
                        {
                            var childAddress = new Uri(href.Value);
                            if (contained.Any(node => node.address.ToString() == href.Value))
                                continue;
                            
                            if (Uri.Compare(address, childAddress, UriComponents.Host, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
                                continue;

                            var newChild = new PageNode
                            {
                                address = childAddress,
                                _children = new List<WebNode>(),
                            };
                            _children.Add(newChild);
                        }

                        var src = match.Groups["src"];
                        if (src.Success && Uri.IsWellFormedUriString(src.Value, UriKind.Absolute))
                        {
                            _children.Add(new ResourceNode(new Uri(src.Value)));
                        }
                        
                        childrenCount++;
                    }
                }
            }
        }
        catch
        {
            // ignored
        }

        return _children;
    }
}

class WebTree
{
    private Uri _address;
    private WebNode _root;

    public WebTree(Uri address)
    {
        _address = address;
        _root = WebNode.InitRoot(_address);
    }

    public void ParseLayer(int maxDeep = 5)
    {
        var layer = new List<WebNode> { _root };
        var deep = 0;
        var contained = new List<WebNode>(layer);

        using var client = new HttpClient();
        while (deep < maxDeep)
        {
            var tasks = layer.Select(x => x.Parse(_root, client, contained, 100));

            var result = Task.WhenAll(tasks).Result;

            layer = result.SelectMany(nodes => nodes).ToList();
            contained.AddRange(layer);
            if (!layer.Any())
                break;
            
            deep++;
        }
    }
}

class Crawler
{
    private readonly WebTree _tree;
    public Crawler(Uri address)
    {
        _tree = new WebTree(address);
    }

    public void DigWeb(int deep)
    {
        _tree.ParseLayer(deep);
    }
}

internal static class Program
{
    private const string BaseUri = "https://www.pravda.ru/";
    static void Main(string[] args)
    {
        var crawler = new Crawler(new Uri(BaseUri));
        crawler.DigWeb(3);
        Console.ReadKey();
    }
}