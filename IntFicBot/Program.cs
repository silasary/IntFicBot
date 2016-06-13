﻿using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TweetSharp;

namespace IntFicBot
{
    class Program
    {
        private static WebClient webClient = new WebClient();
        private const string SearchPage = "http://ifdb.tads.org/search?sortby=rand&newSortBy.x=0&newSortBy.y=0&searchfor=tag%3Ai7+source+available";

        private static TwitterService service;
        private static OAuthAccessToken access;

        private static void Main(string[] args)
        {

            var AppInfo = new
            {
                CLIENT_ID = Environment.GetEnvironmentVariable("CLIENT_ID"),
                CLIENT_SECRET = Environment.GetEnvironmentVariable("CLIENT_SECRET")
            };

            if (File.Exists("AppAuth.json"))
            {
               AppInfo = JsonConvert.DeserializeAnonymousType(File.ReadAllText("AppAuth.json"), AppInfo);
            }
            else
            {
                File.WriteAllText("AppAuth.json", JsonConvert.SerializeObject(AppInfo));
            }


            service = new TwitterService(AppInfo.CLIENT_ID, AppInfo.CLIENT_SECRET);
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
                var link = gamelink.Replace("about://", "");
                string source;
                source = GetSourceFromGamePage(link);
                if (source == null)
                    continue;
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
                return;
            var theDescription = new Regex("The description of (?<name>[\\w ]+) is \"(?<desc>.*?)\"");
            var descriptions = theDescription.Matches(source);

            List<Match> sorted = new List<Match>();
            var rand = new Random();
            var i = 0;
            foreach (Match desc in descriptions)
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
            Console.WriteLine(description);

            if (description.Length <= 140)
            {
                string status = description.Value;
                service.SendTweet(new SendTweetOptions() { Status = status });

            }
            else if (description.Length > 140 * 2)
            {
                service.SendTweet(new SendTweetOptions() { Status = description.Value.Substring(0, 139) + "…" });
            }
            else
            {
                var first = description.Value.Substring(0, 140);
                first = first.Substring(0, first.LastIndexOf(' '));
                if (first.LastIndexOf('.') > 100)
                {
                    first = first.Substring(0, first.LastIndexOf('.') + 1);
                }

                var second = "…" + description.Value.Substring(first.Length);
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

            Thread.Sleep(new TimeSpan(0, 1, 0));
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

            top:
            var search = webClient.DownloadString(SearchPage);
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