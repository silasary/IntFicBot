namespace IntFicBot
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text.RegularExpressions;
    using System.Threading;
    using AngleSharp.Dom.Html;
    using AngleSharp.Parser.Html;
    using Newtonsoft.Json;
    using TweetSharp;

    internal class Program
    {
        private const string SearchPage = "http://ifdb.tads.org/search?sortby=rand&newSortBy.x=0&newSortBy.y=0&searchfor=tag%3Ai7+source+available";
        private static WebClient webClient = new WebClient();

        private static TwitterService service;
        private static OAuthAccessToken access;

        private static void Main(string[] args)
        {
            var appInfo = new
            {
                CLIENT_ID = Environment.GetEnvironmentVariable("CLIENT_ID"),
                CLIENT_SECRET = Environment.GetEnvironmentVariable("CLIENT_SECRET")
            };

            if (File.Exists("AppAuth.json"))
            {
               appInfo = JsonConvert.DeserializeAnonymousType(File.ReadAllText("AppAuth.json"), appInfo);
            }

            if (appInfo.CLIENT_ID == null || appInfo.CLIENT_SECRET == null)
            {
                string id = appInfo.CLIENT_ID;
                if (appInfo.CLIENT_ID == null)
                {
                    Console.Write("AppID: ");
                    id = Console.ReadLine();
                }

                string secret = appInfo.CLIENT_SECRET;
                if (appInfo.CLIENT_SECRET == null)
                {
                    Console.Write("Secret: ");
                    secret = Console.ReadLine();
                }

                appInfo = new
                {
                    CLIENT_ID = id,
                    CLIENT_SECRET = secret
                };
                File.WriteAllText("AppAuth.json", JsonConvert.SerializeObject(appInfo));
            }


            service = new TwitterService(appInfo.CLIENT_ID, appInfo.CLIENT_SECRET);
            access = null;
            if (File.Exists("token.json"))
            {
                access = service.Deserialize<OAuthAccessToken>(File.ReadAllText("token.json"));
            }

            if (access == null)
            {
                OAuthRequestToken requestToken = service.GetRequestToken();
                Uri uri = service.GetAuthorizationUri(requestToken);
                Console.WriteLine(uri.ToString());
                var verifier = Console.ReadLine();
                access = service.GetAccessToken(requestToken, verifier);
                var tokenstr = service.Serializer.Serialize(access, typeof(OAuthAccessToken));
                File.WriteAllText("token.json", tokenstr);
            }

            service.AuthenticateWith(access.Token, access.TokenSecret);

            Console.WriteLine($"Authenticated as {access.ScreenName}");

            foreach (string gamelink in RandomGames())
            {
                string source;
                try
                {
                    var link = gamelink.Replace("about://", "");
                    source = GetSourceFromGamePage(link);
                    if (source == null)
                    {
                        continue;
                    }

                    Console.WriteLine();
                    Console.WriteLine(source);
                }
                catch (Exception)
                {
                    Thread.Sleep(1000);
                    continue;
                }

                try
                {
                    switch (Path.GetExtension(source))
                    {
                        case ".txt":
                        case ".ni":

                            ReadSource(source);
                            break;
                        case ".html":
                            //todo: Deal with HTML source files.
                            ReadSource(source.Replace(".html", ".txt"));
                            break;
                        case ".zip":
                        case ".sit": 
                            break;
                        default:
                            // Try anyway?
                            ReadSource(source);
                            break;
                    }
                }
                catch (WebException c)
                {
                    // 404 or some such error. Go to the Next story.
                    continue;
                }
            }
        }

        private static void ReadSource(string url)
        {
            var source = webClient.DownloadString(url);
            var verify = new Regex("^\".+?\" by ", RegexOptions.Compiled);
            if (!verify.Match(source).Success) // Easiest way to verify the link isn't broken.
            {
                return;
            }

            var AllMatches = new List<Match>();
            var theDescription = new Regex("The description of (?<name>[\\w ]+) is \"(?<desc>.*?)\"", RegexOptions.Compiled);
            var descriptions = theDescription.Matches(source);
            AllMatches.AddRange(descriptions.Cast<Match>());

            theDescription = new Regex("(The )?(?<name>[\\w ]+) is (a|on) (?<type>[\\w ]+). +(The )?description is \"(?<desc>.*?)\"", RegexOptions.Compiled);
            descriptions = theDescription.Matches(source);
            AllMatches.AddRange(descriptions.Cast<Match>());

            theDescription = new Regex(@"(?<name>[\w ]+) is a scene. \k<name> begins when (?<start>.+?)\.", RegexOptions.Compiled);
            descriptions = theDescription.Matches(source);
            AllMatches.AddRange(descriptions.Cast<Match>());

            List<Match> sorted = new List<Match>();
            var rand = new Random();
            var i = 0;
            foreach (Match desc in AllMatches)
            {
                sorted.Insert(rand.Next(i++), desc);
            }

            i = 0;
            foreach (var desc in sorted)
            {
                TweetDescription(desc);
                if (i++ > 4)
                {
                    return; // next story.
                }
            }
        }

        private static void TweetDescription(Match description)
        {
            var text = Markup(description);
            Console.WriteLine();
            Console.WriteLine(text);

            if (text.Length <= 140)
            {
                string status = text;
                service.SendTweet(new SendTweetOptions() { Status = status });

            }
            else if (text.Length > 140 * 2)
            {
                service.SendTweet(new SendTweetOptions() { Status = text.Substring(0, 139) + "…" });
            }
            else
            {
                var first = text.Substring(0, 140);
                first = first.Substring(0, first.LastIndexOf(' '));
                bool elipsis = false;

                if (first.LastIndexOf('.') > 100)
                {
                    first = first.Substring(0, first.LastIndexOf('.') + 1);
                }
                else if (first.LastIndexOf('?') > 100)
                {
                    first = first.Substring(0, first.LastIndexOf('?') + 1);
                }
                else
                {
                    elipsis = true;
                }

                var second = (elipsis ? "…" : string.Empty) + text.Substring(first.Length);
                if (second.Length > 40)
                {
                    var a = service.SendTweet(new SendTweetOptions() { Status = first });
                    service.SendTweet(new SendTweetOptions() { Status = second, InReplyToStatusId = a.Id });
                }
                else if (first.LastIndexOf('.') != -1)
                {
                    service.SendTweet(new SendTweetOptions() { Status = first.Substring(0, first.LastIndexOf('.')) });
                }
                else
                {
                    service.SendTweet(new SendTweetOptions() { Status = first + "…" });
                }
            }

            DateTime sleepUntil = DateTime.Now.AddMinutes(30 - DateTime.Now.Minute % 30);

            var stime = sleepUntil.Subtract(DateTime.Now);
            while (stime.TotalSeconds > 0)
            {
                Console.WriteLine("Sleeping {0} min...", stime.TotalMinutes);
                Thread.Sleep(stime);
                stime = sleepUntil.Subtract(DateTime.Now);
            }
        }

        private static string Markup(Match description)
        {
            var value = description.Value;

            // Line Break
            value = value.Replace("[p]", "\n").Replace("[para]", "\n").Replace("[br]", "\n");
            value = value.Replace("[line break]", "\n");

            // Empty Substitutions. These text substitutions produces no text.
            value = value.Replace("[no line break]", "").Replace("[run paragraph on]", "");

            // Punctuation
            value = value.Replace("[apostrophe]", "[.]'") /*.Replace("[quotation mark]", "\"").Replace("[']", "'").*/;
            value = value.Replace("[--]", "—");

            // Maybe TODO: http://inform7.com/learn/man/WI_5_9.html

            return value;
        }

        private static string GetSourceFromGamePage(string link)
        {
            var parser = new HtmlParser();
            var uri = new Uri(new Uri("http://ifdb.tads.org"), link);
            var infoPage = webClient.DownloadString(uri);
            var document = parser.Parse(infoPage);
            var downloads = document.All.Where(e => e.ClassName == "downloaditem").ToArray();
            foreach (var item in downloads)
            {
                var title = item.QuerySelectorAll("b").FirstOrDefault().TextContent;
                if (title.ToLower().Contains("source"))
                {
                    return (item.QuerySelectorAll("a").First() as IHtmlAnchorElement).Href;
                }
            }

            return null;
        }

        /// <summary>
        /// Find a game with i7 Source available, and return a link to it.
        /// </summary>
        /// <returns>Returns an endless list of links to games</returns>
        private static IEnumerable<string> RandomGames()
        {
            var webClient = new WebClient();
            var parser = new HtmlParser();
            string search;

        top:
            try
            {
                search = webClient.DownloadString(SearchPage);
            }
            catch (WebException c)
            {
                Thread.Sleep(1000);
                goto top;
            }

            var document = parser.Parse(search);
            var main = document.All.FirstOrDefault(m => m.ClassName == "main");
            var games = main.Children.Where(p => p.TagName == "P");
            foreach (var game in games)
            {
                var links = game.QuerySelectorAll("a").ToArray();
                var gamelink = (links.First() as IHtmlAnchorElement).Href;
                yield return gamelink;
            }

            goto top;
        }
    }
}
