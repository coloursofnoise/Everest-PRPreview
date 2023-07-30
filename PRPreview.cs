using Celeste.Mod.Helpers;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text.RegularExpressions;
using System.Linq;

namespace Celeste.Mod.PRPreview {
    public class PRPreviewModule : EverestModule {

        class PRSource {
            public string PR;
            public string PRTitle;
            public Everest.Updater.Source Source;
        }

        private const string BUILD_API = "https://dev.azure.com/EverestAPI/Everest/_apis/build/builds";

        private static readonly string BuildList = new URIHelper(BUILD_API, new NameValueCollection() {
            {"definitions", "3"},
            {"reasonFilter", "pullRequest"},
            {"statusFilter", "completed"},
            {"resultsFilter", "succeeded"},
            {"api-version", "5.0"},
        }).ToString();

        private Dictionary<string, PRSource> updaterSources = new();

        public override void Load() {
#if DEBUG
            Logger.SetLogLevel("PRPreview", LogLevel.Debug);
#else
            Logger.SetLogLevel("PRPreview", LogLevel.Info);
#endif

            if (!Everest.Flags.SupportUpdatingEverest) {
                Logger.Log(LogLevel.Warn, "PRPreview", Dialog.Clean("EVERESTUPDATER_NOTSUPPORTED"));
                return;
            }

            string data;
            try {
                Logger.Log(LogLevel.Debug, "PRPreview", "Attempting to download Everest PR build list");
                using (WebClient wc = new WebClient()) {
                    wc.Headers.Add("User-Agent", "Everest/" + Everest.VersionString);
                    data = wc.DownloadString(BuildList);
                }
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "PRPreview", "Failed requesting build list: " + e.ToString());
                return;
            }

            try {
                JArray builds = JObject.Parse(data)["value"] as JArray;
                foreach (JObject build in builds) {
                    string sourceBranch;
                    string pr;
                    string prTitle = "";

                    try {
                        sourceBranch = build["sourceBranch"].ToString();
                        if (updaterSources.ContainsKey(sourceBranch))
                            continue;

                        pr = Regex.Match(sourceBranch, @"^refs/pull/(\d+)/merge$").Groups[1].Value;
                        try { prTitle = build.SelectToken("triggerInfo['pr.title']").ToString(); } catch { }
                    } catch (Exception e) {
                        Logger.Log(LogLevel.Error, "PRPreview", "Failed parsing information for build: " + e.ToString());
                        continue;
                    }

                    updaterSources.Add(sourceBranch, new PRSource {
                        PR = pr,
                        PRTitle = prTitle,
                        Source = new Everest.Updater.Source {
                            Name = "PR #" + pr,
                            Description = string.IsNullOrWhiteSpace(prTitle) ? Dialog.Clean("updater_src_buildbot_azure") : prTitle,

                            UpdatePriority = Everest.Updater.UpdatePriority.High,

                            Index = new URIHelper(BUILD_API, new() {
                                { "definitions", "3" },
                                { "branchName", sourceBranch },
                                { "statusFilter", "completed" },
                                { "resultsFilter", "succeeded" },
                                { "api-version", "5.0" },
                            }).ToString,
                            ParseData = Everest.Updater.AzureBuildsParser("https://dev.azure.com/EverestAPI/Everest/_apis/build/builds/{0}/artifacts?artifactName=main&api-version=5.0&%24format=zip", offset: 700)
                        }
                    });
                }
            } catch (Exception e) {
                Logger.Log(LogLevel.Error, "PRPreview", "Something went wrong adding update source for Everest PRs:" + e.ToString());
                return;
            }

            foreach (PRSource source in updaterSources.Values.OrderByDescending(source => int.TryParse(source.PR, out int pr) ? pr : 0)) {
                Everest.Updater.Sources.Add(source.Source);
            }
        }

        public override void Unload() { }

    }

}