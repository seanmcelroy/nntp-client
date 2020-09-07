namespace mcnntp.common
{
    public class GroupResponse : NntpResponse
    {
        public string GroupName { get; set; }
        public int EstimatedArticleCount { get; set; }
        public int LowWatermark { get; set; }
        public int HighWatermark { get; set; }

        public GroupResponse(int code, string? message, string groupName, int estimatedArticleCount, int lowWatermark, int highWatermark) : base(code, message)
        {
            this.GroupName = groupName;
            this.EstimatedArticleCount = estimatedArticleCount;
            this.LowWatermark = lowWatermark;
            this.HighWatermark = highWatermark;
        }
    }
}