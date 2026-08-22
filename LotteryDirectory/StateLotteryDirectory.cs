// =====================================================================
//  StateLotteryDirectory.cs
//  Folder : LotteryDirectory/
//  Purpose: Self-contained US state lottery directory.
//           NO dependencies on any other DailyFantasyMAUI source file.
//           Data is built lazily the first time it is accessed.
// =====================================================================

namespace DailyFantasyMAUI.LotteryDirectory;

/// <summary>Immutable data record for one US state lottery entry.</summary>
public sealed class StateLottery
{
    // ── Core data ─────────────────────────────────────────────────────
    public string State        { get; init; } = "";
    public string Abbr         { get; init; } = "";
    public string Region       { get; init; } = "";
    public string LotteryName  { get; init; } = "";
    public string WebsiteUrl   { get; init; } = "";
    public string LogoUrl      { get; init; } = "";
    public bool   HasLottery   { get; init; } = true;

    // ── UI helpers (used in XAML bindings, no converter needed) ──────
    public bool   HasNoLottery => !HasLottery;
    public bool   CanVisit     => HasLottery && !string.IsNullOrEmpty(WebsiteUrl);
    public string DisplayUrl   => WebsiteUrl.Replace("https://www.", "")
                                            .Replace("https://", "")
                                            .TrimEnd('/');

    // Card background: active = dark navy panel / inactive = near-black
    public Color CardBg    => HasLottery ? Color.FromArgb("#0E2039")
                                         : Color.FromArgb("#0A0F1A");

    // State badge circle background (region accent when active, dark gray when not)
    public Color BadgeBg   => HasLottery ? RegionAccent
                                         : Color.FromArgb("#2A2A3A");

    // Lottery name text color
    public Color NameColor => HasLottery ? Colors.White
                                         : Color.FromArgb("#4B5563");

    // Website URL text color
    public Color UrlColor  => HasLottery ? Color.FromArgb("#60A5FA")
                                         : Color.FromArgb("#2A2A3A");

    // Left accent strip — unique color per region
    public Color RegionAccent => Region switch
    {
        "Northeast" => Color.FromArgb("#1565C0"),   // deep blue
        "Southeast" => Color.FromArgb("#B71C1C"),   // deep red
        "Midwest"   => Color.FromArgb("#1B5E20"),   // deep green
        "Southwest" => Color.FromArgb("#BF360C"),   // deep orange
        "West"      => Color.FromArgb("#4A148C"),   // deep purple
        _           => Color.FromArgb("#212121"),   // no lottery — near black
    };
}

/// <summary>Complete directory of US state lotteries — 50 states + DC.</summary>
public static class StateLotteryDirectory
{
    // ── Lazy init: list is only built the first time All is accessed ──
    private static readonly Lazy<IReadOnlyList<StateLottery>> _lazy = new(BuildList);

    /// <summary>All entries. First access triggers the build (fast, ~1 ms).</summary>
    public static IReadOnlyList<StateLottery> All => _lazy.Value;

    /// <summary>Distinct region names in display order (for filter chips).</summary>
    public static readonly string[] Regions =
        ["All", "Northeast", "Southeast", "Midwest", "Southwest", "West", "No Lottery"];

    /// <summary>Filter by region and/or search text.</summary>
    public static IEnumerable<StateLottery> Filter(string region, string search)
    {
        var q = All.AsEnumerable();
        if (!string.IsNullOrEmpty(region) && region != "All")
            q = q.Where(s => s.Region == region);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim().ToLowerInvariant();
            q = q.Where(s => s.State.ToLowerInvariant().Contains(t)
                           || s.Abbr.ToLowerInvariant().Contains(t)
                           || s.LotteryName.ToLowerInvariant().Contains(t));
        }
        return q;
    }

    // ── Private build (runs once) ─────────────────────────────────────
    private static string Fav(string domain) =>
        $"https://www.google.com/s2/favicons?domain={domain}&sz=128";

    private static IReadOnlyList<StateLottery> BuildList() => new List<StateLottery>
    {
        // ── Northeast ────────────────────────────────────────────────────
        new() { State="Connecticut",    Abbr="CT", Region="Northeast", LotteryName="Connecticut Lottery",          WebsiteUrl="https://www.ctlottery.org",          LogoUrl=Fav("ctlottery.org"),          HasLottery=true  },
        new() { State="Delaware",       Abbr="DE", Region="Northeast", LotteryName="Delaware Lottery",             WebsiteUrl="https://www.delottery.com",          LogoUrl=Fav("delottery.com"),          HasLottery=true  },
        new() { State="Maine",          Abbr="ME", Region="Northeast", LotteryName="Maine State Lottery",          WebsiteUrl="https://www.mainelottery.com",       LogoUrl=Fav("mainelottery.com"),       HasLottery=true  },
        new() { State="Maryland",       Abbr="MD", Region="Northeast", LotteryName="Maryland Lottery",             WebsiteUrl="https://www.mdlottery.com",          LogoUrl=Fav("mdlottery.com"),          HasLottery=true  },
        new() { State="Massachusetts",  Abbr="MA", Region="Northeast", LotteryName="Massachusetts Lottery",        WebsiteUrl="https://www.masslottery.com",        LogoUrl=Fav("masslottery.com"),        HasLottery=true  },
        new() { State="New Hampshire",  Abbr="NH", Region="Northeast", LotteryName="NH Lottery",                   WebsiteUrl="https://www.nhlottery.com",          LogoUrl=Fav("nhlottery.com"),          HasLottery=true  },
        new() { State="New Jersey",     Abbr="NJ", Region="Northeast", LotteryName="New Jersey Lottery",           WebsiteUrl="https://www.njlottery.com",          LogoUrl=Fav("njlottery.com"),          HasLottery=true  },
        new() { State="New York",       Abbr="NY", Region="Northeast", LotteryName="New York Lottery",             WebsiteUrl="https://www.nylottery.ny.gov",       LogoUrl=Fav("nylottery.ny.gov"),       HasLottery=true  },
        new() { State="Pennsylvania",   Abbr="PA", Region="Northeast", LotteryName="Pennsylvania Lottery",         WebsiteUrl="https://www.palottery.com",          LogoUrl=Fav("palottery.com"),          HasLottery=true  },
        new() { State="Rhode Island",   Abbr="RI", Region="Northeast", LotteryName="Rhode Island Lottery",         WebsiteUrl="https://www.rilot.com",              LogoUrl=Fav("rilot.com"),              HasLottery=true  },
        new() { State="Vermont",        Abbr="VT", Region="Northeast", LotteryName="Vermont Lottery",              WebsiteUrl="https://www.vtlottery.com",          LogoUrl=Fav("vtlottery.com"),          HasLottery=true  },
        new() { State="Washington D.C.",Abbr="DC", Region="Northeast", LotteryName="DC Lottery",                   WebsiteUrl="https://www.dclottery.com",          LogoUrl=Fav("dclottery.com"),          HasLottery=true  },

        // ── Southeast ────────────────────────────────────────────────────
        new() { State="Alabama",        Abbr="AL", Region="Southeast", LotteryName="Alabama Lottery",              WebsiteUrl="https://www.alottery.com",           LogoUrl=Fav("alottery.com"),           HasLottery=true  },
        new() { State="Arkansas",       Abbr="AR", Region="Southeast", LotteryName="Arkansas Scholarship Lottery", WebsiteUrl="https://www.myarkansaslottery.com",  LogoUrl=Fav("myarkansaslottery.com"),  HasLottery=true  },
        new() { State="Florida",        Abbr="FL", Region="Southeast", LotteryName="Florida Lottery",              WebsiteUrl="https://www.flalottery.com",         LogoUrl=Fav("flalottery.com"),         HasLottery=true  },
        new() { State="Georgia",        Abbr="GA", Region="Southeast", LotteryName="Georgia Lottery",              WebsiteUrl="https://www.galottery.com",          LogoUrl=Fav("galottery.com"),          HasLottery=true  },
        new() { State="Kentucky",       Abbr="KY", Region="Southeast", LotteryName="Kentucky Lottery",             WebsiteUrl="https://www.kylottery.com",          LogoUrl=Fav("kylottery.com"),          HasLottery=true  },
        new() { State="Louisiana",      Abbr="LA", Region="Southeast", LotteryName="Louisiana Lottery",            WebsiteUrl="https://www.louisianalottery.com",   LogoUrl=Fav("louisianalottery.com"),   HasLottery=true  },
        new() { State="Mississippi",    Abbr="MS", Region="Southeast", LotteryName="Mississippi Lottery",          WebsiteUrl="https://www.mslottery.com",          LogoUrl=Fav("mslottery.com"),          HasLottery=true  },
        new() { State="North Carolina", Abbr="NC", Region="Southeast", LotteryName="NC Education Lottery",         WebsiteUrl="https://www.nclottery.com",          LogoUrl=Fav("nclottery.com"),          HasLottery=true  },
        new() { State="South Carolina", Abbr="SC", Region="Southeast", LotteryName="SC Education Lottery",         WebsiteUrl="https://www.sceducationlottery.com", LogoUrl=Fav("sceducationlottery.com"), HasLottery=true  },
        new() { State="Tennessee",      Abbr="TN", Region="Southeast", LotteryName="Tennessee Education Lottery",  WebsiteUrl="https://www.tnlottery.com",          LogoUrl=Fav("tnlottery.com"),          HasLottery=true  },
        new() { State="Virginia",       Abbr="VA", Region="Southeast", LotteryName="Virginia Lottery",             WebsiteUrl="https://www.valottery.com",          LogoUrl=Fav("valottery.com"),          HasLottery=true  },
        new() { State="West Virginia",  Abbr="WV", Region="Southeast", LotteryName="West Virginia Lottery",        WebsiteUrl="https://www.wvlottery.com",          LogoUrl=Fav("wvlottery.com"),          HasLottery=true  },

        // ── Midwest ──────────────────────────────────────────────────────
        new() { State="Illinois",       Abbr="IL", Region="Midwest",   LotteryName="Illinois Lottery",             WebsiteUrl="https://www.illinoislottery.com",    LogoUrl=Fav("illinoislottery.com"),    HasLottery=true  },
        new() { State="Indiana",        Abbr="IN", Region="Midwest",   LotteryName="Hoosier Lottery",              WebsiteUrl="https://www.hoosierlottery.com",     LogoUrl=Fav("hoosierlottery.com"),     HasLottery=true  },
        new() { State="Iowa",           Abbr="IA", Region="Midwest",   LotteryName="Iowa Lottery",                 WebsiteUrl="https://www.ialottery.com",          LogoUrl=Fav("ialottery.com"),          HasLottery=true  },
        new() { State="Kansas",         Abbr="KS", Region="Midwest",   LotteryName="Kansas Lottery",               WebsiteUrl="https://www.kslottery.com",          LogoUrl=Fav("kslottery.com"),          HasLottery=true  },
        new() { State="Michigan",       Abbr="MI", Region="Midwest",   LotteryName="Michigan Lottery",             WebsiteUrl="https://www.michiganlottery.com",    LogoUrl=Fav("michiganlottery.com"),    HasLottery=true  },
        new() { State="Minnesota",      Abbr="MN", Region="Midwest",   LotteryName="Minnesota Lottery",            WebsiteUrl="https://www.mnlottery.com",          LogoUrl=Fav("mnlottery.com"),          HasLottery=true  },
        new() { State="Missouri",       Abbr="MO", Region="Midwest",   LotteryName="Missouri Lottery",             WebsiteUrl="https://www.molottery.com",          LogoUrl=Fav("molottery.com"),          HasLottery=true  },
        new() { State="Nebraska",       Abbr="NE", Region="Midwest",   LotteryName="Nebraska Lottery",             WebsiteUrl="https://www.nelottery.com",          LogoUrl=Fav("nelottery.com"),          HasLottery=true  },
        new() { State="North Dakota",   Abbr="ND", Region="Midwest",   LotteryName="North Dakota Lottery",         WebsiteUrl="https://www.lottery.nd.gov",         LogoUrl=Fav("lottery.nd.gov"),         HasLottery=true  },
        new() { State="Ohio",           Abbr="OH", Region="Midwest",   LotteryName="Ohio Lottery",                 WebsiteUrl="https://www.ohiolottery.com",        LogoUrl=Fav("ohiolottery.com"),        HasLottery=true  },
        new() { State="South Dakota",   Abbr="SD", Region="Midwest",   LotteryName="South Dakota Lottery",         WebsiteUrl="https://www.sdlottery.com",          LogoUrl=Fav("sdlottery.com"),          HasLottery=true  },
        new() { State="Wisconsin",      Abbr="WI", Region="Midwest",   LotteryName="Wisconsin Lottery",            WebsiteUrl="https://www.wilottery.com",          LogoUrl=Fav("wilottery.com"),          HasLottery=true  },

        // ── Southwest ────────────────────────────────────────────────────
        new() { State="Arizona",        Abbr="AZ", Region="Southwest", LotteryName="Arizona Lottery",              WebsiteUrl="https://www.arizonalottery.com",     LogoUrl=Fav("arizonalottery.com"),     HasLottery=true  },
        new() { State="New Mexico",     Abbr="NM", Region="Southwest", LotteryName="New Mexico Lottery",           WebsiteUrl="https://www.nmlottery.com",          LogoUrl=Fav("nmlottery.com"),          HasLottery=true  },
        new() { State="Oklahoma",       Abbr="OK", Region="Southwest", LotteryName="Oklahoma Lottery",             WebsiteUrl="https://www.lottery.ok.gov",         LogoUrl=Fav("lottery.ok.gov"),         HasLottery=true  },
        new() { State="Texas",          Abbr="TX", Region="Southwest", LotteryName="Texas Lottery",                WebsiteUrl="https://www.txlottery.org",          LogoUrl=Fav("txlottery.org"),          HasLottery=true  },

        // ── West ─────────────────────────────────────────────────────────
        new() { State="California",     Abbr="CA", Region="West",      LotteryName="California Lottery",           WebsiteUrl="https://www.calottery.com",          LogoUrl=Fav("calottery.com"),          HasLottery=true  },
        new() { State="Colorado",       Abbr="CO", Region="West",      LotteryName="Colorado Lottery",             WebsiteUrl="https://www.coloradolottery.com",    LogoUrl=Fav("coloradolottery.com"),    HasLottery=true  },
        new() { State="Idaho",          Abbr="ID", Region="West",      LotteryName="Idaho Lottery",                WebsiteUrl="https://www.idaholottery.com",       LogoUrl=Fav("idaholottery.com"),       HasLottery=true  },
        new() { State="Montana",        Abbr="MT", Region="West",      LotteryName="Montana Lottery",              WebsiteUrl="https://www.montanalottery.com",     LogoUrl=Fav("montanalottery.com"),     HasLottery=true  },
        new() { State="Oregon",         Abbr="OR", Region="West",      LotteryName="Oregon Lottery",               WebsiteUrl="https://www.oregonlottery.org",      LogoUrl=Fav("oregonlottery.org"),      HasLottery=true  },
        new() { State="Washington",     Abbr="WA", Region="West",      LotteryName="Washington's Lottery",         WebsiteUrl="https://www.walottery.com",          LogoUrl=Fav("walottery.com"),          HasLottery=true  },
        new() { State="Wyoming",        Abbr="WY", Region="West",      LotteryName="Wyoming Lottery",              WebsiteUrl="https://www.wylotto.com",            LogoUrl=Fav("wylotto.com"),            HasLottery=true  },

        // ── No State Lottery ─────────────────────────────────────────────
        new() { State="Alaska",         Abbr="AK", Region="No Lottery",LotteryName="No State Lottery",             WebsiteUrl="",                                   LogoUrl="",                            HasLottery=false },
        new() { State="Hawaii",         Abbr="HI", Region="No Lottery",LotteryName="No State Lottery",             WebsiteUrl="",                                   LogoUrl="",                            HasLottery=false },
        new() { State="Nevada",         Abbr="NV", Region="No Lottery",LotteryName="No State Lottery",             WebsiteUrl="",                                   LogoUrl="",                            HasLottery=false },
        new() { State="Utah",           Abbr="UT", Region="No Lottery",LotteryName="No State Lottery",             WebsiteUrl="",                                   LogoUrl="",                            HasLottery=false },
    };
}
