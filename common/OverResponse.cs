using mcnntp.common.client;

namespace mcnntp.common
{
    public class OverResponse : NntpResponse
    {
        public int ArticleNumber { get; set; }
        public string? Subject { get; set; }
        public string? From { get; set; }
        public string? Date { get; set; }
        public string? MessageID { get; set; }
        public string? References { get; set; }
        public int Bytes { get; set; }
        public int Lines { get; set; }

        private OverResponse(
            int code,
            string? message,
            int articleNumber,
            string? subject,
            string? from,
            string? date,
            string? messageId,
            string references,
            int bytes,
            int lines) : base(code, message)
        {
            ArticleNumber = articleNumber;
            Subject = subject;
            From = from;
            Date = date;
            MessageID = messageId;
            References = references;
            Bytes = bytes;
            Lines = lines;
        }

        public static OverResponse? Parse(int code, string? message, string? line)
        {
            if (line == null)
                return null;

            var parts = line.Split('\t');
            int articleNumber;
            if (!int.TryParse(parts[0], out articleNumber))
                throw new NntpException(string.Format("No article number provided in OVER response"));

            int bytes, lines;
            return new OverResponse(code, message,
                articleNumber,
                parts[1],
                parts[2],
                parts[3],
                parts[4],
                parts[5],
                int.TryParse(parts[6], out bytes) ? bytes : 0,
                int.TryParse(parts[7], out lines) ? lines : 0);
        }
    }
}