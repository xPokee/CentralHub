using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CentCom.Common.Data;
using CentCom.Common.Models;
using CentCom.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CentCom.Server.BanSources;

public class BubberBanParser(DatabaseContext dbContext, BubberBanService banService, ILogger<BubberBanParser> logger)
    : BanParser(dbContext, logger)
{
    protected override Dictionary<string, BanSource> Sources => new()
    {
        { "bubberstation", new BanSource
        {
            Display = "Bubberstation",
            Name = "bubberstation",
            RoleplayLevel = RoleplayLevel.Medium
        } }
    };

    protected override bool SourceSupportsBanIDs => true;
    protected override string Name => "Bubberstation";

    public override async Task<List<Ban>> FetchAllBansAsync()
    {
        Logger.LogInformation("Fetching all bans for Bubberstation...");
        return await banService.GetBansBatchedAsync(Sources["bubberstation"]);
    }

    public override async Task<List<Ban>> FetchNewBansAsync()
    {
        Logger.LogInformation("Fetching new bans for Bubberstation...");
        var recent = await DbContext.Bans
            .Where(x => Sources.Keys.Contains(x.SourceNavigation.Name))
            .OrderByDescending(x => x.BannedOn)
            .Take(5)
            .Include(x => x.JobBans)
            .Include(x => x.SourceNavigation)
            .ToListAsync();

        var foundBans = new List<Ban>();
        var page = 1;
        while (true)
        {
            var bans = await banService.GetBansAsync(page);
            if (bans.Count == 0)
                break;

            foundBans.AddRange(bans.Select(x => x.AsBan(Sources["bubberstation"])));

            // Check for existing bans
            if (foundBans.Any(x => recent.Any(y => y.BanID == x.BanID)))
                break;

            page++;
        }

        return foundBans.DistinctBy(x => x.BanID).ToList();
    }
}