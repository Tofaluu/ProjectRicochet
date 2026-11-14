using System.Text;

namespace BackendServer.Services
{
    /// <summary>The private stats page (<c>/api/stats?key=...</c>): a plain table, no scripts or outside resources.</summary>
    public static class UsagePage
    {
        public static string Render(UsageSummary summary)
        {
            IReadOnlyList<UsageDay> days = summary.Days;
            var html = new StringBuilder();
            html.Append("""
                <!doctype html><html lang="en"><head><meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1"><meta name="robots" content="noindex">
                <title>Ricochet usage</title>
                <style>
                  body { font-family: system-ui, sans-serif; margin: 24px 16px; background: #1b1d2a; color: #e8e6f0; }
                  h1 { font-size: 20px; } p { color: #a9a6b8; font-size: 14px; }
                  table { border-collapse: collapse; font-size: 14px; }
                  th, td { padding: 6px 10px; text-align: right; border-bottom: 1px solid #343748; }
                  th:first-child, td:first-child { text-align: left; }
                  th { color: #a9a6b8; font-weight: 600; }
                  tr.total td, tr.all td { font-weight: 700; }
                  tr.total td { border-top: 2px solid #4a4e66; }
                  .wrap { overflow-x: auto; }
                  .online { font-size: 16px; color: #e8e6f0; }
                </style></head><body>
                <h1>Ricochet usage</h1>
                """);
            html.Append("<p class=\"online\">Online now: <b>").Append(summary.OnlineNow).Append("</b></p>\n");
            html.Append("""
                <p>Days in Toronto time, newest first. Devices marked internal (?internal=1) are left out.
                Players = different devices; new = first seen that day. Online = the game open and visible in the last
                2 minutes; peak = the most at once that day (blank before it was tracked). Sign-ins = signing in to an
                account that already existed (new accounts are counted on their own). In the totals, players = different
                devices over the whole span (someone back on 5 days counts once) and peak = the busiest day's.</p>
                <div class="wrap"><table>
                <tr><th>Day</th><th>Peak online</th><th>Players</th><th>New</th><th>Returning</th><th>Visits</th><th>Practice solves</th>
                <th>Lobby joins</th><th>Daily opened</th><th>Daily solved</th>
                <th>Tutorial started</th><th>Tutorial finished</th><th>Tutorial skipped</th><th>New accounts</th><th>Sign-ins</th></tr>
                """);
            foreach (UsageDay d in days)
                Row(html, d.Day.ToString("ddd MMM d"), d.PeakOnline > 0 ? d.PeakOnline : null, d.Players, d.NewPlayers, d.ReturningPlayers, d.Visits, d.PracticeSolves,
                    d.LobbyJoins, d.DailyOpens, d.DailySolves, d.TutorialStarts, d.TutorialFinishes, d.TutorialSkips,
                    d.NewAccounts, d.SignIns);
            Total(html, $"Last {days.Count} days", summary.Recent, "total");
            Total(html, "All time", summary.AllTime, "all");
            html.Append("</table></div></body></html>");
            return html.ToString();
        }

        private static void Total(StringBuilder html, string label, UsageTotals t, string cssClass)
        {
            html.Append("<tr class=\"").Append(cssClass).Append("\">");
            Row(html, label, t.PeakOnline > 0 ? t.PeakOnline : null, t.Players, t.NewPlayers, null, t.Visits, t.PracticeSolves,
                t.LobbyJoins, t.DailyOpens, t.DailySolves, t.TutorialStarts, t.TutorialFinishes, t.TutorialSkips,
                t.NewAccounts, t.SignIns, open: false);
        }

        private static void Row(StringBuilder html, string label, int? peak, int? players, int? newPlayers, int? returning, int visits,
            int practice, int lobbies, int dailyOpens, int dailySolves, int tutorialStarts, int tutorialFinishes, int tutorialSkips,
            int newAccounts, int signIns, bool open = true)
        {
            if (open)
                html.Append("<tr>");
            html.Append("<td>").Append(label).Append("</td>");
            foreach (int? v in new[] { peak, players, newPlayers, returning, visits, practice, lobbies, dailyOpens, dailySolves, tutorialStarts,
                tutorialFinishes, tutorialSkips, newAccounts, signIns })
                html.Append("<td>").Append(v?.ToString() ?? "").Append("</td>");
            html.Append("</tr>\n");
        }
    }
}
