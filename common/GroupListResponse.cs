using System.Collections.ObjectModel;

namespace mcnntp.common
{
    public class GroupListResponse : NntpResponse
    {
        public string GroupName { get; set; }
        public int EstimatedArticleCount { get; set; }
        public int LowWatermark { get; set; }
        public int HighWatermark { get; set; }
        public ReadOnlyCollection<int> ArticleNumbers {get;set;}

        public GroupListResponse(int code, string message, string groupName, int estimatedArticleCount, int lowWatermark, int highWatermark, ReadOnlyCollection<int> articleNumbers) : base(code, message)
        {
            this.GroupName = groupName;
            this.EstimatedArticleCount = estimatedArticleCount;
            this.LowWatermark = lowWatermark;
            this.HighWatermark = highWatermark;
            this.ArticleNumbers = articleNumbers;
        }
    }
}