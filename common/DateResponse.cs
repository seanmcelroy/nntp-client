using System;

namespace mcnntp.common
{
    public class DateResponse : NntpResponse
    {
        public DateTime? DateTime { get; private set; }

        public DateResponse(int code, string? message, DateTime? dateTime = null) : base(code, message)
        {
            this.DateTime = dateTime;
        }
    }
}