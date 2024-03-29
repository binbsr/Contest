﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Contest.Enums;
using Microsoft.AspNetCore.Http;

namespace Contest.Models.Score
{
    public class TeamViewModel
    {
        public TeamViewModel()
        {
        }

        public TeamViewModel(Team team, string loggedInUser = "")
        {
            Id = team.Id;
            TeamCode = team.TeamCode;
            TeamName = team.TeamName;
            Theme = team.Theme;
            ProblemStatement = team.ProblemStatement;
            ITRequirements = team.ITRequirements;
            OtherRequirements = team.OtherRequirements;
            Participants = SerializeParticipants(team.Participants);
            CreatedBy = team.CreatedBy ?? "";
            CreatedOn = team.CreatedOn;
            CreatedByFormatted = $"{CreatedBy} [On {CreatedOn:MMM dd hh:mm tt}]";
            IsCreatedByCurrentUser = CreatedBy.Equals(loggedInUser);
            RepoUrl = team.RepoUrl;
            Location = team.Location;

            //Convert byte array as base64 encoded string
            if (team?.Avatar?.Length > 0)
            {
                string avatarDataUrl;
                string avatarBase64 = Convert.ToBase64String(team.Avatar);
                avatarDataUrl = $"data:image/png;base64,{avatarBase64}";
                AvatarDataUrl = avatarDataUrl;
            }
        }

        public int Id { get; set; }
        public string TeamCode { get; set; }

        [Required(ErrorMessage = "Team name is required to add your team to this event")]
        [Display(Name = "Team Name")]
        public string TeamName { get; set; }

        [Display(Name = "Team Avatar")]
        public IFormFile Avatar { get; set; }
        public string AvatarDataUrl { get; set; }

        [Required, EnumDataType(typeof(Theme))]
        public Theme Theme { get; set; } = Theme.OpenIdea;

        [Required(ErrorMessage = "Provide your team's problem statement summary")]
        [DataType(DataType.MultilineText), Display(Name = "Problem Statement")]
        public string ProblemStatement { get; set; }

        [Display(Name = "IT Requirements")]
        public string ITRequirements { get; set; }

        [Display(Name = "Other Requirements")]
        public string OtherRequirements { get; set; }

        public string Location { get; set; }

        [Display(Name = "Team Registered By")]
        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }

        [Display(Name = "Git Repository")]
        public string RepoUrl { get; set; }

        //Score each team got in each award category
        public float? AveragePlainAverage { get; internal set; }
        public float AverageBestIdeaScore { get; set; }
        public float AverageBestImplementationScore { get; set; }
        public float AveragePeoplesChoiceScore { get; set; }

        public string CreatedByFormatted { get; set; }
        public bool IsCreatedByCurrentUser { get; set; }
        
        [Required(ErrorMessage = "Enter your participants in provided format.")]
        [DataType(DataType.MultilineText), Display(Name = "Participants")]
        public string Participants { get; set; } = @"Bishnu Rawal, i82287, R&D1\n\rNiraj Shah (Team Lead), i65001,R&D1";

        public static string SerializeParticipants(IEnumerable<Participant> participants) =>
            string.Join(Environment.NewLine, participants
                            ?.Select(x => SerializeParticipant(x)));

        public static string SerializeParticipant(Participant participant) =>
            $"{participant.Name}, {participant.Inumber}, {participant.Department}";

        public static string SerializeTeam(Team team) =>
            $"{team.TeamName}, {team.TeamCode}, {team.Participants?.FirstOrDefault(x => x.IsTeamLead)?.Name ?? "No Lead Assigned"}, {team.Location}";

        public static IList<Participant> DeserializeParticipants(string participants) =>
            participants?.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => new Participant(x.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))).ToList();
    }
}
