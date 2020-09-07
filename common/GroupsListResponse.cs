using System.Collections.ObjectModel;

namespace mcnntp.common
{
    public class GroupsListResponse : NntpResponse
    {
        public struct GroupEntry
        {
            public string Group { get; set; }
            public int HighWatermark { get; set; }
            public int LowWatermark { get; set; }
            public char Status { get; set; }
        }

        public ReadOnlyCollection<GroupEntry> Groups { get; private set; }

        public GroupsListResponse(int code, string? message, ReadOnlyCollection<GroupEntry>? groups) : base(code, message)
        {
            this.Groups = groups ?? new ReadOnlyCollection<GroupEntry>(new GroupEntry[0]);
        }
    }
}