namespace SignalRChat.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public required string User { get; set; }
        public required string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public string? GroupName { get; set; }
    }
}