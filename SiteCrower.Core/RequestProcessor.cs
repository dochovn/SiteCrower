﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace SiteCrower.Core
{
    // TODO: Make it asyncronous
    public class RequestProcessor
    {
        private readonly string pattern = "href=\"";
        private readonly string[] escapeLinks = new string[] { "#", "/", "//" };

        private Queue<string> links;
        private WebClient webClient;
        private IList<string> visited;
        private LinkDispatcher linkDispatcher;
        private readonly string root;

        private DateTime startTime;

        public event EventHandler<ProcessResult> RequestProceed;
        public event EventHandler<TimeSpan> Finished;

        /// <summary>
        /// Miliseconds
        /// </summary>
        public TimeSpan AvgResponseTime { get; private set; }

        /// <summary>
        /// KB / s
        /// </summary>
        public int AvgDownloadSpeed { get; private set; }

        public RequestProcessor(string siteRoot)
        {
            this.webClient = new WebClient();
            this.root = this.EnsureProtocol(siteRoot);
            this.links = new Queue<string>(new string[] { root });
            this.visited = new List<string>(); ;
            this.linkDispatcher = new LinkDispatcher(root);
        }

        public void Start()
        {
            startTime = DateTime.Now;

            string linkToVisit;
            string content;
            string childLink;
            int openIndex;
            int closeIndex;
            DateTime requestStart;

            while (this.links.Count > 0)
            {
                linkToVisit = this.links.Dequeue();
                requestStart = DateTime.Now;
                ProcessResult result = new ProcessResult() { Url = linkToVisit };

                try
                {
                    linkToVisit = this.linkDispatcher.DecorateUrl(linkToVisit);
                    content = this.webClient.DownloadString(linkToVisit);

                    // Summary data
                    result.Finished = DateTime.Now - requestStart;
                    int kbPerSecond = (int)(((content.Length * sizeof(char)) / 1024) / result.Finished.TotalSeconds);
                    this.AvgDownloadSpeed = (this.AvgDownloadSpeed + kbPerSecond) / 2;
                    this.AvgResponseTime = TimeSpan.FromMilliseconds((this.AvgResponseTime.TotalMilliseconds + result.Finished.TotalMilliseconds) / 2);

                    openIndex = content.IndexOf(pattern) + pattern.Length;
                    while (openIndex != -1)
                    {
                        closeIndex = content.IndexOf("\"", openIndex + pattern.Length);
                        childLink = content.Substring(openIndex + pattern.Length, closeIndex - openIndex - pattern.Length);
                        if (ShouldVisit(childLink))
                        {
                            this.links.Enqueue(childLink);
                            this.visited.Add(childLink);
                        }

                        openIndex++;
                        openIndex = content.IndexOf(pattern, openIndex);
                    }

                    result.Status = ProcessResultStatus.Ok;
                }
                catch (WebException)
                {
                    result.Status = ProcessResultStatus.Fail;
                }
                catch (Exception)
                {
                    result.Status = ProcessResultStatus.Error;
                }

                this.RequestProceed?.Invoke(this, result);
            }

            this.Finished?.Invoke(this, DateTime.Now - startTime);
        }

        /// <summary>
        /// Visit only links in the current domain
        /// </summary>
        private bool ShouldVisit(string link)
        {
            if (!string.IsNullOrEmpty(link) && !escapeLinks.Contains(link) && !visited.Contains(link))
            {
                if (this.linkDispatcher.GetDomain(link) == this.linkDispatcher.GetDomain(root))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Defines if the root is https, http or invalid
        /// </summary>
        private string EnsureProtocol(string siteRoot)
        {
            if (siteRoot.Contains(Constants.Http))
                return siteRoot;

            try
            {
                try
                {
                    string https = $"{Constants.Https}{siteRoot}";
                    this.webClient.DownloadString(https);

                    return https;
                }
                catch (WebException)
                {
                    string http = $"{Constants.Http}{siteRoot}";
                    this.webClient.DownloadString(http);

                    return http;
                }

            }
            catch (WebException webEx)
            {
                throw new ApplicationException("Invalid root address", webEx);
            }
        }
    }
}
