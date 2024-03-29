﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Contest.Data;
using Contest.Models.Result;
using Contest.Models.Score;
using Contest.Web.Constants;
using Contest.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Contest.Web.Common;


namespace Contest.Web.Controllers
{
    [Authorize(Roles = Role.Admin)]
    public class AdminController : Controller
    {
        private readonly KolpiDbContext _context;
        private readonly IConfiguration _configuration;

        public AdminController(KolpiDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<IActionResult> Index()
        {
            var editSettingFinalResult = await _context.Settings.FirstOrDefaultAsync(x => x.Name == Setting.FinalResult);
            var editSettingPeoplesChoice = await _context.Settings.FirstOrDefaultAsync(x => x.Name == Setting.PeoplesChoiceAward);
            var allowFinalResult = editSettingFinalResult?.Value == "1";
            var enablePeoplesChoiceAward = editSettingPeoplesChoice?.Value == "1";
            ViewData["enablePeoplesChoiceAward"] = enablePeoplesChoiceAward;

            if (!allowFinalResult)
                return View("Error", new ErrorViewModel { ErrorCode = "Final Result Disabled", Message = "Final result not disclosed yet to avoid bias and judgement conflicts." });

            List<JudgeScore> judgeScores = await _context.JudgeScores.Where(x => x.Team.CreatedOn.Year == DateTime.Now.Year).Include(x => x.Team.Participants).Include(u => u.KolpiUser).ToListAsync();
            List<JudgeScoreViewModel> judgeScoresViewModels = judgeScores.Select(x => new JudgeScoreViewModel(x)
            {
                Participants = string.Join(",", x.Team.Participants.ToList().Select(s => s.Name))
            }).ToList();

            (IList<(string Code, string Detail, int FinalScore)> allTeamsScoreAdded, List<ParticipantVoteViewModel> allVoteViewModels) peopleChoices = default;

            if (enablePeoplesChoiceAward)
            {
                List<ParticipantVote> allVotes = await _context.ParticipantVotes.Where(x => x.VotedOn.Value.Year == DateTime.Now.Year).ToListAsync();
                List<IdentityUser> allUsers = await _context.Users.ToListAsync();

                var allVoteViewModels = allVotes.Select(x => new ParticipantVoteViewModel(x)
                {
                    UserName = allUsers.FirstOrDefault(y => y.Id == x.UserId)?.UserName
                }).OrderBy(x => x.UserName).ToList();

                IList<(string Code, string Detail)> allTeams = await GetTeamsFormatted();
                IList<(string Code, string Detail, int FinalScore)> allTeamsScoreAdded = new List<(string, string, int)>();
                int finalScoreSum;
                foreach (var (Code, Detail) in allTeams)
                {
                    finalScoreSum = 0;
                    foreach (var vote in allVoteViewModels)
                    {
                        if (Code.Equals(vote.OrderOneTeam))
                        {
                            finalScoreSum += Score.RankOne;
                        }
                        else if (Code.Equals(vote.OrderTwoTeam))
                        {
                            finalScoreSum += Score.RankTwo;
                        }
                        else if (Code.Equals(vote.OrderThreeTeam))
                        {
                            finalScoreSum += Score.RankThree;
                        }
                        else if (Code.Equals(vote.OrderFourTeam))
                        {
                            finalScoreSum += Score.RankFour;
                        }
                        else if (Code.Equals(vote.OrderFiveTeam))
                        {
                            finalScoreSum += Score.RankFive;
                        }
                    }
                    allTeamsScoreAdded.Add((Code, Detail, finalScoreSum));
                }

                peopleChoices = (allTeamsScoreAdded, allVoteViewModels);
            }

            //The weighted average (x) is equal to the sum of the product of the weight (wi) times the data number (xi) divided by the sum of the weights
            List<TeamViewModel> teamsFinalScores = judgeScoresViewModels.GroupBy(x => new { x.Team, x.Theme, x.Participants })
                .Select(g => new TeamViewModel
                {
                    TeamName = g.Key.Team,
                    AveragePlainAverage = g.Average(x => x.AverageScore.Value),
                    AverageBestIdeaScore = g.Average(x => x.BestIdeaScore.Value),
                    AverageBestImplementationScore = g.Average(x => x.BestTechnicalImplementationScore.Value),
                    Theme = g.Key.Theme,
                    Participants = g.Key.Participants
                }).ToList();            

            var finalResult = new FinalResultViewModel
            {
                TeamsScores = teamsFinalScores,
                JudgesScores = judgeScoresViewModels,
                PeoplesChoiceRanks = peopleChoices
            };

            return View(finalResult);
        }

        private async Task<IList<(string Value, string Text)>> GetTeamsFormatted()
        {
            var teams = await _context.Teams.Where(x => x.CreatedOn.Year == DateTime.Now.Year).Include(x => x.Participants).ToListAsync();
            IList<(string Value, string Text)> teamList = teams.Select(t => (t.TeamCode,
                $"{t.TeamName} ({t.Theme.ToString()} - {string.Join(", ", t.Participants.Select(x => x.Name.Split()[0]))} )")).ToList();
            return teamList;
        }

        public IActionResult Error(string errorCode, string message)
        {
            return View(new ErrorViewModel { ErrorCode = errorCode, Message = message });
        }
    }
}
