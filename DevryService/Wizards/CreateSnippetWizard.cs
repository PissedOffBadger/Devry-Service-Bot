using DevryService.Core;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace DevryService.Wizards
{
    public class CreateSnippetWizardConfig : WizardConfig
    {

    }

    public class CreateSnippetWizard : WizardBase<CreateSnippetWizardConfig>
    {
        public CreateSnippetWizard(CommandContext commandContext) : base(commandContext)
        {
        
        }

        #region Context Variables
        string _language = string.Empty,
               _content = string.Empty,
               _topic = string.Empty,
               _extension = string.Empty,
               _filename = string.Empty;

        SnippetWizardConfig _snippetConfig;
        #endregion

        protected override void Initialize()
        {
            base.Initialize();

            try
            {
                _snippetConfig = new SnippetWizardConfig();

                IConfigurationSection section = Worker.Configuration.GetSection(typeof(SnippetWizardConfig).Name.Replace("Config", ""));
                Worker.Configuration.GetSection(typeof(SnippetWizardConfig).Name).Bind(_snippetConfig);

                foreach (var property in _snippetConfig.GetType().GetProperties())
                    if (typeof(IList).IsAssignableFrom(property.PropertyType) || typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                        property.SetValue(_snippetConfig, section.GetSection(property.Name).Get(property.PropertyType));

                foreach (var field in _snippetConfig.GetType().GetFields())
                    if (typeof(IList).IsAssignableFrom(field.FieldType) || typeof(IEnumerable).IsAssignableFrom(field.FieldType))
                        field.SetValue(_snippetConfig, section.GetSection(field.Name).Get(field.FieldType));
            }
            catch(Exception ex)
            {
                logger?.LogError($"Unable to load SnippetWizard Settings to retrieve base directory\n\t{ex.Message}");
                _snippetConfig = new SnippetWizard(null).DefaultSettings();
            }
        }

        const string AUTHOR_NAME = "Coder Hat";
        const string DESCRIPTION = "Add some code snippets to help users out!";
        const string AUTHOR_ICON = "";
        const string REACTION_EMOJI = ":desktop:";
        readonly List<string> RESTRICTED_ROLES = new List<string>()
        {
            "Senior Moderators",
            "Admin",
            "Tutor",
            "Professor",
            "Junior Moderators"
        };

        public override CommandConfig DefaultCommandConfig()
        {
            return new CommandConfig
            {
                AuthorName = AUTHOR_NAME,
                Description = DESCRIPTION,
                ReactionEmoji = REACTION_EMOJI,
                IgnoreHelpWizard = false,
                RestrictedRoles = RESTRICTED_ROLES
            };
        }

        public override CreateSnippetWizardConfig DefaultSettings()
        {
            return new CreateSnippetWizardConfig()
            {
                AuthorName = AUTHOR_NAME,
                AcceptAnyUser = false,
                Description = DESCRIPTION,
                ReactionEmoji = REACTION_EMOJI,
                Headline = "Add Snippets",
                MessageRequireMention = false,
                TimeoutOverride = TimeSpan.FromMinutes(5),
                UsesCommand = new WizardToCommandLink
                {
                    DiscordCommand = "create-snippet",
                    CommandConfig = DefaultCommandConfig()
                }
            };
        }

        protected override async Task ExecuteAsync()
        {
            // Retrieve TOPIC
            _recentMessage = await WithReply("What's the topic of this code snippet? (i.e Arrays, Classes, etc)",
                (context) => ReplyHandlerAction(context, ref _topic),
                true);

            _extension = await ReplyEditWithReply<string>(_recentMessage, EmbedBuilder().WithDescription("What's the file extension of this particular language? (csv, json, without the period)").Build());
            _filename = await ReplyEditWithReply<string>(_recentMessage, EmbedBuilder().WithDescription($"What should the name of this file be? For easier reference... no extension as {_extension} will automatically be applied").Build());
                

            // We must ensure that our config contains information about this 'language' so we can allow users to select it
            if (!_snippetConfig.CodeBlocks.Any(x => x.Extension.Equals(_extension, StringComparison.OrdinalIgnoreCase)))
            {
                // Need to acquire the 'human readable' version for our code block. This will appear in menus when selecting a snippet
                string readableName = string.Empty;
                readableName = await ReplyEditWithReply<string>(_recentMessage,
                    EmbedBuilder()
                    .WithDescription($"Looks like a new type of language is being used... What is the 'human readable' text for this language? For instance, C#, C++, YAML, etc")
                    .Build());

                // Apply all of our info to this point
                _snippetConfig.CodeBlocks.Add(new CodeInfo
                {
                    Color = DiscordColor.HotPink,
                    DiscordCodeBlockLanguage = _language,
                    Extension = _extension,
                    HumanReadableName = readableName
                });

                // Attempt to update the snippet wizard config
                try
                {
                    await Core.Util.ConfigHandler.UpdateWizardConfig(_snippetConfig);
                }
                catch
                {
                    await SendErrorMessage("An error occurred while trying to update the SnippetWizardConfig");
                }
            }

            // Retrieve CODE BLOCK
            string codeBlock = await ReplyEditWithReply<string>(_recentMessage,
                EmbedBuilder().WithDescription("Provide the content you wish to share (in discord's code-block format)").Build());

            string snippetBasePath = _snippetConfig.RootCodePath;
            string directoryPath = Path.Combine(snippetBasePath, _topic);

            if(string.IsNullOrEmpty(codeBlock))
            {
                await SendErrorMessage("Invalid code snippet -- cannot be blank");
                throw new StopWizardException(GetType().Name);
            }

            if(!codeBlock.StartsWith("```") || !codeBlock.EndsWith("```"))
            {
                await SendErrorMessage("Was expecting just a codeblock, which means your message " +
                    "should start AND end with 3 backticks... ''' " +
                    "(can't use backticks in this response otherwise " +
                    "discord will attempt to format and you'll have no " +
                    "idea what I'm talking about)");

                throw new StopWizardException(GetType().Name);
            }

            // Code block cannot be blank
            // Now we have to parse that code block to get the language
            List<string> lines = codeBlock.Split("\n").ToList();

            _language = lines[0].Replace("```", "").ToLower();
            lines.RemoveAt(0);
            lines[lines.Count - 1] = lines[lines.Count - 1].Replace("```", "");

            // Ensure that our topic folder exists

            try
            {
                if (!Directory.Exists(directoryPath))
                    Directory.CreateDirectory(directoryPath);

                // Output the contents to file
                await File.WriteAllTextAsync(Path.Combine(directoryPath, $"{_filename}.{_extension}"), string.Join("\n", lines));
            }
            catch(Exception ex)
            {
                await SendErrorMessage($"An error occurred while saving your snippet... View logs for more information");
                                
                logger?.LogError($"Create Snippet Wizard had an oopsie. Dumping data:\n" +
                    $"\tFilename: {_filename}\n" +
                    $"\tExtension: {_extension}\n" +
                    $"\tTopic: {_topic}\n" +
                    $"\tLanguage: {_language}\n" +
                    $"\tCodeBlock: {codeBlock}\n\n" +
                    $"\tCode to File: {string.Join("\n", lines)}\n" +
                    $"\tFile Path: {Path.Combine(directoryPath, $"{_filename}.{_extension}")}\n" +
                    $"\n\tERROR MESSAGE: {ex.Message}");
            }
        }
    }
}
