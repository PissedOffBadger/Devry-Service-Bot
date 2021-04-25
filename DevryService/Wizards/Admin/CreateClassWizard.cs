using DevryService.Core;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevryService.Wizards.Admin
{
    public class CreateClassWizardConfig : WizardConfig
    {
        /// <summary>
        /// Names of channels that should be loaded
        /// </summary>
        public List<string> ChannelNames { get; set; } = new List<string>();

        /// <summary>
        /// Additional roles that shall gain access to the class
        /// </summary>
        public List<string> AdditionalRoles { get; set; } = new List<string>();

        /// <summary>
        /// Number of standard voice channels to make for this class
        /// </summary>
        public int NumberOfVoiceChannels { get; set; } = 3;
    }

    public class CreateClassWizard : WizardBase<CreateClassWizardConfig>
    {
        /// <summary>
        /// Permissions for allowed users/roles
        /// </summary>
        Permissions ALLOWED =
            Permissions.AccessChannels |
            Permissions.AddReactions |
            Permissions.EmbedLinks |
            Permissions.ReadMessageHistory |
            Permissions.SendMessages |
            Permissions.SendTtsMessages |
            Permissions.Speak |
            Permissions.UseVoice |
            Permissions.UseVoiceDetection |
            Permissions.AttachFiles;

        /// <summary>
        /// Permissions for denied users
        /// </summary>
        Permissions DENIED = Permissions.AccessChannels |
            Permissions.AddReactions |
            Permissions.Administrator |
            Permissions.AttachFiles |
            Permissions.BanMembers |
            Permissions.ChangeNickname |
            Permissions.CreateInstantInvite |
            Permissions.DeafenMembers |
            Permissions.EmbedLinks |
            Permissions.KickMembers |
            Permissions.ManageChannels |
            Permissions.ManageEmojis |
            Permissions.ManageGuild |
            Permissions.ManageMessages |
            Permissions.ManageRoles |
            Permissions.ManageNicknames |
            Permissions.ManageWebhooks |
            Permissions.MentionEveryone |
            Permissions.MoveMembers |
            Permissions.MuteMembers |
            Permissions.ReadMessageHistory |
            Permissions.SendMessages |
            Permissions.SendTtsMessages |
            Permissions.Speak |
            Permissions.UseExternalEmojis |
            Permissions.UseVoiceDetection;


        string _courseCategory = string.Empty;
        string _courseNumber = string.Empty;
        string _courseTitle = string.Empty;

        List<DiscordRole> additionalRoles = new List<DiscordRole>();
        DiscordRole everyone;

        public CreateClassWizard(CommandContext commandContext) : base(commandContext)
        {
        }

        protected override void Initialize()
        {
            base.Initialize();
            List<string> settingRoles = new List<string>();

            foreach(string name in settingRoles)
            {
                try
                {
                    DiscordRole role = _channel.Guild.Roles.FirstOrDefault(x => x.Value.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).Value;

                    if (role != null)
                        additionalRoles.Add(role);
                }
                catch
                {
                    logger?.LogError($"Unable to locate role '{name}'");
                }
            }


            // Get the everyone role
            everyone = _channel.Guild.Roles.FirstOrDefault(x => x.Value.Name.ToLower().Contains("everyone")).Value;
        }

        async Task<int> GetSortPosition()
        {
            var channels = await _channel.Guild.GetChannelsAsync();
            channels = channels.Where(x => x.Type == ChannelType.Category).ToList();

            int startingIndex = 0;
            int actualPosition = 0;
            /*
             * We just want to know where the first course with this course identifier / number lies
             * our server is not necessarily in alphabetical order in the beginning so we can't rely on that.
             */ 

            for(int i = 0; i < channels.Count; i++)
            {
                if(channels[i].Name.ToLower().StartsWith(_courseCategory.ToLower()))
                {
                    startingIndex = i;
                    actualPosition = channels[i].Position;
                    break;
                }
            }

            // Attempt to grab all classes that mathc our course category.
            // We shall use this to determine where to place our new class
            var groups = channels.Where(x => x.Name.ToLower().StartsWith(_courseCategory.ToLower()))
                .Select(x => x.Name.ToLower().Replace("-", " ").Split(" "));

            List<string> names = new List<string>();
            // the second item should be the course number, if this does not exist then oh well
            foreach (var array in groups)
                if (array.Length > 1)
                    names.Add(array[1]);

            names.Add(_courseNumber);

            names.Sort();

            return startingIndex + names.IndexOf(_courseNumber);
        }

        async Task<(bool, string)> CreateClass(CommandContext context)
        {
            string errorMessage = string.Empty;
            bool success = true;

            DiscordChannel categoryChannel = await _channel.Guild.CreateChannelAsync($"{_courseCategory} {_courseNumber} {_courseTitle}", ChannelType.Category);
            DiscordRole categoryRole = await _channel.Guild.CreateRoleAsync($"{_courseCategory} {_courseNumber}", ALLOWED);

            // if you do not modify the position - it will end up at the bottom of the channel list
            await categoryChannel.ModifyPositionAsync(await GetSortPosition());

            await categoryChannel.AddOverwriteAsync(categoryRole, ALLOWED, Permissions.Administrator);
            await categoryChannel.AddOverwriteAsync(everyone, Permissions.None, DENIED);

            // Apply all roles that were found in the config (to our category)
            foreach (DiscordRole role in this.additionalRoles)
                await categoryChannel.AddOverwriteAsync(role, ALLOWED, Permissions.None);

            // Create each specified channel within the config
            foreach (string channelName in _options.ChannelNames)
                await CreateTextChannel(categoryChannel, $"{_courseCategory} {_courseNumber}", channelName);

            // Create voice channels based on config
            for (int i = 0; i < _options.NumberOfVoiceChannels; i++)
                _ = await _channel.Guild.CreateChannelAsync($"{_courseCategory} {_courseNumber} - {i + 1}", ChannelType.Voice, parent: categoryChannel);

            return (success, errorMessage);
        }

        async Task CreateTextChannel(DiscordChannel parent, string prefix, string title)
        {
            _ = await this._channel.Guild.CreateChannelAsync($"{prefix} {title}", ChannelType.Text, parent: parent);
        }

        protected override async Task ExecuteAsync()
        {
            string promptResponse = string.Empty;

            do
            {
                _courseCategory = string.Empty;
                _courseNumber = string.Empty;
                _courseTitle = string.Empty;
                
                _recentMessage = await WithReply($"Time to create a new class!\n\nWhat is the course category? (i.e CEIS, NETW, etc)\n",
                    replyHandler: (context) => _courseCategory = context.Result.Content,
                    isCancellable: true);

                _recentMessage = await ReplyEditWithReply(_recentMessage, $"Category: {_courseCategory}\n\nWhat's the course number?", false, true,
                    (context) => _courseNumber = context.Result.Content);

                _recentMessage = await ReplyEditWithReply(_recentMessage, $"Catgory: {_courseCategory}\n\nCourse Number: {_courseNumber}\n\nWhat is the course title?",
                    false,
                    false,
                    replyHandler: (context) => _courseTitle = context.Result.Content);

                _recentMessage = await ReplyEditWithReply(_recentMessage, $"So you want to create a new class '{_courseCategory} {_courseNumber} {_courseTitle}'? Reply yes/no",
                    add: false,
                    isCancellable: false,
                    replyHandler: (context)=> promptResponse = context.Result.Content);

            } while (promptResponse.ToLower().StartsWith("n"));


        }

        const string AUTHOR_NAME = "Admin Hat";
        const string DESCRIPTION = "Expand our inner-kingdom! Allow our knowledge seeking minions to diverge onto their requested path(s).";
        const string AUTHOR_ICON = "";
        const string REACTION_EMOJI = "";

        public override CommandConfig DefaultCommandConfig()
        {
            return new CommandConfig
            {
                AuthorName = AUTHOR_NAME,
                Description = DESCRIPTION,
                ReactionEmoji = REACTION_EMOJI,
                IgnoreHelpWizard = false,
                RestrictedRoles = new List<string>() 
                { 
                    "Moderator", 
                    "Admin" 
                }
            };
        }

        public override CreateClassWizardConfig DefaultSettings()
        {
            CreateClassWizardConfig config = new CreateClassWizardConfig();

            config.AcceptAnyUser = false;
            config.AdditionalRoles = new List<string>()
            {
                "See-All-Channels"
            };

            config.ChannelNames = new List<string>()
            {
                "general",
                "i-need-help",
                "course-feedback",
                "course-resources"
            };
            
            config.NumberOfVoiceChannels = 3;

            config.AuthorName = AUTHOR_NAME;
            config.AuthorIcon = AUTHOR_ICON;
            config.Description = DESCRIPTION;

            config.TimeoutOverride = null;

            config.UsesCommand = new WizardToCommandLink
            {
                DiscordCommand = "create-class",
                CommandConfig = DefaultCommandConfig()
            };

            return config;
        }
    }
}
