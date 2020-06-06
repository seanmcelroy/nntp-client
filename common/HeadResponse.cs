using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;

namespace mcnntp.common
{
    public class HeadResponse : NntpResponse
    {
        public ReadOnlyCollection<string> Lines { get; private set; }

        public HeadResponse(int code, string message, ReadOnlyCollection<string> lines) : base(code, message)
        {
            this.Lines = lines;
        }

        public HeadResponse(int code, string message, IEnumerable<string> lines) : base(code, message)
        {
            this.Lines = new ReadOnlyCollection<string>(lines.ToList());
        }

        public string GetHeaderValue(string headerName)
        {
            if (this.Lines == null)
                throw new InvalidOperationException("No lines are part of this response");

            foreach (var line in Lines)
            {
                if (line.StartsWith($"{headerName}: ", StringComparison.OrdinalIgnoreCase))
                {
                    var ret = line.Substring($"{headerName}: ".Length);
                    return ret;
                }
            }

            return null;
        }
    }
}