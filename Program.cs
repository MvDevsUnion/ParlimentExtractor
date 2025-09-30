using Parliment.Extractor.Services;
using Parliment.Extractor.Models;

namespace Parliment.Extractor;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Parliament Data Extractor");
        Console.WriteLine("========================");

        // Parse command line arguments
        bool includeDetailedProfiles = false;
        bool useCache = false;
        bool downloadAssets = false;
        
        foreach (var arg in args)
        {
            if (arg.Equals("--detailed", StringComparison.OrdinalIgnoreCase))
            {
                includeDetailedProfiles = true;
            }
            else if (arg.Equals("--cache", StringComparison.OrdinalIgnoreCase))
            {
                useCache = true;
            }
            else if (arg.Equals("--download-assets", StringComparison.OrdinalIgnoreCase))
            {
                downloadAssets = true;
            }
        }

        useCache = true;
        includeDetailedProfiles = true;
        
        if (includeDetailedProfiles)
        {
            Console.WriteLine("Detailed profile extraction enabled");
        }
        else
        {
            includeDetailedProfiles = true;
            Console.WriteLine("Basic extraction mode (use --detailed for full profiles)");
        }
        
        if (useCache)
        {
            Console.WriteLine("Cache mode enabled - using cached HTML files when available");
        }
        else
        {
            Console.WriteLine("Live mode - fetching fresh data from server (use --cache to use cached files)");
        }
        
        if (downloadAssets)
        {
            Console.WriteLine("Asset download mode enabled - will download asset declarations");
        }
        else
        {
            Console.WriteLine("Asset download disabled (use --download-assets to download asset declarations)");
        }

        var extractor = new ParliamentExtractor(useCache);
        
        try
        {
            var parliamentData = await extractor.ExtractParliamentDataAsync(includeDetailedProfiles);
            
            var filename = includeDetailedProfiles ? "parliament_data_detailed.json" : "parliament_data.json";
            var outputPath = Path.Combine(Directory.GetCurrentDirectory(), filename);
            await extractor.SaveToJsonAsync(parliamentData, outputPath);
            
            Console.WriteLine("\nExtraction Summary:");
            Console.WriteLine($"- Total Members: {parliamentData.TotalMembers}");
            Console.WriteLine($"- Members Extracted: {parliamentData.Members.Count}");
            Console.WriteLine($"- Number of Parties: {parliamentData.NumberOfParties}");
            
            if (parliamentData.PartyBreakdown.Any())
            {
                Console.WriteLine("\nParty Breakdown:");
                foreach (var party in parliamentData.PartyBreakdown.OrderByDescending(p => p.Value))
                {
                    Console.WriteLine($"- {party.Key}: {party.Value} members");
                }
            }
            
            if (!string.IsNullOrEmpty(parliamentData.Leadership.Speaker))
            {
                Console.WriteLine("\nLeadership:");
                Console.WriteLine($"- Speaker: {parliamentData.Leadership.Speaker}");
                Console.WriteLine($"- Deputy Speaker: {parliamentData.Leadership.DeputySpeaker}");
                Console.WriteLine($"- Majority Leader: {parliamentData.Leadership.MajorityLeader}");
                Console.WriteLine($"- Minority Leader: {parliamentData.Leadership.MinorityLeader}");
            }
            
            Console.WriteLine($"\nData saved to: {outputPath}");
            
            // Download asset declarations if requested
            if (downloadAssets && includeDetailedProfiles)
            {
                Console.WriteLine("\nDownloading asset declarations...");
                var profileExtractor = new MemberProfileExtractor(null, useCache);
                try
                {
                    var downloadCount = await profileExtractor.DownloadAssetDeclarationsAsync(parliamentData.Members);
                    Console.WriteLine($"Downloaded {downloadCount} asset declaration(s) to './asset_declarations/' directory");
                }
                catch (Exception downloadEx)
                {
                    Console.WriteLine($"Error downloading asset declarations: {downloadEx.Message}");
                }
                finally
                {
                    profileExtractor.Dispose();
                }
            }
            else if (downloadAssets && !includeDetailedProfiles)
            {
                Console.WriteLine("\nWarning: Asset download requires detailed profiles. Use --detailed flag to enable profile extraction.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine("Please check your internet connection and try again.");
        }
        finally
        {
            extractor.Dispose();
        }

        Console.ReadLine();
    }
}