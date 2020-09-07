using System.Collections.ObjectModel;

namespace mcnntp.common
{
    public class GroupsListTimesResponse : NntpResponse
    {
        public struct GroupTimeEntry
        {
            public string Group { get; set; }
            public int EpochSeconds { get; set; }
            public string Creator { get; set; }
        }

        public ReadOnlyCollection<GroupTimeEntry> Groups { get; private set; }

        public GroupsListTimesResponse(int code, string? message, ReadOnlyCollection<GroupTimeEntry>? groups) : base(code, message)
        {
            this.Groups = groups ?? new ReadOnlyCollection<GroupTimeEntry>(new GroupTimeEntry[0]);
        }
    }
}