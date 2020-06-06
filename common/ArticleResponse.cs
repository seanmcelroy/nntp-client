using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;

namespace mcnntp.common
{
    public class ArticleResponse : NntpResponse
    {
        public ReadOnlyCollection<string> Lines { get; private set; }

        public ArticleResponse(int code, string message, ReadOnlyCollection<string> lines) : base(code, message)
        {
            this.Lines = lines;
        }

        public ArticleResponse(int code, string message, IEnumerable<string> lines) : base(code, message)
        {
            this.Lines = new ReadOnlyCollection<string>(lines.ToList());
        }

        public string GetHeaderValue(string headerName)
        {
            if (this.Lines == null)
                throw new InvalidOperationException("No lines are part of this response");

            foreach (var line in Lines)
            {
                // Break between header and body
                if (string.IsNullOrEmpty(line))
                    return null;

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