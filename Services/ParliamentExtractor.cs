using System.Text.Json;
using HtmlAgilityPack;
using Parliment.Extractor.Models;

namespace Parliment.Extractor.Services;

public class ParliamentExtractor
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly bool _useCache;
    private const string BaseUrl = "https://majlis.gov.mv";
    private const string MembersUrl = "https://majlis.gov.mv/en/20-parliament/members";

    public ParliamentExtractor(bool useCache = false)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        
        _useCache = useCache;
        _cacheDirectory = Path.Combine(Directory.GetCurrentDirectory(), "cache");
        
        if (_useCache && !Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    public async Task<ParliamentData> ExtractParliamentDataAsync(bool includeDetailedProfiles = false)
    {
        try
        {
            Console.WriteLine("Fetching parliament members page...");
            var html = await GetHtmlAsync(MembersUrl, "members.html");
            
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var parliamentData = new ParliamentData();

            // Extract the actual data first
            ExtractLeadership(doc, parliamentData);
            await ExtractMembers(doc, parliamentData);
            
            // Optionally fetch detailed profiles for each member
            if (includeDetailedProfiles)
            {
                await ExtractDetailedProfiles(parliamentData);
            }
            
            // Calculate statistics from extracted data (client-side)
            CalculateStatisticsFromData(parliamentData);

            return parliamentData;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting parliament data: {ex.Message}");
            throw;
        }
    }

    private async Task ExtractDetailedProfiles(ParliamentData parliamentData)
    {
        Console.WriteLine($"\nFetching detailed profiles for {parliamentData.Members.Count} members...");
        
        var profileExtractor = new MemberProfileExtractor(_httpClient, _useCache);
        var semaphore = new SemaphoreSlim(3, 3); // Limit to 3 concurrent requests
        var successCount = 0;
        
        var tasks = parliamentData.Members.Select(async (member, index) =>
        {
            if (string.IsNullOrEmpty(member.ProfileUrl))
            {
                Console.WriteLine($"[{index + 1}/{parliamentData.Members.Count}] Skipping {member.Name} - No profile URL");
                return;
            }

            await semaphore.WaitAsync();
            try
            {
                Console.WriteLine($"[{index + 1}/{parliamentData.Members.Count}] Fetching profile for {member.Name}...");
                
                var (profile, stats) = await profileExtractor.ExtractProfileAsync(member.ProfileUrl);
                if (profile != null)
                {
                    member.DetailedProfile = profile;
                    member.ActivityStats = stats;
                    Interlocked.Increment(ref successCount);
                    Console.WriteLine($"[{index + 1}/{parliamentData.Members.Count}] ✓ {member.Name} profile extracted");
                }
                else
                {
                    Console.WriteLine($"[{index + 1}/{parliamentData.Members.Count}] ✗ Failed to extract profile for {member.Name}");
                }
                
                // Small delay to be respectful to the server
                await Task.Delay(500);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        
        Console.WriteLine($"\nProfile extraction complete: {successCount}/{parliamentData.Members.Count} profiles extracted successfully");
    }

    private void CalculateStatisticsFromData(ParliamentData parliamentData)
    {
        // Calculate total members from actual extracted data
        parliamentData.TotalMembers = parliamentData.Members.Count;
        
        // Calculate party breakdown from actual member data
        parliamentData.PartyBreakdown.Clear();
        
        foreach (var member in parliamentData.Members)
        {
            if (!string.IsNullOrEmpty(member.Party))
            {
                var party = member.Party.Trim();
                if (parliamentData.PartyBreakdown.ContainsKey(party))
                {
                    parliamentData.PartyBreakdown[party]++;
                }
                else
                {
                    parliamentData.PartyBreakdown[party] = 1;
                }
            }
        }

        // Calculate number of parties from actual data
        parliamentData.NumberOfParties = parliamentData.PartyBreakdown.Count;
        
        Console.WriteLine($"Client-side calculations:");
        Console.WriteLine($"- Total members: {parliamentData.TotalMembers}");
        Console.WriteLine($"- Number of parties: {parliamentData.NumberOfParties}");
        
        if (parliamentData.PartyBreakdown.Any())
        {
            Console.WriteLine("- Party breakdown:");
            foreach (var party in parliamentData.PartyBreakdown.OrderByDescending(p => p.Value))
            {
                Console.WriteLine($"  * {party.Key}: {party.Value} members");
            }
        }
    }

    private void ExtractLeadership(HtmlDocument doc, ParliamentData parliamentData)
    {
        // Look for leadership cards - typically contain person's name (h5) followed by position
        var allHeaders = doc.DocumentNode.SelectNodes("//h5 | //h4 | //h3");
        
        if (allHeaders != null)
        {
            for (int i = 0; i < allHeaders.Count; i++)
            {
                var nameHeader = allHeaders[i];
                var nameText = nameHeader.InnerText?.Trim();
                
                if (string.IsNullOrEmpty(nameText)) continue;
                
                // Look for position text in the next sibling or nearby elements
                var positionText = "";
                
                // Check next sibling elements for position
                var nextSibling = nameHeader.NextSibling;
                while (nextSibling != null)
                {
                    if (nextSibling.NodeType == HtmlNodeType.Text)
                    {
                        var text = nextSibling.InnerText?.Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            positionText = text;
                            break;
                        }
                    }
                    else if (nextSibling.NodeType == HtmlNodeType.Element)
                    {
                        var text = nextSibling.InnerText?.Trim();
                        if (!string.IsNullOrEmpty(text) && (
                            text.Contains("Speaker") || text.Contains("Leader") || 
                            text.Contains("Chairman") || text.Contains("President")))
                        {
                            positionText = text;
                            break;
                        }
                    }
                    nextSibling = nextSibling.NextSibling;
                }
                
                // Also check parent container for position info
                if (string.IsNullOrEmpty(positionText))
                {
                    var parent = nameHeader.ParentNode;
                    if (parent != null)
                    {
                        var allText = parent.InnerText;
                        if (allText.Contains("Speaker") || allText.Contains("Leader"))
                        {
                            var lines = allText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                var trimmedLine = line.Trim();
                                if (trimmedLine.Contains("Speaker") || trimmedLine.Contains("Leader"))
                                {
                                    positionText = trimmedLine;
                                    break;
                                }
                            }
                        }
                    }
                }
                
                // Assign based on position
                if (!string.IsNullOrEmpty(positionText))
                {
                    if (positionText.Equals("Speaker", StringComparison.OrdinalIgnoreCase))
                    {
                        parliamentData.Leadership.Speaker = nameText;
                    }
                    else if (positionText.Contains("Deputy Speaker", StringComparison.OrdinalIgnoreCase))
                    {
                        parliamentData.Leadership.DeputySpeaker = nameText;
                    }
                    else if (positionText.Contains("Majority Leader", StringComparison.OrdinalIgnoreCase))
                    {
                        parliamentData.Leadership.MajorityLeader = nameText;
                    }
                    else if (positionText.Contains("Minority Leader", StringComparison.OrdinalIgnoreCase))
                    {
                        parliamentData.Leadership.MinorityLeader = nameText;
                    }
                }
            }
        }
        
        // Alternative approach: look for text patterns that indicate leadership
        var allTextNodes = doc.DocumentNode.SelectNodes("//text()[normalize-space()]");
        if (allTextNodes != null)
        {
            string currentName = "";
            foreach (var textNode in allTextNodes)
            {
                var text = textNode.InnerText?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                
                // If we find a position, try to associate it with the previous name we found
                if (text.Equals("Speaker", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(currentName))
                {
                    if (string.IsNullOrEmpty(parliamentData.Leadership.Speaker))
                        parliamentData.Leadership.Speaker = currentName;
                }
                else if (text.Contains("Deputy Speaker", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(currentName))
                {
                    if (string.IsNullOrEmpty(parliamentData.Leadership.DeputySpeaker))
                        parliamentData.Leadership.DeputySpeaker = currentName;
                }
                else if (text.Contains("Majority Leader", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(currentName))
                {
                    if (string.IsNullOrEmpty(parliamentData.Leadership.MajorityLeader))
                        parliamentData.Leadership.MajorityLeader = currentName;
                }
                else if (text.Contains("Minority Leader", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(currentName))
                {
                    if (string.IsNullOrEmpty(parliamentData.Leadership.MinorityLeader))
                        parliamentData.Leadership.MinorityLeader = currentName;
                }
                
                // Keep track of potential names (longer text that could be a person's name)
                if (text.Length > 10 && text.Contains(" ") && !text.Contains("Parliament") && !text.Contains("Member"))
                {
                    currentName = text;
                }
            }
        }
    }

    private async Task ExtractMembers(HtmlDocument doc, ParliamentData parliamentData)
    {
        // Look for member links that point to individual member pages
        var memberLinkNodes = doc.DocumentNode.SelectNodes("//a[contains(@href, '/en/20-parliament/members/')]");
        
        if (memberLinkNodes == null)
        {
            Console.WriteLine("No member link nodes found, trying alternative approach...");
            // Fallback to looking for any links with member-like patterns
            memberLinkNodes = doc.DocumentNode.SelectNodes("//a[@href] | //div[contains(@class, 'card')] | //div[contains(@class, 'member')]");
        }

        if (memberLinkNodes != null)
        {
            Console.WriteLine($"Processing {memberLinkNodes.Count} potential member link nodes...");
            
            foreach (var node in memberLinkNodes)
            {
                var member = ExtractMemberFromNode(node);
                if (member != null && !string.IsNullOrEmpty(member.Name))
                {
                    parliamentData.Members.Add(member);
                }
            }
        }

        Console.WriteLine($"Successfully extracted {parliamentData.Members.Count} members");
    }

    private ParliamentMember? ExtractMemberFromNode(HtmlNode node)
    {
        try
        {
            // Member name is typically in h5
            var nameNode = node.SelectSingleNode(".//h5 | .//h4 | .//h3 | .//strong | .//b | .//*[contains(@class, 'name')]");
            
            // Constituency is typically in h6 (appears after the member name)
            var constituencyNode = node.SelectSingleNode(".//h6 | .//*[contains(@class, 'constituency')] | .//*[contains(@class, 'location')]");
            
            // Party code appears early in the card, usually before the name
            // Look for short text nodes that contain known party codes
            var allTextNodes = node.SelectNodes(".//text()[normalize-space()]");
            string? partyCode = null;
            
            if (allTextNodes != null)
            {
                var knownPartyCodes = new[] { "PNC", "MDP", "IND", "MDA", "JP", "MNP" };
                foreach (var textNode in allTextNodes)
                {
                    var text = textNode.InnerText?.Trim();
                    if (!string.IsNullOrEmpty(text) && knownPartyCodes.Contains(text))
                    {
                        partyCode = text;
                        break;
                    }
                }
            }
            
            var imageNode = node.SelectSingleNode(".//img");
            
            // If the current node is a link, use it directly; otherwise look for child links
            var profileUrl = "";
            if (node.Name.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                profileUrl = node.GetAttributeValue("href", "");
            }
            else
            {
                var linkNode = node.SelectSingleNode(".//a[@href]");
                profileUrl = linkNode?.GetAttributeValue("href", "") ?? "";
            }

            var name = nameNode?.InnerText?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length < 3) return null;

            // Skip if this looks like a heading or navigation element
            if (name.Contains("Speaker") || name.Contains("Leader") || name.Contains("Parliament"))
                return null;

            var member = new ParliamentMember
            {
                Name = CleanText(name),
                Constituency = CleanText(constituencyNode?.InnerText),
                Party = CleanText(partyCode),
                PhotoUrl = imageNode?.GetAttributeValue("src", "") ?? "",
                ProfileUrl = profileUrl
            };

            // Only include members with both name and some identifying information
            if (string.IsNullOrEmpty(member.Constituency) && string.IsNullOrEmpty(member.Party))
                return null;

            if (!string.IsNullOrEmpty(member.PhotoUrl) && member.PhotoUrl.StartsWith("/"))
            {
                member.PhotoUrl = BaseUrl + member.PhotoUrl;
            }

            if (!string.IsNullOrEmpty(member.ProfileUrl) && member.ProfileUrl.StartsWith("/"))
            {
                member.ProfileUrl = BaseUrl + member.ProfileUrl;
            }

            return member;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting member from node: {ex.Message}");
            return null;
        }
    }


    private string ExtractNameFromText(string text)
    {
        var parts = text.Split(':');
        return parts.Length > 1 ? parts[1].Trim() : text.Trim();
    }

    private string CleanText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Trim().Replace("\n", " ").Replace("\r", "").Replace("\t", " ");
    }

    public async Task SaveToJsonAsync(ParliamentData data, string filePath)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(data, options);
            await File.WriteAllTextAsync(filePath, json);
            
            Console.WriteLine($"Parliament data saved to: {filePath}");
            Console.WriteLine($"Total members: {data.TotalMembers}");
            Console.WriteLine($"Successfully extracted: {data.Members.Count} members");
            Console.WriteLine($"Parties represented: {data.NumberOfParties}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving JSON file: {ex.Message}");
            throw;
        }
    }

    private async Task<string> GetHtmlAsync(string url, string cacheFileName)
    {
        if (_useCache)
        {
            var cacheFilePath = Path.Combine(_cacheDirectory, cacheFileName);
            
            if (File.Exists(cacheFilePath))
            {
                Console.WriteLine($"Using cached HTML: {cacheFileName}");
                return await File.ReadAllTextAsync(cacheFilePath);
            }
        }
        
        Console.WriteLine($"Fetching from server: {url}");
        var html = await _httpClient.GetStringAsync(url);
        
        if (_useCache)
        {
            var cacheFilePath = Path.Combine(_cacheDirectory, cacheFileName);
            await File.WriteAllTextAsync(cacheFilePath, html);
            Console.WriteLine($"Cached HTML saved: {cacheFileName}");
        }
        
        return html;
    }

    private string GetCacheFileNameFromUrl(string url)
    {
        var uri = new Uri(url);
        var path = uri.AbsolutePath.Replace("/", "_").Replace("\\", "_");
        return $"{path}.html";
    }


    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}