using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using System.IO;
using System.Configuration;

using Dota2Api;
using Dota2Api.Enums;

namespace GetDotAData
{
    class Program
    {
        public const string OUTPUT_FILE = "output.dat";

        //made by /u/dipique with <3 for /u/GeneralGaylord
        static void Main(string[] args)
        {
            //Put your API key in the app.config (get one here: https://steamcommunity.com/dev/apikey)
            using (ApiHandler handler = new ApiHandler(ConfigurationManager.AppSettings["Dota2ApiKey"]))
            {
                //Start by getting a list of heroes
                var tH = handler.GetHeroes();
                tH.Wait();
                var heroIDs = tH.Result.Heroes.Select(h => h.Id);
                WaitOneSecond(); //to avoid erroring out API

                //get first match ID (well, last actually)
                var t = handler.GetMatchHistory(gameMode: GameMode.AllPick, matchesRequested: "1");
                t.Wait();
                long startID = t.Result.Matches.First().MatchId;
                WaitOneSecond(); //to avoid erroring out API

                //run skills from high to low and skip segments where we don't have high skill data to
                //compare with (this makes it run faster but does not change the shape of the data)
                var skillList = from int s in Enum.GetValues(typeof(Skill))
                                orderby s descending
                                select s;

                //make sure we have enough data; loop with more if < 500 matches
                var matchList = new List<MatchInfo>();
                while (matchList.Count() < 500)
                {
                    //It'd be nice to not do this, but the API is stupid and will only give access to the most recent 500 matches
                    //otherwise. So we run it for each hero instead and add up all the data.
                    foreach (var id in heroIDs)
                    {
                        //Oddly, the skill level isn't in the response, so we need to run the query by skill level
                        //and cache it for later.
                        int vh = 0; int h = 0;
                        foreach (var skill in skillList)
                        {
                            //the whole purpose of this is comparison, so if there aren't any matches in the previous skill level, skip this hero
                            if ((Skill)skill == Skill.High && vh == 0) continue;
                            if ((Skill)skill == Skill.Normal && h == 0) continue;

                            //Get the list of matches that meet our current search criteria
                            Console.WriteLine($"Search: {skill} - heroID {id}");
                            var matchesTask = handler.GetMatchHistory(startID.ToString(), 
                                                                      skill: skill.ToString(), 
                                                                      heroId: id, 
                                                                      gameMode: GameMode.AllPick,
                                                                      matchesRequested: "25");
                            matchesTask.Wait();

                            //Filter the results to ones we're interested in
                            var matches = matchesTask.Result.Matches
                                                     .Where(m => m.LobbyType == LobbyType.PublicMatchmaking)    //pubs only (no league games, siltbreaker, etc.)
                                                     .Where(m => m.Players.Count() == 10)                       //no 1v1s
                                                     .Where(m => !matchList.Any(ml => ml.MatchID == m.MatchId)) //prevent duplicate entries;
                                                     .ToList(); 

                            //track the number in each skill category, making this loop useless (but allowing us to skip
                            //segments where we don't have data to compare and limit our API calls which take fucking forever)
                            if (skill == (int)Skill.VeryHigh) vh = matches.Count();
                            if (skill == (int)Skill.High) h = matches.Count();

                            //cache the data we've colleceted as MatchInfo objects
                            Console.WriteLine($"Found {matches.Count()} matches to parse");
                            WaitOneSecond(); //to avoid erroring out API
                            matchList.AddRange(matches.Select(m => new MatchInfo(handler, m.MatchId, (Skill)skill)));
                        }
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
            public long MatchID { get; set; }
            public int Duration { get; set; }
            public Skill SkillLevel { get; set; }
            public bool Eliminate => !string.IsNullOrEmpty(EliminateReason);
            public string EliminateReason { get; set; }
            public string Exception { get; set; }

            private const int MIN_DURATION = 600; // 10 minutes

            /// <summary>
            /// Creating the matchinfo object actually does another API call to get the match details. Some
            /// matches are excluded based onabandons or length, and it'd be easy to add other criteria here
            /// as well.
            /// </summary>
            /// <param name="handler"></param>
            /// <param name="matchID"></param>
            /// <param name="skill"></param>
            public MatchInfo(ApiHandler handler, long matchID, Skill skill)
            {
                MatchID = matchID;
                SkillLevel = skill;

                try
                {
                    var tD = handler.GetDetailedMatch(matchID.ToString());
                    tD.Wait();
                    var result = tD.Result;
                    Duration = result.Duration;
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
                return $"{MatchID}\t{Duration}\t{SkillLevel}\t{Eliminate}\t{EliminateReason}\t{Exception}";
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
