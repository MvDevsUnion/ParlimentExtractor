namespace Parliment.Extractor.Models;

public class ParliamentMember
{
    public string Name { get; set; } = string.Empty;
    public string Constituency { get; set; } = string.Empty;
    public string Party { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public string ProfileUrl { get; set; } = string.Empty;
    
    public MemberActivityStats  ActivityStats { get; set; } = new MemberActivityStats();
    
    // Detailed profile information (populated when profile is fetched)
    public MemberProfile? DetailedProfile { get; set; }
    
    
}


public class MemberProfile
{
    public string PartyColor { get; set; } = string.Empty;
    
    // Contact Information`
    public MemberContact Contact { get; set; } = new();
    
    // Parliamentary Activity
    public MemberActivity Activity { get; set; } = new();
    
    // Documents
    public List<MemberDocument> Documents { get; set; } = new();
}

public class MemberContact
{
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Facebook { get; set; } = string.Empty;
    public string Twitter { get; set; } = string.Empty;
}

/// <summary>
/// scrap this data from the page instead of client side calc
/// </summary>
public class MemberActivityStats
{
    public int TotalCommittees { get; set; } = 0;
    public int TotalEmergencyMotions { get; set; } = 0;
    public int TotalBills { get; set; } = 0;
    public int TotalMotions { get; set; } = 0;
    public int TotalResolutions { get; set; } = 0;
    public int TotalCommitteeWork { get; set; } = 0;
    public int TotalParliamentaryQuestions { get; set; } = 0;
    public int TotalActivities { get; set; } = 0;
}

public class MemberActivity
{
    /// <summary>
    /// Save the header
    /// eg : <h4 class="mb-3 mt-5">Proposed Motions, Emergency Motions, Motion of Privilege, Bills, Resolutions and Parliamentary Questions</h4>
    /// </summary>
    public string SectionName { get; set; } = string.Empty;
    
    //here we store all the details data we got under the heading above
    public List<MemberActivityDetail>  Details { get; set; } = new();
}

public class MemberActivityDetail
{
    /// <summary>
    /// eg : <div class="card-corner-container">Emergency Motion</div>
    /// </summary>
    public string ActivityName { get; set; } = string.Empty;
    
    /// <summary>
    /// <h6 class="card-main-title mt-4">Emergency Motion submitted under Section 170 of the Rules of Procedure of the Parliament, by Hon. Mohamed Ibrahim, MP for North Galolhu</h6>
    /// </summary>
    public string ActivityDescr { get; set; } = string.Empty;
    
    /// <summary>
    /// the url is usually wrapped around the card-list-link
    /// </summary>
    public string Url { get; set; } = string.Empty;
}

public class MemberDocument
{
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Asset Declaration, etc.
    public string Url { get; set; } = string.Empty;
}

public class ParliamentData
{
    public int TotalMembers { get; set; }
    public int NumberOfParties { get; set; }
    public ParliamentLeadership Leadership { get; set; } = new();
    public List<ParliamentMember> Members { get; set; } = new();
    public Dictionary<string, int> PartyBreakdown { get; set; } = new();
}

public class ParliamentLeadership
{
    public string Speaker { get; set; } = string.Empty;
    public string DeputySpeaker { get; set; } = string.Empty;
    public string MajorityLeader { get; set; } = string.Empty;
    public string MinorityLeader { get; set; } = string.Empty;
}