using System;
using System.Linq;
using System.Threading;
using mcnntp.common.client;

namespace mcnntp.client
{
    class Program
    {
        static void Main(string[] args)
        {
            var host = args.Length > 0 ? args[0] : "news.fysh.org";
            Console.WriteLine("Hello World!");

            var cts = new CancellationTokenSource();

            var client = new NntpClient();
            try
            {
                Console.WriteLine($"Connecting to {host}...");
                var connectTask = client.ConnectAsync(host).Result;
                Console.WriteLine($"Connected to {host}");

                Console.WriteLine($"\r\nRetrieving server capabilities");
                var caps = client.GetCapabilitiesAsync(cts.Token).Result;
                foreach (var cap in caps)
                    Console.WriteLine($"\t{cap}");

                var newsgroups = client.GetNewsgroupsListAsync(cts.Token).Result;

                Console.WriteLine($"\r\nGroups count={newsgroups.Count}");
                foreach (var ng in newsgroups)
                    Console.WriteLine($"\t{ng}");

                Console.WriteLine($"\r\nRetrieving news...");
                foreach (var ng in newsgroups)
                {
                    Console.WriteLine($"\tGroup summary for {ng}");
                    var group = client.GetGroupAsync(ng, cts.Token).Result;
                    Console.WriteLine($"\t\tEstimated article count: {group.EstimatedArticleCount}");
                    Console.WriteLine($"\t\tLow watermark: {group.LowWatermark}");
                    Console.WriteLine($"\t\tHigh watermark: {group.HighWatermark}");

                    if (group.HighWatermark == group.LowWatermark - 1)
                    {
                        Console.WriteLine("\t\t--- EMPTY GROUP WITH NO ARTICLES ---");
                    }
                    else
                    {
                        var groupList = client.GetGroupListAsync(ng, null, null, cts.Token).Result;
                        Console.WriteLine($"\t\tLISTGROUP also called low {groupList.LowWatermark}~={group.LowWatermark}");

                        Console.WriteLine($"\tNews for {ng}");
                        var overResponse = client.OverAsync(group.LowWatermark, group.HighWatermark, cts.Token).Result;
                        Console.Write($"\t\tArticle count={overResponse.Count}");
                        foreach (var over in overResponse.Take(2))
                        {
                            Console.WriteLine($"\t\t#{over.ArticleNumber}: {over.Subject}");
                            var articleByNumber = client.ArticleAsync(over.ArticleNumber, cts.Token).Result;
                            Console.WriteLine($"\t\t\t({articleByNumber.Code}) ARTICLE:\r\n{articleByNumber.Lines.Take(50).Aggregate((c, n) => c + "\r\n\t\t\t" + n)}");

                            var messageId = articleByNumber.GetHeaderValue("Message-ID");

                            var articleByMessageId = client.ArticleAsync(messageId, cts.Token).Result;
                            Console.WriteLine($"\t\t\t\t({articleByMessageId.Code}) ARTICLE: {articleByNumber.Lines.Take(5).Aggregate((c, n) => c + "\r\n\t\t\t" + n)}");

                            var articleCurrent = client.ArticleAsync(cts.Token).Result;
                            var messageIdCurrent = articleCurrent.GetHeaderValue("Message-ID");
                            Console.WriteLine($"\t\t\t\tArticle-Num={messageId} ~= Article-Current={messageIdCurrent}");
                        }

                        // Test some other commands
                        var next = client.LastAsync(cts.Token).Result;
                        Console.WriteLine($"\t\tNEXT ({next.Code}) article={next.ArticleNumber}, messageId={next.MessageId}");

                        var last = client.LastAsync(cts.Token).Result;
                        Console.WriteLine($"\t\tLAST ({last.Code}) article={last.ArticleNumber}, messageId={last.MessageId}");
                    }
                }
            }
            catch (ArgumentException aex)
            {
                Console.Error.WriteLine($"Caught ArgumentException: {aex.Message}");
                return;
            }
            catch (AggregateException agg)
            {
                Console.Error.WriteLine($"Caught AggregateException: {agg.Message}");
                foreach (var ex in agg.InnerExceptions)
                    Console.Error.WriteLine($"...InnerException: {ex.Message}: {ex}");
                return;
            }

            var quitResponse = client.DisconnectAsync().Result;
            Console.Out.WriteLine($"Closed connection: {quitResponse}");
        }
    }
}
