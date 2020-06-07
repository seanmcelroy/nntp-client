using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace mcnntp.common
{
    public class NewNewsResponse : NntpResponse
    {
        public ReadOnlyCollection<string> MessageIds { get; private set; }

        public NewNewsResponse(int code, string message, ReadOnlyCollection<string> messageIds) : base(code, message)
        {
            this.MessageIds = messageIds;
        }

        public NewNewsResponse(int code, string message, IEnumerable<string> messageIds) : base(code, message)
        {
            this.MessageIds = new ReadOnlyCollection<string>(messageIds.ToList());
        }
    }
}