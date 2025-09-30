using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Parliment.Extractor.Models;

namespace Parliment.Extractor.Services;

public class MemberProfileExtractor
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly bool _useCache;
    private const string BaseUrl = "https://majlis.gov.mv";

    public MemberProfileExtractor(HttpClient? httpClient = null, bool useCache = false)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        
        _useCache = useCache;
        _cacheDirectory = Path.Combine(Directory.GetCurrentDirectory(), "cache");
        
        if (_useCache && !Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    public async Task<(MemberProfile?, MemberActivityStats)> ExtractProfileAsync(string profileUrl)
    {
        try
        {
            Console.WriteLine($"Fetching profile: {profileUrl}");
            
            var cacheFileName = GetCacheFileNameFromUrl(profileUrl);
            var html = await GetHtmlAsync(profileUrl, cacheFileName);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Remove script tags and other unwanted elements  
            // CleanDocument(doc); // Temporarily disable to see if this affects extraction

            var profile = new MemberProfile();

            ExtractBasicInfo(doc, profile);
            ExtractContactInfo(doc, profile);
            ExtractActivities(doc, profile);
            ExtractDocuments(doc, profile);
            
            var stats = ExtractActivityStats(doc);

            return (profile, stats);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting profile from {profileUrl}: {ex.Message}");
            return (null, new MemberActivityStats());
        }
    }

    private void CleanDocument(HtmlDocument doc)
    {
        // Remove script tags and their content
        var scriptNodes = doc.DocumentNode.SelectNodes("//script");
        if (scriptNodes != null)
        {
            foreach (var script in scriptNodes.ToList())
            {
                script.Remove();
            }
        }

        // Remove style tags and their content
        var styleNodes = doc.DocumentNode.SelectNodes("//style");
        if (styleNodes != null)
        {
            foreach (var style in styleNodes.ToList())
            {
                style.Remove();
            }
        }

        // Remove comments
        var commentNodes = doc.DocumentNode.SelectNodes("//comment()");
        if (commentNodes != null)
        {
            foreach (var comment in commentNodes.ToList())
            {
                comment.Remove();
            }
        }

        // Remove common noise elements
        var noiseSelectors = new[]
        {
            "//noscript",
            "//*[contains(@class, 'advertisement')]",
            "//*[contains(@class, 'ads')]",
            "//*[contains(@class, 'tracking')]",
            "//*[contains(@id, 'google')]",
            "//*[contains(@class, 'analytics')]"
        };

        foreach (var selector in noiseSelectors)
        {
            var nodes = doc.DocumentNode.SelectNodes(selector);
            if (nodes != null)
            {
                foreach (var node in nodes.ToList())
                {
                    node.Remove();
                }
            }
        }
    }

    private void ExtractBasicInfo(HtmlDocument doc, MemberProfile profile)
    {
        // Extract party color
        var colorNodes = doc.DocumentNode.SelectNodes("//*[@style]");
        if (colorNodes != null)
        {
            foreach (var node in colorNodes)
            {
                var style = node.GetAttributeValue("style", "");
                if (style.Contains("color:") && style.Contains("#"))
                {
                    var colorMatch = Regex.Match(style, @"#[0-9a-fA-F]{6}|#[0-9a-fA-F]{3}");
                    if (colorMatch.Success)
                    {
                        profile.PartyColor = colorMatch.Value;
                        break;
                    }
                }
            }
        }
    }

    private void ExtractContactInfo(HtmlDocument doc, MemberProfile profile)
    {
        // Email - look for majlis.gov.mv email pattern
        var emailNodes = doc.DocumentNode.SelectNodes("//a[contains(@href, 'mailto:') or contains(@href, 'majlis.gov.mv')] | //*[contains(text(), '@majlis.gov.mv')]");
        if (emailNodes != null)
        {
            foreach (var node in emailNodes)
            {
                var emailText = node.InnerText ?? node.GetAttributeValue("href", "");
                var emailMatch = Regex.Match(emailText, @"[\w\.-]+@majlis\.gov\.mv");
                if (emailMatch.Success)
                {
                    profile.Contact.Email = emailMatch.Value;
                    break;
                }
            }
        }

        // Facebook
        var fbNodes = doc.DocumentNode.SelectNodes("//a[contains(@href, 'facebook.com')]");
        if (fbNodes != null)
        {
            foreach (var node in fbNodes)
            {
                var href = node.GetAttributeValue("href", "");
                if (href.Contains("facebook.com"))
                {
                    profile.Contact.Facebook = href;
                    break;
                }
            }
        }

        // Twitter/X
        var twitterNodes = doc.DocumentNode.SelectNodes("//a[contains(@href, 'twitter.com') or contains(@href, 'x.com')]");
        if (twitterNodes != null)
        {
            foreach (var node in twitterNodes)
            {
                var href = node.GetAttributeValue("href", "");
                if (href.Contains("twitter.com") || href.Contains("x.com"))
                {
                    profile.Contact.Twitter = href;
                    break;
                }
            }
        }

        // Phone - look for phone number patterns
        var phoneText = FindTextMatching(doc, @"(\+960\s?)?[0-9]{3}[-\s]?[0-9]{4}");
        if (!string.IsNullOrEmpty(phoneText))
        {
            profile.Contact.Phone = CleanText(phoneText);
        }
    }

    private void ExtractActivities(HtmlDocument doc, MemberProfile profile)
    {
        // Look for activity section headers like the one mentioned in the model
        var sectionHeaders = doc.DocumentNode.SelectNodes("//h4[contains(@class, 'mb-3') and contains(@class, 'mt-5')] | //h3 | //h2");
        
        if (sectionHeaders != null)
        {
            foreach (var header in sectionHeaders)
            {
                var sectionText = CleanText(header.InnerText);
                
                if (sectionText.Length > 20 && IsValidContent(sectionText) && 
                    (sectionText.Contains("Motion") || sectionText.Contains("Bill") || 
                     sectionText.Contains("Resolution") || sectionText.Contains("Question") ||
                     sectionText.Contains("Committee")))
                {
                    // This looks like an activity section header
                    profile.Activity.SectionName = sectionText;
                    
                    // Look for activity cards following this header
                    ExtractActivityDetails(doc.DocumentNode, profile);
                    break; // Use the first valid section we find
                }
            }
        }
        
        // Fallback: if no section header found, extract activities directly
        if (string.IsNullOrEmpty(profile.Activity.SectionName))
        {
            profile.Activity.SectionName = "Parliamentary Activities";
            ExtractActivityDetails(doc.DocumentNode, profile);
        }
    }

    private void ExtractActivityDetails(HtmlNode startNode, MemberProfile profile)
    {
        var activityLinks = startNode.SelectNodes(".//a[contains(@class, 'card-list-link')]");
        
        if (activityLinks != null)
        {
            foreach (var link in activityLinks)
            {
                var activityDetail = ExtractSingleActivity(link);
                if (activityDetail != null)
                {
                    profile.Activity.Details.Add(activityDetail);
                }
            }
        }
    }

    private MemberActivityDetail? ExtractSingleActivity(HtmlNode cardLinkNode)
    {
        try
        {
            // The cardLinkNode is the <a class="card-list-link"> element
            var url = GetNodeUrl(cardLinkNode);
            
            // Look for the corner container (activity type) inside the card
            var cornerNode = cardLinkNode.SelectSingleNode(".//div[contains(@class, 'card-corner-container')]");
            var activityName = cornerNode != null ? CleanText(cornerNode.InnerText) : "";
            
            // Look for the main title (description) inside the card
            var titleNode = cardLinkNode.SelectSingleNode(".//h6[contains(@class, 'card-main-title')]");
            var description = titleNode != null ? CleanText(titleNode.InnerText) : "";
            
            // If we have both activity name and description, create the detail
            if (!string.IsNullOrEmpty(activityName) && !string.IsNullOrEmpty(description))
            {
                return new MemberActivityDetail
                {
                    ActivityName = activityName,
                    ActivityDescr = description,
                    Url = url
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting activity detail: {ex.Message}");
        }
        
        return null;
    }

    private string DetermineActivityType(string text)
    {
        if (text.Contains("Emergency Motion", StringComparison.OrdinalIgnoreCase))
            return "Emergency Motion";
        if (text.Contains("Motion", StringComparison.OrdinalIgnoreCase))
            return "Motion";
        if (text.Contains("Bill", StringComparison.OrdinalIgnoreCase))
            return "Bill";
        if (text.Contains("Resolution", StringComparison.OrdinalIgnoreCase))
            return "Resolution";
        if (text.Contains("Question", StringComparison.OrdinalIgnoreCase))
            return "Parliamentary Question";
        if (text.Contains("Committee", StringComparison.OrdinalIgnoreCase))
            return "Committee";
        
        return "Activity";
    }




    private MemberActivityStats ExtractActivityStats(HtmlDocument doc)
    {
        var stats = new MemberActivityStats();
        
        try
        {
            // Look for the Statistics section
            var statsSection = doc.DocumentNode.SelectSingleNode("//h5[text()='Statistics']/following-sibling::div[@class='row']");
            
            if (statsSection != null)
            {
                var statItems = statsSection.SelectNodes(".//div[contains(@class, 'col-6')]");
                
                if (statItems != null)
                {
                    foreach (var item in statItems)
                    {
                        var numberNode = item.SelectSingleNode(".//h1[contains(@class, 'stat-number')]");
                        var titleNode = item.SelectSingleNode(".//p[contains(@class, 'stat-title')]");
                        
                        if (numberNode != null && titleNode != null)
                        {
                            var number = CleanText(numberNode.InnerText);
                            var title = CleanText(titleNode.InnerText);
                            
                            if (int.TryParse(number, out int value))
                            {
                                switch (title.ToLower())
                                {
                                    case "committee":
                                    case "committees":
                                        stats.TotalCommittees = value;
                                        break;
                                    case "emergency motions":
                                    case "emergency motion":
                                        stats.TotalEmergencyMotions = value;
                                        break;
                                    case "bills":
                                    case "bill":
                                        stats.TotalBills = value;
                                        break;
                                    case "motions":
                                    case "motion":
                                        stats.TotalMotions = value;
                                        break;
                                    case "resolutions":
                                    case "resolution":
                                        stats.TotalResolutions = value;
                                        break;
                                    case "committee work":
                                        stats.TotalCommitteeWork = value;
                                        break;
                                }
                            }
                        }
                    }
                }
            }
            
            // Calculate total activities
            stats.TotalActivities = stats.TotalBills + stats.TotalMotions + stats.TotalEmergencyMotions + 
                                  stats.TotalResolutions + stats.TotalCommitteeWork + stats.TotalCommittees;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting activity stats: {ex.Message}");
        }
        
        return stats;
    }

    private void ExtractDocuments(HtmlDocument doc, MemberProfile profile)
    {
        // Look for document links, especially asset declarations
        var docNodes = doc.DocumentNode.SelectNodes("//a[contains(@href, '.pdf') or contains(text(), 'Declaration') or contains(text(), 'Asset')]");
        if (docNodes != null)
        {
            foreach (var node in docNodes)
            {
                var href = node.GetAttributeValue("href", "");
                var text = CleanText(node.InnerText);
                
                if (!string.IsNullOrEmpty(href) || text.Contains("Declaration") || text.Contains("Asset"))
                {
                    var document = new MemberDocument
                    {
                        Title = !string.IsNullOrEmpty(text) ? text : "Document",
                        Url = href.StartsWith("http") ? href : (!string.IsNullOrEmpty(href) ? BaseUrl + href : ""),
                        Type = text.Contains("Asset") ? "Asset Declaration" : "Document"
                    };
                    
                    profile.Documents.Add(document);
                }
            }
        }
    }


    private string FindTextContaining(HtmlDocument doc, params string[] keywords)
    {
        var allTextNodes = doc.DocumentNode.SelectNodes("//text()[normalize-space()]");
        if (allTextNodes != null)
        {
            foreach (var textNode in allTextNodes)
            {
                var text = textNode.InnerText?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    var containsAll = keywords.All(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                    if (containsAll)
                    {
                        return text;
                    }
                }
            }
        }
        return string.Empty;
    }

    private string FindTextMatching(HtmlDocument doc, string pattern)
    {
        var allTextNodes = doc.DocumentNode.SelectNodes("//text()[normalize-space()]");
        if (allTextNodes != null)
        {
            foreach (var textNode in allTextNodes)
            {
                var text = textNode.InnerText?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    var match = Regex.Match(text, pattern);
                    if (match.Success)
                    {
                        return match.Value;
                    }
                }
            }
        }
        return string.Empty;
    }

    private string CleanText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        
        // Initial cleanup
        var cleaned = text.Trim()
                         .Replace("\n", " ")
                         .Replace("\r", "")
                         .Replace("\t", " ");
        
        // Remove JavaScript-like patterns
        cleaned = Regex.Replace(cleaned, @"function\s*\([^)]*\)\s*\{[^}]*\}", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"var\s+\w+\s*=\s*[^;]+;", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"document\.\w+", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"window\.\w+", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"console\.\w+\([^)]*\)", "", RegexOptions.IgnoreCase);
        
        // Remove common JavaScript keywords and patterns
        var jsPatterns = new[]
        {
            @"\$\([^)]*\)",  // jQuery selectors
            @"addEventListener\([^)]*\)", 
            @"querySelector\([^)]*\)",
            @"getElementById\([^)]*\)",
            @"className\s*=",
            @"innerHTML\s*=",
            @"src\s*=\s*[""'][^""']*[""']",
            @"href\s*=\s*[""'][^""']*[""']"
        };
        
        foreach (var pattern in jsPatterns)
        {
            cleaned = Regex.Replace(cleaned, pattern, "", RegexOptions.IgnoreCase);
        }
        
        // Remove HTML attributes that might have leaked through
        cleaned = Regex.Replace(cleaned, @"\b(class|id|style|onclick|onload|src|href)\s*=\s*[""'][^""']*[""']", "", RegexOptions.IgnoreCase);
        
        // Remove curly braces and square brackets (common in JS)
        cleaned = Regex.Replace(cleaned, @"[\{\}\[\]]", " ");
        
        // Remove semicolons at the end of lines
        cleaned = Regex.Replace(cleaned, @";\s*$", "");
        
        // Clean up multiple spaces
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        
        // Remove leading/trailing punctuation that might be artifacts
        cleaned = cleaned.Trim(' ', ',', ';', ':', '.', '!', '?', '-', '_', '=', '+');
        
        return cleaned.Trim();
    }

    private bool IsValidContent(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 3)
            return false;
            
        // Skip if it looks like JavaScript code
        var jsIndicators = new[] { "function", "var ", "let ", "const ", "document.", "window.", "console.", "addEventListener", "querySelector" };
        if (jsIndicators.Any(indicator => text.Contains(indicator, StringComparison.OrdinalIgnoreCase)))
            return false;
            
        // Skip if it's mostly symbols/punctuation
        var alphanumericCount = text.Count(char.IsLetterOrDigit);
        if (alphanumericCount < text.Length * 0.3) // Less than 30% alphanumeric
            return false;
            
        // Skip if it looks like CSS
        if (text.Contains("{") && text.Contains("}") && text.Contains(":"))
            return false;
            
        // Skip if it looks like HTML attributes
        if (text.StartsWith("class=") || text.StartsWith("id=") || text.StartsWith("style="))
            return false;
            
        return true;
    }

    private string GetNodeUrl(HtmlNode? node)
    {
        if (node == null) return "";
        
        var href = node.GetAttributeValue("href", "");
        if (string.IsNullOrEmpty(href)) return "";
        
        // Handle relative URLs
        if (href.StartsWith("/"))
        {
            return BaseUrl + href;
        }
        else if (href.StartsWith("http"))
        {
            return href;
        }
        
        return "";
    }






    private async Task<string> GetHtmlAsync(string url, string cacheFileName)
    {
        if (_useCache)
        {
            var cacheFilePath = Path.Combine(_cacheDirectory, cacheFileName);
            
            if (File.Exists(cacheFilePath))
            {
                Console.WriteLine($"Using cached profile HTML: {cacheFileName}");
                return await File.ReadAllTextAsync(cacheFilePath);
            }
        }
        
        Console.WriteLine($"Fetching profile from server: {url}");
        var html = await _httpClient.GetStringAsync(url);
        
        if (_useCache)
        {
            var cacheFilePath = Path.Combine(_cacheDirectory, cacheFileName);
            await File.WriteAllTextAsync(cacheFilePath, html);
            Console.WriteLine($"Cached profile HTML saved: {cacheFileName}");
        }
        
        return html;
    }

    private string GetCacheFileNameFromUrl(string url)
    {
        var uri = new Uri(url);
        var path = uri.AbsolutePath.Replace("/", "_").Replace("\\", "_");
        var fileName = $"profile{path}.html";
        
        // Replace any invalid filename characters
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }
        
        return fileName;
    }

    public async Task<int> DownloadAssetDeclarationsAsync(List<ParliamentMember> members)
    {
        int downloadCount = 0;
        var downloadsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "asset_declarations");
        
        if (!Directory.Exists(downloadsDirectory))
        {
            Directory.CreateDirectory(downloadsDirectory);
        }
        
        // Process members sequentially (one at a time) for reliability
        foreach (var member in members)
        {
            if (member.DetailedProfile?.Documents != null)
            {
                foreach (var document in member.DetailedProfile.Documents)
                {
                    if (document.Type.Equals("Asset Declaration", StringComparison.OrdinalIgnoreCase) && 
                        !string.IsNullOrEmpty(document.Url))
                    {
                        try
                        {
                            var extension = Path.GetExtension(document.Url);
                            if (string.IsNullOrEmpty(extension))
                            {
                                extension = ".pdf"; // Default to PDF if no extension found
                            }
                            
                            var sanitizedName = SanitizeFileName(member.Name);
                            var sanitizedParty = SanitizeFileName(member.Party);
                            var sanitizedConstituency = SanitizeFileName(member.Constituency);
                            
                            var fileName = $"{sanitizedName}_{sanitizedParty}_{sanitizedConstituency}{extension}";
                            var filePath = Path.Combine(downloadsDirectory, fileName);
                            
                            if (!File.Exists(filePath))
                            {
                                Console.WriteLine($"Downloading asset declaration for {member.Name}...");
                                
                                // Add delay to be respectful to the server
                                await Task.Delay(1000); // 1 second delay between downloads
                                
                                var fileBytes = await _httpClient.GetByteArrayAsync(document.Url);
                                await File.WriteAllBytesAsync(filePath, fileBytes);
                                downloadCount++;
                                Console.WriteLine($"  Saved as: {fileName}");
                            }
                            else
                            {
                                Console.WriteLine($"Asset declaration for {member.Name} already exists, skipping...");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error downloading asset declaration for {member.Name}: {ex.Message}");
                        }
                    }
                }
            }
        }
        
        return downloadCount;
    }
    
    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return "Unknown";
            
        // First, decode HTML entities like &#039; to actual characters
        var decoded = HttpUtility.HtmlDecode(fileName);
        
        // Remove invalid characters and replace with underscores
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = decoded;
        
        foreach (var invalidChar in invalidChars)
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }
        
        // Replace spaces and common problematic characters
        sanitized = sanitized.Replace(' ', '_')
                           .Replace('.', '_')
                           .Replace(',', '_')
                           .Replace(';', '_')
                           .Replace(':', '_')
                           .Replace('(', '_')
                           .Replace(')', '_')
                           .Replace('[', '_')
                           .Replace(']', '_')
                           .Replace('{', '_')
                           .Replace('}', '_')
                           .Replace('\'', '_') // Handle apostrophes specifically
                           .Replace('"', '_')
                           .Replace('&', '_');
        
        // Remove multiple consecutive underscores
        while (sanitized.Contains("__"))
        {
            sanitized = sanitized.Replace("__", "_");
        }
        
        // Remove leading/trailing underscores
        sanitized = sanitized.Trim('_');
        
        return string.IsNullOrEmpty(sanitized) ? "Unknown" : sanitized;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}