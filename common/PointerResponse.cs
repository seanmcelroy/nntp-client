namespace mcnntp.common
{
    public class PointerResponse : NntpResponse
    {
        public int? ArticleNumber { get; private set; }
        public string? MessageId { get; private set; }

        public PointerResponse(int code, string? message, int? articleNumber, string? messageId) : base(code, message)
        {
            this.ArticleNumber = articleNumber;
            this.MessageId = messageId;
        }
    }
}