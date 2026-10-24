using System.Text;

namespace BackendServer.Services
{
    /// <summary>The private stats page (<c>/api/stats?key=...</c>): a plain table, no scripts or outside resources.</summary>
    public static class UsagePage
    {
        public static string Render(IReadOnlyList<UsageDay> days)
        {
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
                  tr.total td { font-weight: 700; border-top: 2px solid #4a4e66; }
                  .wrap { overflow-x: auto; }
                </style></head><body>
                <h1>Ricochet usage</h1>
                <p>Days in Toronto time, newest first. Devices marked internal (?internal=1) are left out.
                Players = different devices; new = first seen that day.</p>
                <div class="wrap"><table>
                <tr><th>Day</th><th>Players</th><th>New</th><th>Returning</th><th>Visits</th><th>Practice solves</th>
                <th>Lobby joins</th><th>Daily opened</th><th>Daily solved</th>
                <th>Tutorial started</th><th>Tutorial finished</th><th>Tutorial skipped</th></tr>
                """);
            foreach (UsageDay d in days)
                Row(html, d.Day.ToString("ddd MMM d"), d.Players, d.NewPlayers, d.ReturningPlayers, d.Visits, d.PracticeSolves,
                    d.LobbyJoins, d.DailyOpens, d.DailySolves, d.TutorialStarts, d.TutorialFinishes, d.TutorialSkips);
            html.Append("<tr class=\"total\">");
            Row(html, "Total (new players)", null, days.Sum(d => d.NewPlayers), null, days.Sum(d => d.Visits),
                days.Sum(d => d.PracticeSolves), days.Sum(d => d.LobbyJoins), days.Sum(d => d.DailyOpens), days.Sum(d => d.DailySolves),
                days.Sum(d => d.TutorialStarts), days.Sum(d => d.TutorialFinishes), days.Sum(d => d.TutorialSkips), open: false);
            html.Append("</table></div></body></html>");
            return html.ToString();
        }

        private static void Row(StringBuilder html, string label, int? players, int? newPlayers, int? returning, int visits,
            int practice, int lobbies, int dailyOpens, int dailySolves, int tutorialStarts, int tutorialFinishes, int tutorialSkips,
            bool open = true)
        {
            if (open)
                html.Append("<tr>");
            html.Append("<td>").Append(label).Append("</td>");
            foreach (int? v in new[] { players, newPlayers, returning, visits, practice, lobbies, dailyOpens, dailySolves, tutorialStarts,
                tutorialFinishes, tutorialSkips })
                html.Append("<td>").Append(v?.ToString() ?? "").Append("</td>");
            html.Append("</tr>\n");
        }
    }
}
