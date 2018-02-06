using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using System.IO;
using System.Configuration;

using Dota2Api;
using Dota2Api.Enums;
using Dota2Api.ApiClasses;

namespace GetDotAData
{
    class Program
    {
        public const string OUTPUT_FILE = "output.dat";
        private const int MATCH_COUNT = 1000;
        private const long ACCOUNT_ID = 409640815;

        //made by /u/dipique with <3 for /u/GeneralGaylord
        static void Main(string[] args)
        {
            //Put your API key in the app.config (get one here: https://steamcommunity.com/dev/apikey)
            using (ApiHandler handler = new ApiHandler(ConfigurationManager.AppSettings["Dota2ApiKey"]))
            {
                //Start by getting a list of heroes
                var tH = handler.GetHeroes();
                tH.Wait();
                var heroes = tH.Result.Heroes;
                var heroIDs = heroes.Select(h => h.Id);
                WaitOneSecond(); //to avoid erroring out API

                //get first match ID (well, last actually)
                var t = handler.GetMatchHistory(gameMode: GameMode.AllPick, matchesRequested: "1");
                t.Wait();
                long startID = t.Result.Matches.First().MatchId;
                WaitOneSecond(); //to avoid erroring out API

                //make sure we have enough data; loop with more if < 500 matches
                var matchList = new List<MatchInfo>();
                while (matchList.Count() < MATCH_COUNT)
                {
                    //It'd be nice to not do this, but the API is stupid and will only give access to the most recent 500 matches
                    //otherwise. So we run it for each hero instead and add up all the data.
                    foreach (var id in heroIDs)
                    {
                        //Get the list of matches that meet our current search criteria
                        Console.WriteLine($"Search: heroID {id}");
                        var matchesTask = handler.GetMatchHistory(startID.ToString(),
                                                                  heroId: id,
                                                                  gameMode: GameMode.AllPick,
                                                                  accountId: ACCOUNT_ID.ToString(),
                                                                  matchesRequested: "25");
                        matchesTask.Wait();

                        //Filter the results to ones we're interested in
                        var matches = matchesTask.Result.Matches
                                                 .Where(m => m.LobbyType == LobbyType.RankedMatchmaking)    //pubs only (no league games, siltbreaker, etc.)
                                                 .Where(m => m.Players.Count() == 10)                       //no 1v1s
                                                 .Where(m => !matchList.Any(ml => ml.MatchID == m.MatchId)) //prevent duplicate entries;
                                                 .ToList();

                        //cache the data we've colleceted as MatchInfo objects
                        Console.WriteLine($"Found {matches.Count()} matches to parse");
                        WaitOneSecond(); //to avoid erroring out API
                        matchList.AddRange(matches.Select(m => new MatchInfo(handler, m.MatchId, heroes)));
                    }
                    //when we loop for new data (meaning games farther back), this makes sure we fetch data older than the data we currently have
                    startID = matchList.Min(m => m.MatchID) - 1;
                }

                //Write the results to a tab-delimited file easy to copy & paste into Excel
                File.WriteAllLines(OUTPUT_FILE, matchList.Select(m => m.ToString()).ToArray());              

            } //disposes handler

            Console.WriteLine("Done.");
            Console.ReadKey(); //so it doesn't just close on you
        }

        /// <summary>
        /// A class used to cache the match data for writing later on
        /// </summary>
        public class MatchInfo
        {
            public long PlayerID { get; set; } = 409640815;
            public string PlayerTeam { get; set; }
            public string PlayerHero { get; set; }
            public string MatchDate { get; set; }

            public long MatchID { get; set; }
            public int Duration { get; set; }
            public bool Eliminate => !string.IsNullOrEmpty(EliminateReason);
            public string EliminateReason { get; set; }
            public string Exception { get; set; }
            public string Winner { get; set; }

            private const int MIN_DURATION = 600; // 10 minutes

            /// <summary>
            /// Creating the matchinfo object actually does another API call to get the match details. Some
            /// matches are excluded based onabandons or length, and it'd be easy to add other criteria here
            /// as well.
            /// </summary>
            /// <param name="handler"></param>
            /// <param name="matchID"></param>
            /// <param name="skill"></param>
            public MatchInfo(ApiHandler handler, long matchID, List<Hero> heroes)
            {
                MatchID = matchID;

                try
                {
                    var tD = handler.GetDetailedMatch(matchID.ToString());
                    tD.Wait();
                    var result = tD.Result;
                    Duration = result.Duration;
                    Winner = result.WinningFaction.ToString();
                    MatchDate = result.StartTime.ToString();

                    if (PlayerID != 0)
                    {
                        PlayerTeam = result.Players.First(p => p.AccountId == PlayerID).Faction.ToString();
                        PlayerHero = heroes.FirstOrDefault(h => h.Id == result.Players.First(p => p.AccountId == PlayerID).HeroId).Name;
                    }

                    if (result.Players.Any(p => p.LeaverStatus != LeaverStatus.DotaLeaverNone))
                    {
                        EliminateReason = "Abandon/disconnect";
                    }
                    else if (Duration <= MIN_DURATION)
                    {
                        EliminateReason = $"Shorter than min duration ({MIN_DURATION} seconds)";
                    }
                }
                catch (Exception e) { Exception = e.Message; }

                var exMsg = string.IsNullOrEmpty(Exception) ? string.Empty : $" - Exception: {Exception}";
                Console.WriteLine($"Recorded match {MatchID}{exMsg}");

                WaitOneSecond(); //to avoid erroring out API
            }

            /// <summary>
            /// This is used when we actually write to a file.
            /// </summary>
            /// <returns></returns>
            public override string ToString()
            {
                return $"{MatchID}\t{MatchDate}\t{Winner}\t{PlayerTeam}\t{PlayerHero}\t{Duration}\t{Eliminate}\t{EliminateReason}\t{Exception}";
            }
        }

        public static void WaitOneSecond()
        {
            //force 1 per second
            var delay = Task.Delay(1000);
            delay.Wait();
        }

        public enum Skill
        {
            Normal = 1,
            High = 2,
            VeryHigh = 3
        }
    }
}
