using Discord;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net.WebSockets;

class Program
{
    private DiscordSocketClient _client;
    private const string token = "MTE3NTM5OTE0OTU5NjgzNTg4MA.GyjKdr.A0oTIfbhA4-_oln2wiWwGgHyY1GDBd9jnglPFs";
    private ConcurrentDictionary<ulong, DateTime> userCommandTimestamps = new ConcurrentDictionary<ulong, DateTime>();
    private ConcurrentDictionary<ulong, int> adTimeout = new ConcurrentDictionary<ulong, int>();

    static void Main(string[] args) => new Program().RunBotAsync().GetAwaiter().GetResult();

    private async Task RunBotAsync()
    {
        DiscordSocketConfig _config = new()
        {
            UseInteractionSnowflakeDate = false,
            GatewayIntents = GatewayIntents.Guilds
        };

        _client = new DiscordSocketClient(_config);

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.SlashCommandExecuted += ExecuteCommand;

        AppDomain.CurrentDomain.ProcessExit += async (sender, eventArgs) =>
        {
            await Shutdown();
        };

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    private async Task Shutdown()
    {
        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    private Task LogAsync(LogMessage log)
    {
        if (log.Exception is not ArgumentNullException && log.Exception is not GatewayReconnectException && log.Exception is not TimeoutException && log.Exception is not JsonSerializationException && log.Exception is not NullReferenceException && log.Exception?.InnerException is not WebSocketException && log.Exception?.InnerException is not WebSocketClosedException)
        {
            Console.WriteLine(log);
        }

        return Task.CompletedTask;
    }

    private async Task ReadyAsync()
    {
        await _client.SetGameAsync("/get-pfp", null, ActivityType.Watching);

        Console.WriteLine($"Bot is in {_client.Guilds.Count} servers");
    }

    private async Task<bool> HandleCommandCooldownAsync(SocketSlashCommand slashCommand)
    {
        ulong userId = slashCommand.User.Id;

        if (userCommandTimestamps.TryGetValue(userId, out DateTime lastCommandTime) && DateTime.UtcNow - lastCommandTime < TimeSpan.FromSeconds(2))
        {
            await slashCommand.RespondAsync("**Please do not spam commands**", ephemeral: true);

            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                await slashCommand.DeleteOriginalResponseAsync();
            });

            return true;
        }

        userCommandTimestamps[userId] = DateTime.UtcNow;
        return false;
    }

    private async Task ShowAd(SocketSlashCommand command)
    {
        ulong userId = command.User.Id;

        adTimeout.AddOrUpdate(userId, 1, (key, oldValue) => oldValue + 1);

        if (adTimeout.TryGetValue(userId, out var requests) && requests < 3)
        {
            return;
        }

        var adEmbed = new EmbedBuilder()
        {
            Description = "# Check this bot too!\n**Introducing ChatGPT Bot, the most capable Discord AI chatbot! It also can generate high-quality images, music, and more! Invite it now to get a free bonus!**",
            Color = Color.Green,
            Url = "https://example.com"
        }.Build();

        var firstExample = new EmbedBuilder().WithImageUrl("https://media.discordapp.net/attachments/1173584048103366716/1207636851549151272/chrome-capture_5.png?ex=65fc0df6&is=65e998f6&hm=a25c491e3ae2192aab6b2c29222bde6f420e66ae98bd9b2d0ad903019c7bf287").WithUrl("https://example.com").Build();
        var secondExample = new EmbedBuilder().WithImageUrl("https://media.discordapp.net/attachments/1173584048103366716/1215308186089951282/chrome-capture_12.png?ex=65fc46f2&is=65e9d1f2&hm=0d835d1ff4432e8fa96926e950aec7cf49e3861900a76639db5339f59fe4108d").WithUrl("https://example.com").Build();
        var embedsList = new List<Embed> { adEmbed, firstExample, secondExample };

        var adBuilder = new ComponentBuilder().WithButton("Receive Bonus", style: ButtonStyle.Link, emote: new Emoji("🎁"), url: "https://discord.com/api/oauth2/authorize?client_id=1142038083236286505&permissions=395137076224&scope=bot+applications.commands").Build();

        await command.FollowupAsync(embeds: embedsList.ToArray(), components: adBuilder, ephemeral: true);
        adTimeout.TryRemove(userId, out _);
    }

    private async Task ExecuteCommand(SocketSlashCommand slashCommand)
    {
        if (await HandleCommandCooldownAsync(slashCommand))
        {
            return;
        }

        Console.WriteLine($"Executed a slash command: {slashCommand.Data.Name}");

        switch (slashCommand.Data.Name)
        {
            case "get-pfp":
                await GetPfp(slashCommand);
                break;
            case "get-banner":
                await GetBanner(slashCommand);
                break;
            case "get-emoji":
                await GetEmoji(slashCommand);
                break;
            case "get-icon":
                await GetGuildIcon(slashCommand);
                break;
            case "get-server-banner":
                await GetGuildBanner(slashCommand);
                break;
        }
    }

    private async Task GetEmoji(SocketSlashCommand command)
    {
        string emojiString = command.Data.Options.FirstOrDefault().Value.ToString();

        if(Emote.TryParse(emojiString, out var emote))
        {
            await command.DeferAsync();
            await command.FollowupAsync(emote.Url);
            await ShowAd(command);
        }
        else
        {
            await command.RespondAsync("**Invalid input. Enter the emoji you want to download**");
        }
    }

    private async Task GetGuildIcon(SocketSlashCommand command)
    {
        if(command.Channel is IDMChannel)
        {
            await command.RespondAsync("**Use this command on a server**");
            return;
        }

        ulong guildId = (ulong)command.GuildId;
        string iconUrl = _client.GetGuild(guildId).IconUrl;

        if (!string.IsNullOrEmpty(iconUrl))
        {
            string extension = await GetExtension(guildId, "icons");

            await command.DeferAsync();
            await command.FollowupAsync($"{iconUrl.Replace("jpg", extension)}?size=1024");
            await ShowAd(command);
        }
        else
        {
            await command.RespondAsync("**This server has no icon**");
        }
    }

    private async Task GetGuildBanner(SocketSlashCommand command)
    {
        if (command.Channel is IDMChannel)
        {
            await command.RespondAsync("**Use this command on a server**");
            return;
        }

        ulong guildId = (ulong)command.GuildId;
        string bannerUrl = _client.GetGuild(guildId).BannerUrl;

        if (!string.IsNullOrEmpty(bannerUrl))
        {
            string extension = await GetExtension(guildId, "icons");

            await command.DeferAsync();
            await command.FollowupAsync($"{bannerUrl.Replace("jpg", extension)}?size=2048");
            await ShowAd(command);
        }
        else
        {
            await command.RespondAsync("**This server has no banner**");
        }
    }

    private async Task GetPfp(SocketSlashCommand command)
    {
        var user = command.Data.Options.FirstOrDefault().Value as SocketUser;
        await command.DeferAsync();

        string avatarUrl = user.GetAvatarUrl();

        if (!string.IsNullOrEmpty(avatarUrl))
        {
            await command.FollowupAsync(avatarUrl.Replace("128", "1024"));
        }
        else
        {
            await command.FollowupAsync(user.GetDefaultAvatarUrl());
        }

        await ShowAd(command);
    }

    private async Task GetBanner(SocketSlashCommand command)
    {
        var user = command.Data.Options.FirstOrDefault().Value as SocketUser;
        ulong userId = user.Id;

        await command.DeferAsync();

        string apiUrl = $"https://discord.com/api/v8/users/{userId}";

        using(HttpClient httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bot {token}");

            var response = await httpClient.GetAsync(apiUrl);
            var responseData = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                dynamic jsonResponse = JsonConvert.DeserializeObject(responseData);
                string bannerHash = jsonResponse.banner;

                if (bannerHash != null) 
                {
                    string extension = await GetExtension(userId, "banners", bannerHash);
                    string bannerUrl = $"https://cdn.discordapp.com/banners/{userId}/{bannerHash}.{extension}?size=2048";

                    await command.FollowupAsync(bannerUrl);
                    await ShowAd(command);
                }
                else
                {
                    await command.FollowupAsync("**This user has no custom banner**");
                }
            }
            else
            {
                Console.WriteLine($"An error has occurred while downloading banner: {responseData}, status code: {response.StatusCode}");
                await command.FollowupAsync("**An error has occurred(**");
            }
        }
    }

    private async Task<string> GetExtension(ulong id, string target, string hash = "")
    {
        using (var httpClient = new HttpClient())
        {
            var response = await httpClient.GetAsync($"https://cdn.discordapp.com/{target}/{id}/{hash}.gif");

            if (response.IsSuccessStatusCode)
            {
                return "gif";
            }

            return "png";
        }
    }
}

