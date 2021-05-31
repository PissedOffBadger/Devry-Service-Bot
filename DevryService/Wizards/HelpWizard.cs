using DevryService.Commands;
using DevryService.Core;
using DevryService.Core.Util;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Interactivity;
using DSharpPlus.EventArgs;

using DescriptionAttribute = DevryService.Core.Util.DescriptionAttribute;

namespace DevryService.Wizards
{
    using Core.Util;
    using Microsoft.Extensions.Logging;

    public struct MainPage
    {
        public Dictionary<string, SubPage> Wizards { get; set; }
        public SubPage Misc { get; set; }
    }

    public struct SubPage
    {
        public string Description { get; set; }
        public Dictionary<string, PageEntry> Options { get; set; }
    }

    public struct PageEntry
    {
        public string Description { get; set; }
        public Action<CommandContext> Command { get; set; }
    }

    public class HelpWizardConfig : WizardConfig
    {
        public List<OptionsBaseConfig> Options { get; set; } = new List<OptionsBaseConfig>();
    }

    public class HelpWizard : WizardBase<HelpWizardConfig>
    {
        const string Headline = "Devry Help Wizard. Please react to the appropriate emoji and we'll get you started!\n\n";
        const string POSITIVE_EMOJI = ":white_check_mark:";
        const string NEGATIVE_EMOJI = ":negative_squared_cross_mark:";

        const string AUTHOR_NAME = "Helper Hat";
        const string AUTHOR_ICON = "https://www.iconfinder.com/data/icons/millionaire-habits-filledoutline/64/HELP_OTHERS_SUCCEED-bussiness-people-finance-marketing-milionaire_habits-512.png";
        const string DESCRIPTION = "Gives Guidance";
        const string REACTION_EMOJI = "";

        Dictionary<string, (Type, MethodInfo)> commandsToMethods = new Dictionary<string, (Type,MethodInfo)>();

        MainPage MainMenu = new MainPage()
        {
            Misc = new SubPage()
            {
                Options = new Dictionary<string, PageEntry>()
            },
            Wizards = new Dictionary<string, SubPage>()
        };

        public HelpWizard(CommandContext commandContext) : base(commandContext)
        {
        }

        protected override void Initialize()
        {
            base.Initialize();
            initializeCommandsToMethods();
        }


        void initializeCommandsToMethods()
        {
            Type[] types = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(x => !x.IsAbstract && !x.IsInterface && x.Name.ToLower().EndsWith("command"))
                .ToArray();

            foreach(Type type in types)
            {
                MethodInfo info = type.GetMethods().FirstOrDefault(x => x.GetCustomAttribute<CommandAttribute>() != null);

                if (info != null)
                    commandsToMethods.Add(info.GetCustomAttribute<CommandAttribute>().Name, (type, info));
            }
        }

        protected override async Task ExecuteAsync()
        {
            var embed = EmbedBuilder();

            List<string> currentEmojis = new List<string>();
            
            await _context.TriggerTypingAsync();

            // This is to create our first main window
            foreach (var option in _options.Options)
            {
                string description = string.Empty;
                
                if(option.RunCommand != null)
                {
                    if(!ConfigHandler.CommandConfigs.ContainsKey(option.RunCommand.CommandName))
                    {
                        Worker.Instance.Logger?.LogError($"Could not locate CommandConfig for '{option.RunCommand.CommandName}'");
                        continue;
                    }

                    CommandConfig config = ConfigHandler.CommandConfigs[option.RunCommand.CommandName];

                    // If this command has restrictions on roles -- take that into consideration
                    if(config.RestrictedRoles != null && config.RestrictedRoles.Count > 0)
                        // We want to see if the roles on the member match anything within the restricted roles array
                        if (!_context.Member.Roles.Any(x => config.RestrictedRoles.Contains(x.Name)))
                            continue;

                    // Option will become the following
                    embed.AddField(option.RunCommand.Emoji, DiscordMarkdown.GetText(_context, ConfigHandler.CommandConfigs[option.RunCommand.CommandName].Description)+ "\n\n", true);
                    currentEmojis.Add(option.RunCommand.Emoji);
                }
                else if(option.YesNoBetween != null)
                {
                    CommandConfig yesConfig = null, noConfig = null;

                    if(!ConfigHandler.CommandConfigs.ContainsKey(option.YesNoBetween.Yes))
                    {
                        Worker.Instance.Logger?.LogError($"Could not locate CommandConfig for '{option.YesNoBetween.Yes}'");
                        continue;
                    }

                    if(!ConfigHandler.CommandConfigs.ContainsKey(option.YesNoBetween.No))
                    {
                        Worker.Instance.Logger?.LogError($"Could not locate CommandConfig for '{option.YesNoBetween.No}'");
                        continue;
                    }

                    yesConfig = ConfigHandler.CommandConfigs[option.YesNoBetween.Yes];
                    noConfig = ConfigHandler.CommandConfigs[option.YesNoBetween.No];

                    bool addYes = true, addNo = true;
                    if (yesConfig.RestrictedRoles != null && yesConfig.RestrictedRoles.Count > 0)
                        if (!_context.Member.Roles.Any(x => yesConfig.RestrictedRoles.Contains(x.Name)))
                            addYes = false;

                    if (noConfig.RestrictedRoles != null && noConfig.RestrictedRoles.Count > 0)
                        if (!_context.Member.Roles.Any(x => noConfig.RestrictedRoles.Contains(x.Name)))
                            addNo = false;

                    // At this point we can finally 'ADD' the menu emoji to the help menu because the user has permission to use AT LEAST one of the sub-commands
                    if(addYes || addNo)
                    {
                        embed.AddField(option.YesNoBetween.Emoji, DiscordMarkdown.GetText(_context, option.YesNoBetween.Description) + "\n\n", true);
                        currentEmojis.Add(option.YesNoBetween.Emoji);
                    }
                }
            }

            await _context.TriggerTypingAsync();
            _recentMessage = await SimpleReply(embed.Build(), true, true);

            // Add emojis from above
            foreach (var emoji in currentEmojis)
            {
                try
                {
                    await _recentMessage.CreateReactionAsync(DiscordEmoji.FromName(Bot.Discord, emoji));
                }
                catch
                {
                    Console.WriteLine($"Invalid emoji: {emoji}");
                }
                await Task.Delay(250);
            }
            

            var interactivityResult = await _recentMessage.WaitForReactionAsync(_context.Member);

            // Did the client timeout?
            if(interactivityResult.TimedOut)
            {
                await SimpleReply(EmbedBuilder()
                                        .WithColor(DiscordColor.Red)
                                        .WithDescription(_options.AuthorName + " Wizard timed out...")
                                        .Build(),
                                  false,false);
                throw new StopWizardException(_options.AuthorName);
            }

            string userEmoji = interactivityResult.Result.Emoji;
            userEmoji = DiscordEmoji.FromUnicode(Bot.Discord, userEmoji).GetDiscordName();

            // Did the user interact using the appropriate emoji?
            if (!currentEmojis.Contains(userEmoji))
                return;

            foreach(var option in _options.Options)
            {
                if(option.RunCommand != null)
                {
                    if (option.RunCommand.Emoji.Equals(userEmoji, StringComparison.OrdinalIgnoreCase))
                    {
                        // Cleanup these messages -> because -> we're going to be transitioning into another wizard
                        await CleanupAsync();

                        var data = commandsToMethods[option.RunCommand.CommandName];
                        object instance = Activator.CreateInstance(data.Item1);
                        data.Item2.Invoke(instance, new object[] { _context });
                        break;
                    }
                }
                else if(option.YesNoBetween != null)
                {
                    if(option.YesNoBetween.Emoji.Equals(userEmoji, StringComparison.OrdinalIgnoreCase))
                    {
                        // We have established the fact this menu was selected. No we need a submenu to select yes/no
                        embed = EmbedBuilder().WithDescription(DiscordMarkdown.GetText(_context, option.YesNoBetween.Description))
                            .AddField(option.YesNoBetween.YesEmoji, DiscordMarkdown.GetText(_context, ConfigHandler.CommandConfigs[option.YesNoBetween.Yes].Description))
                            .AddField(option.YesNoBetween.NoEmoji, DiscordMarkdown.GetText(_context, ConfigHandler.CommandConfigs[option.YesNoBetween.No].Description))
                            .WithColor(DiscordColor.PhthaloGreen);

                        await _recentMessage.ModifyAsync(embed: embed.Build());
                        await _recentMessage.DeleteAllReactionsAsync();

                        await _recentMessage.CreateReactionAsync(DiscordEmoji.FromName(Bot.Discord, option.YesNoBetween.YesEmoji));
                        await Task.Delay(250);
                        await _recentMessage.CreateReactionAsync(DiscordEmoji.FromName(Bot.Discord, option.YesNoBetween.NoEmoji));
                        await Task.Delay(250);

                        interactivityResult = await _recentMessage.WaitForReactionAsync(_context.Member);

                        if(interactivityResult.TimedOut)
                        {
                            await CleanupAsync();
                            await SimpleReply(EmbedBuilder().WithColor(DiscordColor.Red).WithDescription(_options.AuthorName + " Wizard Timed Out"),false,false);
                            break;
                        }

                        // Finally get the name
                        userEmoji = DiscordEmoji.FromUnicode(Bot.Discord, interactivityResult.Result.Emoji).GetDiscordName();
                        
                        // Can finally cleanup as we're about to enter the next wizard
                        await CleanupAsync();

                        if(option.YesNoBetween.YesEmoji.Equals(userEmoji))
                        {
                            var data = commandsToMethods[option.YesNoBetween.Yes];
                            object instance = Activator.CreateInstance(data.Item1);
                            data.Item2.Invoke(instance, new object[] { _context });
                        }
                        else if(option.YesNoBetween.NoEmoji.Equals(userEmoji))
                        {
                            var data = commandsToMethods[option.YesNoBetween.No];
                            object instance = Activator.CreateInstance(data.Item1);
                            data.Item2.Invoke(instance, new object[] { _context });
                        }

                        break;
                    }
                }                
            }
        }

        public override CommandConfig DefaultCommandConfig()
        {
            var config = ConfigHandler.FindConfig<CommandConfig>("help");

            return new CommandConfig
            {
                AuthorName = AUTHOR_NAME,
                Description = DESCRIPTION,
                IgnoreHelpWizard = false,
                ReactionEmoji = REACTION_EMOJI
            };
        }

        public override HelpWizardConfig DefaultSettings()
        {
            HelpWizardConfig config = new HelpWizardConfig();

            config.AuthorName = AUTHOR_NAME;
            config.Description = DESCRIPTION;
            config.AuthorIcon = AUTHOR_ICON;

            config.AcceptAnyUser = false;
            config.MessageRequireMention = false;

            config.UsesCommand = new WizardToCommandLink
            {
                DiscordCommand = "help",
                CommandConfig = DefaultCommandConfig()
            };

            config.Options = new List<OptionsBaseConfig>()
            {
                new OptionsBaseConfig
                {
                    Page = 0,
                    RunCommand = new RunCommandConfig
                    {
                        Emoji = ":e_mail:",
                        CommandName = "invite"
                    }
                },

                new OptionsBaseConfig
                {
                    Page = 0,
                    YesNoBetween = new YesNoOptionConfig
                    {
                        Emoji = ":classical_building:",
                        Yes = "join",
                        No = "leave",
                        Description = "Need to join a class? No problem! Or is it that time of year where your class has ended?\n\n" +
                        "It is worth noting you are NOT required to leave. You're more than welcome to stick around for awhile! Help out!" +
                        "Perhaps become a tutor for that class! (let a @Moderator know!)",
                        YesEmoji = POSITIVE_EMOJI,
                        NoEmoji = NEGATIVE_EMOJI
                    }
                },

                new OptionsBaseConfig
                {
                    Page = 0,
                    RunCommand = new RunCommandConfig
                    {
                        Emoji = ":desktop:",
                        CommandName = "code"
                    }
                },

                new OptionsBaseConfig
                {
                    Page = 0,
                    YesNoBetween = new YesNoOptionConfig
                    {
                        Emoji = ":date:",
                        Yes = "create-event",
                        No = "delete-event",
                        YesEmoji = POSITIVE_EMOJI,
                        NoEmoji = NEGATIVE_EMOJI,
                        Description = "Create a reminder/event that regularly displays a message! Or... perhaps delete one!\n\nThis is channel-based. So make sure " +
                        "the channel you wish to display a reminder in is the one you're in right now! Same applies for deleting!"
                    },
                    RestrictedRoles = new List<string>()
                    {
                        "Senior Moderators","Junior Moderators","Tutor","Professor","Admin"
                    }
                }
            };

            return config;
        }
    }
}
