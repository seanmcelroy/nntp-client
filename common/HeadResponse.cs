using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace mcnntp.common
{
    public class HeadResponse : NntpResponse
    {
        public ReadOnlyCollection<string> Lines { get; private set; }

        public HeadResponse(int code, string? message, ReadOnlyCollection<string>? lines) : base(code, message)
        {
            this.Lines = lines ?? new ReadOnlyCollection<string>(new string[0]);
        }

        public HeadResponse(int code, string? message, IEnumerable<string>? lines) : base(code, message)
        {
            this.Lines = new ReadOnlyCollection<string>(lines?.ToArray() ?? new string[0]);
        }

        public IEnumerable<KeyValuePair<string, string>> GetHeaders() => StringUtility.GetHeaders(this.Lines);

        public IEnumerable<string> GetHeaderValues(string headerName)
        {
            if (this.Lines == null)
                throw new InvalidOperationException("No lines are part of this response");

            foreach (var kvp in GetHeaders())
                if (string.Compare(kvp.Key, headerName, StringComparison.OrdinalIgnoreCase) == 0)
                    yield return kvp.Value;
        }
    }
}