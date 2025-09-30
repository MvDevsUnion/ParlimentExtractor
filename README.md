# Parliament Data Extractor

A C# console application for extracting and analyzing parliament member data from the Maldivian Parliament website (majlis.gov.mv). This tool scrapes member information, detailed profiles, activity statistics, and asset declarations to provide comprehensive parliament analytics.

## Features

- **Member Information Extraction**: Scrapes basic member details including name, constituency, party affiliation, and contact information
- **Detailed Profile Analysis**: Extracts comprehensive member profiles with activity statistics and parliamentary work history
- **Leadership Information**: Identifies and extracts parliament leadership positions (Speaker, Deputy Speaker, Majority/Minority Leaders)
- **Activity Statistics**: Tracks member activities including bills, motions, resolutions, and committee work
- **Asset Declaration Downloads**: Downloads member asset declaration documents
- **Caching Support**: Implements intelligent caching to reduce server load and improve performance
- **Concurrent Processing**: Uses semaphore-based concurrency control for efficient data extraction
- **JSON Export**: Exports all extracted data to structured JSON format

## Prerequisites

- .NET 9.0 or later
- Internet connection for data extraction
- HtmlAgilityPack NuGet package (automatically included)

## Installation

1. Clone the repository
2. Navigate to the project directory
3. Restore dependencies:
   ```bash
   dotnet restore
   ```
4. Build the project:
   ```bash
   dotnet build
   ```

## Usage

### Basic Usage
```bash
dotnet run
```

### Command Line Options

- `--detailed`: Enable detailed profile extraction for all members
- `--cache`: Use cached HTML files when available (recommended for development)
- `--download-assets`: Download asset declaration documents (requires --detailed)

### Examples

**Extract basic member information:**
```bash
dotnet run
```

**Extract detailed profiles with caching:**
```bash
dotnet run --detailed --cache
```

**Full extraction with asset downloads:**
```bash
dotnet run --detailed --cache --download-assets
```

## Output

The application generates the following outputs:

### JSON Files
- `parliament_data.json`: Basic member information and statistics
- `parliament_data_detailed.json`: Complete dataset with detailed profiles (when using --detailed)

### Directory Structure
```
├── cache/                    # Cached HTML files (when using --cache)
├── asset_declarations/       # Downloaded asset declarations (when using --download-assets)
├── parliament_data.json      # Basic extraction output
└── parliament_data_detailed.json  # Detailed extraction output
```

### Sample JSON Structure
```json
{
  "totalMembers": 87,
  "numberOfParties": 6,
  "leadership": {
    "speaker": "Mohamed Aslam",
    "deputySpeaker": "Eva Abdulla",
    "majorityLeader": "Ahmed Nihan",
    "minorityLeader": "Ibrahim Mohamed Solih"
  },
  "members": [
    {
      "name": "Ahmed Nihan",
      "constituency": "Vilimale'",
      "party": "PNC",
      "photoUrl": "https://majlis.gov.mv/...",
      "profileUrl": "https://majlis.gov.mv/...",
      "activityStats": {
        "totalCommittees": 3,
        "totalBills": 12,
        "totalMotions": 8,
        "totalActivities": 45
      },
      "detailedProfile": {
        "contact": {
          "email": "member@majlis.gov.mv",
          "phone": "+960 123-4567"
        },
        "activity": {
          "sectionName": "Parliamentary Activities",
          "details": [...]
        },
        "documents": [...]
      }
    }
  ],
  "partyBreakdown": {
    "PNC": 45,
    "MDP": 35,
    "IND": 5,
    "JP": 2
  }
}
```

## Project Structure

- **Program.cs**: Main entry point with command-line argument parsing
- **Models/ParliamentMember.cs**: Data models for parliament members and related entities
- **Services/ParliamentExtractor.cs**: Core extraction logic for member data and statistics
- **Services/MemberProfileExtractor.cs**: Detailed profile extraction and asset declaration handling

## Key Classes

### ParliamentMember
Represents a parliament member with basic information and optional detailed profile data.

### ParliamentExtractor
Main service class responsible for:
- Fetching and parsing the parliament members page
- Extracting member information and leadership data
- Coordinating detailed profile extraction
- Generating statistics and exporting data

### MemberProfileExtractor
Specialized service for:
- Extracting detailed member profiles
- Processing activity statistics
- Downloading asset declarations
- Managing profile-specific caching

## Performance Considerations

- **Concurrent Processing**: Limited to 3 concurrent requests to respect server resources
- **Intelligent Caching**: Reduces redundant HTTP requests during development
- **Rate Limiting**: 500ms delay between requests to be respectful to the server
- **Error Handling**: Robust error handling with detailed logging

## Data Sources

This application extracts data from:
- Parliament Members Page: `https://majlis.gov.mv/en/20-parliament/members`
- Individual Member Profiles: `https://majlis.gov.mv/en/20-parliament/members/[member-slug]`
- Asset Declaration Documents: Various PDF documents linked from member profiles

## Legal and Ethical Considerations

- This tool is designed for research and transparency purposes
- Respects server resources through rate limiting and caching
- Only accesses publicly available information
- Follows ethical web scraping practices

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## License

This project is open source and available under the MIT License.

## Disclaimer

This tool is not affiliated with the Maldivian Parliament. The data extracted is publicly available information used for research and transparency purposes. Users are responsible for ensuring compliance with applicable terms of service and data usage policies.