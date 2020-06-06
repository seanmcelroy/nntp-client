namespace mcnntp.common
{
    public class OverResponse : NntpResponse
    {
        public int ArticleNumber { get; set; }
        public string Subject { get; set; }
        public string From { get; set; }
        public string Date { get; set; }
        public string MessageID { get; set; }
        public string References { get; set; }
        public int Bytes { get; set; }
        public int Lines { get; set; }

        public OverResponse(int code, string message) : base(code, message)
        {            
        }
    }
}