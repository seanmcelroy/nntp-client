using System.Collections.ObjectModel;

namespace mcnntp.common
{
    public class NewsgroupsListResponse : NntpResponse
    {
        public struct GroupEntry
        {
            public string Group { get; set; }
            public string Description { get; set; }
        }

        public ReadOnlyCollection<GroupEntry> Groups { get; private set; }

        public NewsgroupsListResponse(int code, string message, ReadOnlyCollection<GroupEntry> groups) : base(code, message)
        {
            this.Groups = groups;
        }
    }
}