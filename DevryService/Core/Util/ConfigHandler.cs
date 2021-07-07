using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevryService.Core.Util
{
    using System.IO;
    using System.Reflection;
    using Microsoft.Extensions.Configuration;
    using Wizards;
    using Wizards.Admin;
    using Commands;
    using DSharpPlus.CommandsNext.Attributes;
    using Microsoft.Extensions.Logging;
    using System.Collections;

    public static class ConfigHandler
    {
        static string ConfigDirectory => Path.Join(Directory.GetCurrentDirectory(), "Configs");
        static string PageDirectory => Path.Join(Directory.GetCurrentDirectory(), "Pages");

        public static Dictionary<string, WizardConfig> WizardConfigs = new Dictionary<string, WizardConfig>();
        public static Dictionary<string, CommandConfig> CommandConfigs = new Dictionary<string, CommandConfig>();

        static void Print(string message, ConsoleColor color = ConsoleColor.White, bool newLine = true)
        {
            Console.ForegroundColor = color;

            if (newLine)
                Console.WriteLine(message);
            else
                Console.Write(message);

            Console.ResetColor();
        }

        public static string GetConfigFileName<T>()
        {
            return $"{typeof(T).Name}.json";
        }

        public static string GetConfigFileName(Type type)
        {
            return $"{type.Name}.json";
        }

        public static string[] InitializeSettings()
        {
            if (!CommandConfigs.ContainsKey("invite")) 
                CommandConfigs.Add("invite", InviteLinkConfig());
            if(!CommandConfigs.ContainsKey("view-welcome"))
                CommandConfigs.Add("view-welcome", ViewWelcomeConfig());
            if(!CommandConfigs.ContainsKey("view-stats"))
                CommandConfigs.Add("view-stats", ViewStatsConfig());

            // Shall track the current config files tracked by the system
            List<string> configFiles = new List<string>();
            
            // This shall track the current commands that have configurations (via wizards activated by a command)
            List<string> commandConfigurations = new List<string>();

            // What are the wizards within this application
            Type[] wizardTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(x => !x.IsAbstract && x.Name.ToLower().EndsWith("wizard"))
                .ToArray();

            // Ensure that our config folder exists
            if (!Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);

            // Ensure that our page directory exists
            if (!Directory.Exists(PageDirectory))
                Directory.CreateDirectory(PageDirectory);


            foreach(var wizard in wizardTypes)
            {
                // Print the current type that we're initializing settings for
                Print("Discovered Wizard ", newLine: false);
                Print($"'{wizard.Name}'", ConsoleColor.DarkYellow);

                // Put the path together...
                string configFilePath = Path.Combine(ConfigDirectory, GetConfigFileName(wizard));
                configFiles.Add(configFilePath);

                if (WizardConfigs.ContainsKey(wizard.Name))
                    continue;

                object wizardInstance = Activator.CreateInstance(wizard, args: new object[] { null });
                // We shall invoke the DefaultSettings method on this wizard to acquire ... default configuration
                MethodInfo info = wizard.GetMethods().FirstOrDefault(x => x.Name.Equals("DefaultSettings", StringComparison.OrdinalIgnoreCase));

                // Does the file exist?
                if (File.Exists(configFilePath))
                {
                    Print("\tLocated config for ", newLine: false);
                    Print($"'{wizard.Name}'", ConsoleColor.DarkYellow);

                    if(Worker.Configuration != null)
                    {
                        IConfigurationSection section = Worker.Configuration.GetSection(wizard.Name);
                        WizardConfig config = Activator.CreateInstance(info.ReturnType) as WizardConfig;
                        section.Bind(config);

                        IConfigurationSection usesCommandSection = section.GetSection("UsesCommand");
                        IConfigurationSection commandConfigSection = usesCommandSection.GetSection("CommandConfig");
                        
                        foreach(var prop in typeof(CommandConfig).GetProperties())
                            if (typeof(IEnumerable).IsAssignableFrom(prop.PropertyType) || typeof(IList).IsAssignableFrom(prop.PropertyType))
                                prop.SetValue(config.UsesCommand.CommandConfig, commandConfigSection.GetSection(prop.Name).Get(prop.PropertyType));

                        foreach (var field in typeof(CommandConfig).GetFields())
                            if (typeof(IEnumerable).IsAssignableFrom(field.FieldType) || typeof(IList).IsAssignableFrom(field.FieldType))
                                field.SetValue(config.UsesCommand.CommandConfig, commandConfigSection.GetSection(field.Name).Get(field.FieldType));

                        if(config.UsesCommand != null && config.UsesCommand.CommandConfig != null && !CommandConfigs.ContainsKey(config.UsesCommand.DiscordCommand))
                            CommandConfigs.Add(config.UsesCommand.DiscordCommand, config.UsesCommand.CommandConfig);

                        if (typeof(HelpWizardConfig).IsAssignableTo(config.GetType()))
                        {
                            HelpWizardConfig helpConfig = (HelpWizardConfig)config;

                            IConfigurationSection optionsSection = section.GetSection("Options");
                            helpConfig.Options = optionsSection.Get<List<OptionsBaseConfig>>();

                            WizardConfigs.Add(wizard.Name, helpConfig);
                        }
                        else
                            WizardConfigs.Add(wizard.Name, config);
                    }

                    continue;
                }

                try
                {
                    WizardConfig defaultConfig = info.Invoke(wizardInstance, null) as WizardConfig;

                    // Serialize this configuration into JSON
                    string settings = Newtonsoft.Json.JsonConvert.SerializeObject(defaultConfig);

                    // We must format this json so we can read it in by section (aka the wizard type)
                    string json = "{" + $"\"{wizard.Name}\":" + settings + "}";
                    Console.WriteLine($"\n\n{json}\n\n");

                    // Finally output our default config to disk
                    File.WriteAllText(configFilePath, json);

                    if(defaultConfig.UsesCommand != null)
                    {
                        commandConfigurations.Add(defaultConfig.UsesCommand.DiscordCommand);
                        CommandConfigs.Add(defaultConfig.UsesCommand.DiscordCommand, defaultConfig.UsesCommand.CommandConfig);
                    }

                    WizardConfigs.Add(wizard.Name, defaultConfig);
                }
                catch(Exception ex)
                {
                    Print($"Error processing '{wizard.Name}' --> Getting Default Config\n\t{ex.Message}", ConsoleColor.Red);
                }
            }

            Type[] commandTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(x => !x.IsAbstract && x.Name.ToLower().EndsWith("command") && !x.IsInterface)
                .ToArray();

            foreach(Type commandType in commandTypes)
            {                
                var method = commandType.GetMethods().FirstOrDefault(x => x.GetCustomAttribute<CommandAttribute>() != null);

                if (method == null)
                    continue;
                
                string name = method.GetCustomAttribute<CommandAttribute>().Name;

                // Ensure a wizard didn't already have this config... configured...
                if (commandConfigurations.Contains(name))
                    continue;

                // If our command config contains the command....
                if (CommandConfigs.ContainsKey(name))
                {                    
                    string configPath = Path.Combine(ConfigDirectory, $"{name}.json");
                    
                    // But we don't have a file that means we have a default config that needs to be saved to disk
                    if(!File.Exists(configPath))
                    {
                        // Format the json how we should
                        string json = "{" + $"\"{name}\":" + Newtonsoft.Json.JsonConvert.SerializeObject(CommandConfigs[name]) + "}";
                        File.WriteAllText(configPath, json);

                        // Be sure to add this config file to our end result
                        configFiles.Add(configPath);
                    }
                }
            }

            return configFiles.ToArray();
        }

        public static async Task UpdateWizardConfig<T>(T config) where T: WizardConfig
        {
            string filePath = Path.Combine(ConfigDirectory, GetConfigFileName(typeof(T)));
            string json = "{" + $"\"{typeof(T).Name}\":" + Newtonsoft.Json.JsonConvert.SerializeObject(config) + "}";

            // Update config on disk for later consumption
            await File.WriteAllTextAsync(filePath, json);

            // We need to also update our cache
            if(WizardConfigs.ContainsKey(config.GetType().Name))
            {
                WizardConfigs[config.GetType().Name] = config;
                Worker.Instance.Logger?.LogInformation($"Updated Config for '{config.GetType().Name}'");
            }
        }

        public static async Task UpdateCommandConfig<T>(T config) where T : CommandConfig
        {
            string filePath = Path.Combine(ConfigDirectory, GetConfigFileName<T>());
            string json = "{" + $"\"{typeof(T).Name}\":" + Newtonsoft.Json.JsonConvert.SerializeObject(config) + "}";

            // Update the config on DISK for later consumption, but also update our cache
            await File.WriteAllTextAsync(filePath, json);

            var pair = CommandConfigs.FirstOrDefault(x => x.Value.AuthorName == config.AuthorName);
            
            if(pair.Key != string.Empty)
            {
                CommandConfigs[pair.Key] = config;
            }
        }

        public static T FindConfig<T>(string name) where T : CommandConfig
        {
            string path = Path.Join(ConfigDirectory, $"{name}.json");
            
            if(WizardConfigs.Values.Any(x=>x.UsesCommand != null && x.UsesCommand.DiscordCommand.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return (T)WizardConfigs.Values
                    .First(x => x.UsesCommand
                                 .DiscordCommand
                                 .Equals(name, StringComparison.OrdinalIgnoreCase))
                                    .UsesCommand
                                    .CommandConfig;

            if (CommandConfigs.ContainsKey(name))
                return (T)CommandConfigs[name];

            if(Worker.Configuration != null)
            {
                try
                {
                    T instance = Activator.CreateInstance<T>();
                    Worker.Configuration.GetSection(name).Bind(instance);

                    return instance;
                }
                catch(Exception ex)
                {
                    Worker.Instance?.Logger?.LogError($"Error finding config for '{name}'\n\t{ex.Message}");
                    return default(T);
                }
            }

            // We can't exactly 'load' from the file in a clean way because it's not 'standard' json.. if you recall we added a few things

            return null;
        }

        public static MessageConfig ViewWelcomeConfig()
        {
            MessageConfig config = FindConfig<MessageConfig>("view-welcome");

            string contents = $"Welcome, [USER], to the Unofficial DeVry IT Discord! A community-built by students to help foster education and professional growth; please " +
                $"introduce yourself in the #welcome-page channel and join our main discussions in the #main-hub-general . \n\n" +
                $"You can find your fellow classmates 1 of 3 ways. \n" +
                $"1.) Utilize the `!help` command, then react to the appropriate emoji\n" +
                $"2.) Utilize the `!join` command to bypass the `help` wizard.\n" +
                $"3.) Inform us which classes you're in, a Junior or Senior Moderator will gladly assist you.\n\n" +
                $"Thanks, and welcome to the Unofficial DeVry IT Discord!";

            if (config == null)
                return new MessageConfig
                {
                    Description = "View the message that gets sent to newcomers when they join!",
                    Contents = contents
                };

            return config;
        }

        public static EmbedConfig InviteLinkConfig()
        {
            EmbedConfig config = FindConfig<EmbedConfig>("invite");

            if(config == null)
                config = new EmbedConfig
                {
                    AuthorName = "Recruiter Hat",
                    Title = "Invitation",
                    ReactionEmoji = ":email:",
                    Description = "Spread the word! Get your fellow classmates to join us!",
                    Contents = "Spread the word, our trusted scout! Spread the word " +
                    "of our kingdom! Amass an army of knowledge seeking minions! Lay waste " +
                    "to the legions of doubt and uncertainty!!",
                    Fields = new List<string>()
                    {
                        "Invite|https://discord.io/unofficial-DevryIT"
                    },
                    Footer = "Minions of knowledge! Assembblleeee!",
                    IgnoreHelpWizard = false
                };

            return config;
        }

        public static CommandConfig ViewStatsConfig()
        {
            CommandConfig config = FindConfig<CommandConfig>("view-stats");

            if(config == null)
                config = new CommandConfig
                {
                    AuthorName = "Stats Hat",
                    Description = "Not fully implemented",
                    AuthorIcon = "https://alifeofproductivity.com/wp-content/uploads/2013/06/stat.001.jpg"
                };

            return config;
        }
    }
}
