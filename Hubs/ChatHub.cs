using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SignalRChat.Data;
using SignalRChat.Models;
using System.Collections.Concurrent;

namespace SignalRChat.Hubs
{
    public class ChatHub : Hub
    {
        // Groupname and UserCount
        private static readonly ConcurrentDictionary<string, int> activeGroups = new ConcurrentDictionary<string, int>();
        // ConnectionID and Their Groups
        private static readonly ConcurrentDictionary<string, ConcurrentBag<string>> userGroups = new ConcurrentDictionary<string, ConcurrentBag<string>>();

        // Database Context
        private readonly ChatDbContext _dbContext;

        public ChatHub(ChatDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task SendMessage(string user, string message)
        {
            await DB_SaveChanges(message, user: user); // Save with the actual user
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }

        public async Task JoinGroup(string groupName, string user)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

            string systemMessage = $"{user} has joined the group {groupName}.";
            await DB_SaveChanges(systemMessage, groupName); // System message

            await Clients.Group(groupName).SendAsync("ReceiveMessage", "System", systemMessage);

            userGroups.AddOrUpdate(
                Context.ConnectionId,
                key => new ConcurrentBag<string> { groupName },
                (key, existingBag) =>
                {
                    if (!existingBag.Contains(groupName))
                    {
                        existingBag.Add(groupName);
                    }
                    return existingBag;
                });

            UpdateGroups(groupName, true);
            await GroupsHaveBeenUpdated();
            await SendGroupHistory(groupName);
        }

        public async Task SendMessageToGroup(string groupName, string user, string message)
        {
            await DB_SaveChanges(message, groupName, user); // Save with the actual user
            await Clients.Group(groupName).SendAsync("ReceiveMessage", user, message);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (userGroups.TryGetValue(Context.ConnectionId, out var groupBag) && !groupBag.IsEmpty)
            {
                foreach (var groupName in groupBag)
                {
                    string systemMessage = $"A user has disconnected from {groupName}.";
                    await DB_SaveChanges(systemMessage, groupName); // System message
                    await Clients.Group(groupName).SendAsync("ReceiveMessage", "System", systemMessage);
                    UpdateGroups(groupName, false);
                }
                userGroups.TryRemove(Context.ConnectionId, out _);
                await GroupsHaveBeenUpdated();
            }
            else
            {
                string systemMessage = $"A user has disconnected globally.";
                await DB_SaveChanges(systemMessage); // System message
                await Clients.All.SendAsync("ReceiveMessage", "System", systemMessage);
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task LeaveGroup(string groupName, string user)
        {
            string systemMessage = $"{user} has left the group {groupName}.";
            await DB_SaveChanges(systemMessage, groupName); // System message

            await Clients.Group(groupName).SendAsync("ReceiveMessage", "System", systemMessage); // Fix to use "System" prefix
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

            if (userGroups.TryGetValue(Context.ConnectionId, out var groupBag))
            {
                lock (groupBag)
                {
                    var updatedBag = new ConcurrentBag<string>(groupBag.Where(g => g != groupName));
                    userGroups[Context.ConnectionId] = updatedBag;
                    if (updatedBag.IsEmpty)
                    {
                        userGroups.TryRemove(Context.ConnectionId, out _);
                    }
                }
            }

            UpdateGroups(groupName, false);
            await GroupsHaveBeenUpdated();
        }

        // Group Updating
        private async Task GroupsHaveBeenUpdated()
        {
            await Clients.All.SendAsync("ReceiveGroupList", activeGroups.Keys.ToList());
        }

        private void UpdateGroups(string groupName, bool onJoin)
        {
            if (onJoin)
            {
                activeGroups.AddOrUpdate(groupName, 1, (key, oldValue) => oldValue + 1);
            }
            else
            {
                if (activeGroups.TryGetValue(groupName, out int currentCount) && currentCount > 0)
                {
                    if (activeGroups.TryUpdate(groupName, currentCount - 1, currentCount))
                    {
                        if (activeGroups[groupName] <= 0)
                        {
                            activeGroups.TryRemove(groupName, out _);
                        }
                    }
                }
            }
        }

        // Database Storing/Updating
        private async Task DB_SaveChanges(string message, string groupName = "", string user = "System")
        {
            var chatMessage = new ChatMessage
            {
                User = user,
                Message = message,
                Timestamp = DateTime.UtcNow,
                GroupName = string.IsNullOrEmpty(groupName) ? null : groupName
            };
            _dbContext.ChatMessages.Add(chatMessage);
            await _dbContext.SaveChangesAsync();
        }

        // Get Chat History
        public async Task GetChatHistory(string groupName = null)
        {
            var query = _dbContext.ChatMessages
                .Where(m => m.GroupName == groupName) // null for global, specific groupName for group messages
                .OrderBy(m => m.Timestamp)
                .Take(50) // Limit to last 50 messages
                .Select(m => new { m.User, m.Message, m.Timestamp });

            var messages = await query.ToListAsync();
            await Clients.Caller.SendAsync("ReceiveHistory", groupName, messages);
        }

        // Helper method to send group history when joining
        private async Task SendGroupHistory(string groupName)
        {
            await GetChatHistory(groupName);
        }
    }
}