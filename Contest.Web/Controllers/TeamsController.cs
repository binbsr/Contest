﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Contest.Data;
using Contest.Models.Score;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Contest.Utilities;
using Microsoft.AspNetCore.Authorization;
using Contest.Web.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Rendering;
using Contest.Web.Models;
using Contest.Web.Common;
using Contest.Enums;

namespace Contest.Web.Controllers
{
    [Authorize(Roles = Role.AdminOrParticipant)]
    public class TeamsController : Controller
    {
        private readonly KolpiDbContext _context;
        public IConfiguration _Config { get; }

        public TeamsController(KolpiDbContext context, IConfiguration _config)
        {
            _context = context;
            _Config = _config;
        }

        public async Task<IActionResult> Index()
        {
            var teams = await _context.Teams
                .Include(team => team.Participants)
                .Where(x => x.CreatedOn.Year == DateTime.Now.Year)
                .OrderBy(x => x.TeamName)
                .ToListAsync();
            var currentUserName = User.FindFirst(ClaimTypes.Name)?.Value;
            var teamViewModels = teams.Select(x => new TeamViewModel(x, currentUserName));
            return View(teamViewModels);
        }

        public async Task<IActionResult> LastYearParticipants()
        {
            var teams = await _context.Teams
                .Include(team => team.Participants)
                .Where(x => x.CreatedOn.Year == DateTime.Now.Year - 1)
                .OrderBy(x => x.TeamName)
                .ToListAsync();
            var currentUserName = User.FindFirst(ClaimTypes.Name)?.Value;
            var teamViewModels = teams.Select(x => new TeamViewModel(x, currentUserName));
            return View(teamViewModels);
        }

        public async Task<IActionResult> Analytics()
        {
            var teams = await _context.Teams
                .Include(team => team.Participants)
                .Where(x => x.CreatedOn.Year == DateTime.Now.Year)
                .ToListAsync();
            
            var analytics = new TeamAnalyticsViewModel
            {
                TotalTeams = teams.Count,
                TotalParticipants = teams.Sum(x => x.Participants.Count),                
                AllParticipants = teams.SelectMany(x => x.Participants.Select(y => TeamViewModel.SerializeParticipant(y))).ToList(),
                AllTeams = teams.Select(x => TeamViewModel.SerializeTeam(x)).ToList(),
                TeamRequirements = teams.Select(x => (x.TeamName, x.ITRequirements, x.OtherRequirements)).ToList()
            };            

            return View(analytics);
        }

        public async Task<IActionResult> Create()
        {
            var teamRegistrationConfig = await _context.Settings.FirstOrDefaultAsync(x => x.Name == Setting.TeamRegistration);
            bool teamRegistrationDisabled = teamRegistrationConfig.Value == "0";

            if(teamRegistrationDisabled)
                return RedirectToAction("Error", "Home", new ErrorViewModel { ErrorCode = "Registration", Message = "Team registration disabled." });

            ViewData["locations"] = FetchEventLocationSelectItemList();
            return View();
        }


        // Specific properties only: To protect from overposting attacks
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TeamViewModel teamViewModel)
        {   
            if (ModelState.IsValid)
            {
                if (!ParticipantsEnteredCorrectly(teamViewModel.Participants))
                {
                    ViewData["locations"] = FetchEventLocationSelectItemList();
                    return View(teamViewModel);
                }
                //Raw participants in text are good to deserialize
                var participantsDtos = TeamViewModel.DeserializeParticipants(teamViewModel.Participants);

                List<Participant> partcipantsOnDb = _context.Participants
                    .Where(x => x.Team.CreatedOn.Year == DateTime.Now.Year).ToList();

                var alreadyTakenParticipants = partcipantsOnDb.Intersect(participantsDtos, ComplexTypeComparer<Participant>.Create(x => x.OfficeMail));
                if (alreadyTakenParticipants.Any())
                {
                    ModelState.AddModelError("Participants", $"Participants submitted are already involved with other teams: {string.Join(", ", alreadyTakenParticipants.Select(x => x.Name))}. Note: individual participants are distinguished by their office Email.");
                    ViewData["locations"] = FetchEventLocationSelectItemList();
                    return View(teamViewModel);
                }

                //Autogenerate properties at creation time
                teamViewModel.CreatedBy = User.FindFirst(ClaimTypes.Name)?.Value ?? "";
                teamViewModel.CreatedOn = DateTime.Now;
                teamViewModel.TeamCode = Regex.Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "[/+=]", "");

                _context.Add(new Team(teamViewModel, participantsDtos));
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(teamViewModel);
        }

        public async Task<IActionResult> Edit(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return NotFound();
            }

            var team = await _context.Teams.SingleAsync(t => t.TeamCode == identifier);

            if (team == null)
            {
                return NotFound();
            }

            //Fetch participants explicitly
            await _context.Entry(team).Collection(t => t.Participants).LoadAsync();

            var teamViewModel = new TeamViewModel(team);
            ViewData["locations"] = FetchEventLocationSelectItemList();

            return View(teamViewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string teamCode, TeamViewModel teamViewModel)
        {
            if (teamCode != teamViewModel.TeamCode)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                if (!ParticipantsEnteredCorrectly(teamViewModel.Participants))
                {
                    ViewData["locations"] = FetchEventLocationSelectItemList();

                    return View(teamViewModel);
                }

                //Raw participants in text are good to deserialize
                var participantsDtos = TeamViewModel.DeserializeParticipants(teamViewModel.Participants);
                var team = new Team(teamViewModel, participantsDtos);

                var oldParticipants = _context.Participants.Where(x => x.Team.Id == team.Id);
                _context.Participants.RemoveRange(oldParticipants);

                try
                {
                    _context.Attach(team);

                    _context.Entry(team).Property(p => p.TeamName).IsModified = true;
                    _context.Entry(team).Property(p => p.ProblemStatement).IsModified = true;
                    _context.Entry(team).Property(p => p.Theme).IsModified = true;
                    _context.Entry(team).Collection(p => p.Participants).IsModified = true;
                    _context.Entry(team).Property(p => p.ITRequirements).IsModified = true;
                    _context.Entry(team).Property(p => p.OtherRequirements).IsModified = true;
                    _context.Entry(team).Property(p => p.RepoUrl).IsModified = true;
                    _context.Entry(team).Property(p => p.Location).IsModified = true;

                    //Only flag as modified if user uploads something
                    if (team?.Avatar?.Length > 0)
                        _context.Entry(team).Property(p => p.Avatar).IsModified = true;

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!TeamViewModelExists(teamViewModel.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }

            ViewData["locations"] = FetchEventLocationSelectItemList();

            return View(teamViewModel);
        }

        public async Task<IActionResult> Delete(string identifier)
        {
            if (identifier == null)
            {
                return NotFound();
            }

            var team = await _context.Teams.SingleAsync(m => m.TeamCode == identifier);
            if (team == null)
            {
                return NotFound();
            }

            //Fetch participants explicitly
            await _context.Entry(team).Collection(t => t.Participants).LoadAsync();

            return View(new TeamViewModel(team));
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string teamCode)
        {
            var team = await _context.Teams.FirstOrDefaultAsync(x => x.TeamCode.Equals(teamCode));
            await _context.Entry(team).Collection(t => t.Participants).LoadAsync();

            _context.Teams.Remove(team);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool TeamViewModelExists(int id)
        {
            return _context.Teams.Any(e => e.Id == id);
        }

        public IActionResult About()
        {
            return View();
        }

        private bool ParticipantsEnteredCorrectly(string participantsPosted)
        {
            var participants = participantsPosted.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var participant in participants)
            {
                var properties = participant.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                //User required to enter exact 3 comma separated values for each participant
                if (properties.Length != 3)
                {
                    ModelState.AddModelError("Participants", $"Please enter participants exactly as example says. Each participant in new line and have exactly 3 values separated by commas. Btw, '{participant}' causing this error.");
                    return false;
                }
            }

            return true;
        }

        private IEnumerable<SelectListItem> FetchEventLocationSelectItemList()
        {
            var locations = _Config.GetSection("Locations")?.Get<List<string>>();
            return locations?.Select(x => new SelectListItem { Text = x, Value = x }).ToList();
        }
    }
}
