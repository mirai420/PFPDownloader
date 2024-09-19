using Discord;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net.WebSockets;

class Program
{
    private DiscordSocketClient _client;
    private const string token = "discord_app_token";
    private ConcurrentDictionary<ulong, DateTime> userCommandTimestamps = new ConcurrentDictionary<ulong, DateTime>();

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

