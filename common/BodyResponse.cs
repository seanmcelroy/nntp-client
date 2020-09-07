using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;

namespace mcnntp.common
{
    public class BodyResponse : NntpResponse
    {
        public ReadOnlyCollection<string> Lines { get; private set; }

        public BodyResponse(int code, string? message, ReadOnlyCollection<string>? lines) : base(code, message)
        {
            this.Lines = lines ?? new ReadOnlyCollection<string>(new string[0]);
        }

        public BodyResponse(int code, string? message, IEnumerable<string> lines) : base(code, message)
        {
            this.Lines = new ReadOnlyCollection<string>(lines.ToList());
        }
    }
}