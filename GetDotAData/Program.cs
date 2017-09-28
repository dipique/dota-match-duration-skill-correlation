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

        static void Main(string[] args)
        {
            using (ApiHandler handler = new ApiHandler(ConfigurationManager.AppSettings["Dota2ApiKey"]))
            {
                var tH = handler.GetHeroes();
                tH.Wait();
                var heroIDs = tH.Result.Heroes.Select(h => h.Id);
                WaitOneSecond(); //to avoid erroring out API

                //get first match ID (well, last actually)
                var t = handler.GetMatchHistory(gameMode: GameMode.AllPick, matchesRequested: "1");
                t.Wait();
                long startID = t.Result.Matches.First().MatchId;
                WaitOneSecond(); //to avoid erroring out API

                var matchList = new List<MatchInfo>();
                var skillList = from int s in Enum.GetValues(typeof(Skill))
                                orderby s descending
                                select s;
                while (matchList.Count() < 500)
                {
                    foreach (var id in heroIDs)
                    {
                        int vh = 0; int h = 0;
                        foreach (var skill in skillList)
                        {
                            //the whole purpose of this is comparison, so if there aren't any matches in the previous skill level, skip this hero
                            if ((Skill)skill == Skill.High && vh == 0) continue;
                            if ((Skill)skill == Skill.Normal && h == 0) continue;

                            Console.WriteLine($"Search: {skill} - heroID {id}");
                            var matchesTask = handler.GetMatchHistory(startID.ToString(), 
                                                                      skill: skill.ToString(), 
                                                                      heroId: id, 
                                                                      gameMode: GameMode.AllPick,
                                                                      matchesRequested: "25");
                            matchesTask.Wait();
                            var matches = matchesTask.Result.Matches
                                                     .Where(m => m.LobbyType == LobbyType.PublicMatchmaking)
                                                     .Where(m => m.Players.Count() == 10)
                                                     .Where(m => !matchList.Any(ml => ml.MatchID == m.MatchId)) //prevent duplicate entries;
                                                     .ToList(); 

                            //track the number in each skill category, making this loop useless
                            if (skill == (int)Skill.VeryHigh) vh = matches.Count();
                            if (skill == (int)Skill.High) h = matches.Count();

                            Console.WriteLine($"Found {matches.Count()} matches to parse");
                            WaitOneSecond(); //to avoid erroring out API
                            matchList.AddRange(matches.Select(m => new MatchInfo(handler, m.MatchId, (Skill)skill)));
                        }
                    }
                    startID = matchList.Min(m => m.MatchID) - 1;
                }

                //Now we have the list, so let's get the durations
                File.WriteAllLines(OUTPUT_FILE, matchList.Select(m => m.ToString()).ToArray());              
            } //disposes handler
            Console.WriteLine("Done.");
            Console.ReadKey();
        }

        public class MatchInfo
        {
            public long MatchID { get; set; }
            public int Duration { get; set; }
            public Skill SkillLevel { get; set; }
            public bool Eliminate => !string.IsNullOrEmpty(EliminateReason);
            public string EliminateReason { get; set; }
            public string Exception { get; set; }

            private const int MIN_DURATION = 600; // 10 minutes

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
