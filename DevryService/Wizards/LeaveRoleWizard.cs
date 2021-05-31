using DevryService.Core;
using DevryService.Core.Util;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevryService.Wizards
{
    public class LeaveRoleWizardConfig : JoinRoleWizardConfig
    {

    }

    [WizardInfo(Name ="Bouncer Hat",
        Title = "Leave Class(es)",
        IconUrl = "",
        Description = "Select the corresponding number(s) to leave a class")]
    public class LeaveRoleWizard : WizardBase<LeaveRoleWizardConfig>
    {
        public LeaveRoleWizard(CommandContext commandContext) : base(commandContext)
        {
        }

        protected override void Initialize()
        {
            base.Initialize();
        }

        const string AUTHOR_NAME = "Bouncer Hat";
        const string DESCRIPTION = "Select the corresponding number(s) to leave a class";
        const string REACTION_EMOJI = "";
        const string AUTHOR_ICON = "https://vignette.wikia.nocookie.net/harrypotter/images/6/62/Sorting_Hat.png/revision/latest?cb=20161120072849";

        public override CommandConfig DefaultCommandConfig()
        {
            return new CommandConfig
            {
                AuthorName = AUTHOR_NAME,
                Description = DESCRIPTION,
                ReactionEmoji = REACTION_EMOJI,
                AuthorIcon = AUTHOR_ICON,
                IgnoreHelpWizard = false
            };
        }

        public override LeaveRoleWizardConfig DefaultSettings()
        {
            LeaveRoleWizardConfig config = new LeaveRoleWizardConfig();

            config.AuthorName = AUTHOR_NAME;
            config.Headline = "Leave Class(es)";
            config.Description = DESCRIPTION;
            config.AuthorIcon = AUTHOR_ICON;

            config.MessageRequireMention = false;
            config.AcceptAnyUser = false;
            config.BlacklistedRoles = new string[]
            {
                "Senior Moderators", "Admin", "Junior Moderators"
            };

            config.UsesCommand = new WizardToCommandLink
            {
                DiscordCommand = "leave",                
                CommandConfig = DefaultCommandConfig()
            };

            return config;
        }

        protected override async Task ExecuteAsync()
        {
            var lowercased = _options.BlacklistedRoles.Select(x => x.ToLower()).ToList();

            var roles = _channel.Guild.Roles
                .Where(x => !lowercased.Contains(x.Value.Name.ToLower()))
                .OrderBy(x => x.Value.Name)
                .Select(x => x.Value)
                .ToList();

            // We have to do this to update our local cache.. otherwise any roles appied won't appear/disappear
            var member = await _context.Guild.GetMemberAsync(_originalMember.Id);
            
            var memberRoles = member.Roles.Where(x => !lowercased.Contains(x.Name.ToLower()))
                .OrderBy(x => x.Name)
                .ToList();
            
            if(memberRoles.Count == 0)
            {
                await SimpleReply("You don't have any roles for me to remove!", false, false);
                return;
            }

            string message = "Select the number(s) that correspond to the role you wish to remove\n";
            var embed = EmbedBuilder().WithFooter(CANCEL_MESSAGE);

            for (int i = 0; i < memberRoles.Count; i++)
            {
                string name = memberRoles[i].Name;

                if(name.Length < 25)
                {
                    int amount = 25 - name.Length;
                    name = name.PadLeft(amount, ' ');
                }

                embed.AddField((i + 1).ToString(), name, true);
            }
                

            string reply = string.Empty;
            _recentMessage = await WithReply(embed.Build(), (context) => reply = context.Result.Content, true);

            string[] parameters = null;

            try
            {
                parameters = reply.Replace(",", " ").Split(" ");
            }
            catch
            {
                await CleanupAsync();
                return;
            }

            List<string> removed = new List<string>();
            await _context.TriggerTypingAsync();
            foreach(var selection in parameters)
            {
                if(int.TryParse(selection, out int index))
                {
                    index -= 1;
                    if(index < 0 || index >= memberRoles.Count)
                        continue;
                    
                    await member.RevokeRoleAsync(memberRoles[index]);
                    removed.Add(memberRoles[index].Name);

                    await Task.Delay(500);
                }
            }

            await _context.TriggerTypingAsync();
            await CleanupAsync();

            if (removed.Count > 0)
            {
                embed = EmbedBuilder().WithDescription($"{member.Mention}\nThe following roles were removed:");

                for (int i = 0; i < removed.Count; i++)
                    embed.AddField((i + 1).ToString(), removed[i], true);

                await SimpleReply(embed.Build(), false, false);
            }
            else
                await SimpleReply(EmbedBuilder().WithDescription($"{member.Mention},\nNo changes were made").Build(), false, false);

            
        }
    }
}
