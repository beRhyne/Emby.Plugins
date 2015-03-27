﻿using EmbyTV.EPGProvider.Responses;
using EmbyTV.GeneralHelpers;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EmbyTV.EPGProvider
{
    public class SchedulesDirect : IEpgSupplier
    {
        public string username;
        public string _lineup;
        private string password;
        private string token;
        private string apiUrl;
        private Dictionary<string, ScheduleDirect.Station> channelPair;
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IHttpClient _httpClient;

        public SchedulesDirect(string username, string password, string lineup, ILogger logger, IJsonSerializer jsonSerializer, IHttpClient httpClient)
        {
            this.username = username;
            this.password = password;
            this._lineup = lineup;
            _logger = logger;
            _jsonSerializer = jsonSerializer;
            _httpClient = httpClient;
            apiUrl = "https://json.schedulesdirect.org/20141201";
        }

        public async Task getToken()
        {

            if (username.Length > 0 && password.Length > 0)
            {
                var httpOptions = new HttpRequestOptions()
                {
                    Url = apiUrl + "/token",
                    UserAgent = "Emby-Server",
                    RequestContent = "{\"username\":\"" + username + "\",\"password\":\"" + password + "\"}",
                };
                _logger.Info("Obtaining token from Schedules Direct from addres: " + httpOptions.Url + " with body " + httpOptions.RequestContent);
                using (var responce = await _httpClient.Post(httpOptions))
                {
                    var root = _jsonSerializer.DeserializeFromStream<ScheduleDirect.Token>(responce.Content);
                    if (root.message == "OK") { token = root.token; _logger.Info("Authenticated with Schedules Direct token: " + token); }
                    else { throw new ApplicationException("Could not authenticate with Schedules Direct Error: " + root.message); }
                }
            }
        }

        public async Task refreshToken()
        {
            if (username.Length > 0 && password.Length > 0)
            {
                var httpOptions = new HttpRequestOptions()
                {
                    Url = apiUrl + "/token",
                    UserAgent = "Emby-Server",
                    RequestContent = "{\"username\":\"" + username + "\",\"password\":\"" + password + "\"}",
                };

                _logger.Info("Obtaining token from Schedules Direct from addres: " + httpOptions.Url + " with body " + httpOptions.RequestContent);
                using (var response = await _httpClient.Post(httpOptions))
                {
                    var root = _jsonSerializer.DeserializeFromStream<ScheduleDirect.Token>(response.Content);
                    if (root.message == "OK") { token = root.token; _logger.Info("Authenticated with Schedules Direct token: " + token); }
                    else { throw new ApplicationException("Could not authenticate with Schedules Direct Error: " + root.message); }
                }
            }
        }

        public async Task<IEnumerable<ChannelInfo>> getChannelInfo(IEnumerable<ChannelInfo> channelsInfo)
        {
            if (username.Length > 0 && password.Length > 0)
            {
                if (apiUrl != "https://json.schedulesdirect.org/20141201") { apiUrl = "https://json.schedulesdirect.org/20141201"; await refreshToken(); }
                else { await getToken(); }
                if (!String.IsNullOrWhiteSpace(_lineup))
                {
                    var httpOptions = new HttpRequestOptionsMod()
                    {
                        Url = apiUrl + "/lineups/" + _lineup,
                        UserAgent = "Emby-Server",
                        Token = token
                    };
                    channelPair = new Dictionary<string, ScheduleDirect.Station>();
                    using (var response = await _httpClient.Get(httpOptions))
                    {
                        var root = _jsonSerializer.DeserializeFromStream<ScheduleDirect.Channel>(response);
                        _logger.Info("Found " + root.map.Count() + " channels on the lineup on ScheduleDirect");
                        _logger.Info("Mapping Stations to Channel");
                        foreach (ScheduleDirect.Map map in root.map)
                        {
                            channelPair.Add(map.channel.TrimStart('0'), root.stations.First(item => item.stationID == map.stationID));
                        }
                        _logger.Info("Added " + channelPair.Count() + " channels to the dictionary");
                        string channelName;
                        foreach (ChannelInfo channel in channelsInfo)
                        {
                            //  Helper.logger.Info("Modifyin channel " + channel.Number);
                            if (channelPair.ContainsKey(channel.Number.TrimStart('0')))
                            {
                                if (channelPair[channel.Number].logo != null) { channel.ImageUrl = channelPair[channel.Number].logo.URL; channel.HasImage = true; }
                                if (channelPair[channel.Number].affiliate != null) { channelName = channelPair[channel.Number].affiliate; }
                                else { channelName = channelPair[channel.Number].name; }
                                channel.Name = channelName;
                                //channel.Id = channelPair[channel.Number].stationID;
                            }
                            else { _logger.Info("Schedules Direct doesnt have data for channel: " + channel.Number + " " + channel.Name); }
                        }
                    }
                }
            }
            return channelsInfo;
        }

        public async Task<IEnumerable<ProgramInfo>> getTvGuideForChannel(string channelNumber, DateTime start, DateTime end)
        {
            if (!String.IsNullOrWhiteSpace(_lineup) && username.Length > 0 && password.Length > 0)
            {
                if (apiUrl != "https://json.schedulesdirect.org/20141201") { apiUrl = "https://json.schedulesdirect.org/20141201"; await refreshToken(); }
                else { await getToken(); }
                HttpRequestOptionsMod httpOptions = new HttpRequestOptionsMod()
                {
                    Url = apiUrl + "/schedules",
                    UserAgent = "Emby-Server",
                    Token = token
                };
                _logger.Info("Schedules 1");
                List<string> dates = new List<string>();
                int numberOfDay = 0;
                DateTime lastEntry = start;
                while (lastEntry != end)
                {
                    lastEntry = start.AddDays(numberOfDay);
                    dates.Add(lastEntry.ToString("yyyy-MM-dd"));
                    numberOfDay++;
                }
                _logger.Info("Schedules dates is null?" + (dates != null || dates.All(x => string.IsNullOrWhiteSpace(x))));
                _logger.Info("Date count?" + dates[0]);

                string stationID = channelPair[channelNumber].stationID;
                _logger.Info("Channel ?" + stationID);
                List<ScheduleDirect.RequestScheduleForChannel> requestList =
                    new List<ScheduleDirect.RequestScheduleForChannel>() {
                        new ScheduleDirect.RequestScheduleForChannel() {
                            stationID = stationID, date = dates 
                        } 
                    };

                _logger.Info("Schedules 3");
                _logger.Info("Request string for schedules is: " + _jsonSerializer.SerializeToString(requestList));
                httpOptions.RequestContent = _jsonSerializer.SerializeToString(requestList);
                _logger.Info("Schedules 5");
                using (var response = await _httpClient.Post(httpOptions))
                {
                    StreamReader reader = new StreamReader(response.Content);
                    string responseString = reader.ReadToEnd();
                    _logger.Info("Schedules 6");
                    responseString = "{ \"days\":" + responseString + "}";
                    var root = _jsonSerializer.DeserializeFromString<ScheduleDirect.Schedules>(responseString);
                    // Helper.logger.Info("Found " + root.Count() + " programs on "+channelNumber +" ScheduleDirect");
                    List<ProgramInfo> programsInfo = new List<ProgramInfo>();
                    httpOptions = new HttpRequestOptionsMod()
                    {
                        Url = apiUrl + "/programs",
                        UserAgent = "Emby-Server",
                        Token = token
                    };
                    // httpOptions.SetRequestHeader("Accept-Encoding", "deflate,gzip");
                    httpOptions.EnableHttpCompression = true;
                    string requestBody = "";
                    List<string> programsID = new List<string>();
                    List<string> imageID = new List<string>();
                    Dictionary<string, List<string>> haveImageID = new Dictionary<string, List<string>>();
                    foreach (ScheduleDirect.Day day in root.days)
                    {
                        foreach (ScheduleDirect.Program schedule in day.programs)
                        {
                            var imageId = schedule.programID.Substring(0, 10);
                            programsID.Add(schedule.programID);
                            imageID.Add(imageId);

                            if (!haveImageID.ContainsKey(imageId))
                            {
                                haveImageID.Add(imageId, new List<string>());
                            }
                            if (!haveImageID[imageId].Contains(schedule.programID))
                            {
                                haveImageID[imageId].Add(schedule.programID);
                            }
                        }
                    }
                    _logger.Info("finish creating dict: ");

                    programsID = programsID.Distinct().ToList();
                    imageID = imageID.Distinct().ToList();


                    requestBody = "[\"" + string.Join("\", \"", programsID) + "\"]";
                    httpOptions.RequestContent = requestBody;
                    using (var innerResponse = await _httpClient.Post(httpOptions))
                    {
                        using (var innerReader = new StreamReader(innerResponse.Content))
                        {
                            responseString = innerReader.ReadToEnd();
                            responseString = "{ \"result\":" + responseString + "}";
                            var programDetails = _jsonSerializer.DeserializeFromString<ScheduleDirect.ProgramDetailsResilt>(responseString);
                            Dictionary<string, ScheduleDirect.ProgramDetails> programDict = programDetails.result.ToDictionary(p => p.programID, y => y);



                            foreach (ScheduleDirect.Day day in root.days)
                            {
                                foreach (ScheduleDirect.Program schedule in day.programs)
                                {
                                    _logger.Info("Proccesing Schedule for statio ID " + stationID + " which corresponds to channel" + channelNumber + " and program id " + schedule.programID);

                                    programsInfo.Add(GetProgram(channelNumber, schedule, programDict[schedule.programID]));
                                }
                            }
                            _logger.Info("Finished with TVData");
                            return programsInfo;
                        }
                    }
                }
            }

            return (IEnumerable<ProgramInfo>)new List<ProgramInfo>();
        }

        private ProgramInfo GetProgram(string channel, ScheduleDirect.Program programInfo, ScheduleDirect.ProgramDetails details)
        {
            DateTime startAt = DateTime.ParseExact(programInfo.airDateTime, "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);
            DateTime endAt = startAt.AddSeconds(programInfo.duration);
            ProgramAudio audioType = ProgramAudio.Mono;
            bool hdtv = false;
            bool repeat = (programInfo.@new == null);
            string newID = programInfo.programID + "T" + startAt.Ticks + "C" + channel;


            if (programInfo.audioProperties != null) { if (programInfo.audioProperties.Exists(item => item == "stereo")) { audioType = ProgramAudio.Stereo; } else { audioType = ProgramAudio.Mono; } }

            if ((programInfo.videoProperties != null)) { hdtv = programInfo.videoProperties.Exists(item => item == "hdtv"); }

            string desc = "";
            if (details.descriptions != null)
            {
                if (details.descriptions.description1000 != null) { desc = details.descriptions.description1000[0].description; }
                else if (details.descriptions.description100 != null) { desc = details.descriptions.description100[0].description; }
            }
            ScheduleDirect.Gracenote gracenote;
            string EpisodeTitle = "";
            if (details.metadata != null)
            {
                gracenote = details.metadata.Find(x => x.Gracenote != null).Gracenote;
                if (details.eventDetails.subType == "Series") { EpisodeTitle = "Season: " + gracenote.season + " Episode: " + gracenote.episode; }
                if (details.episodeTitle150 != null) { EpisodeTitle = EpisodeTitle + " " + details.episodeTitle150; }
            }
            if (details.episodeTitle150 != null) { EpisodeTitle = EpisodeTitle + " " + details.episodeTitle150; }
            bool hasImage = false;
            var imageLink = "";
            /*
            if (!details.hasImageArtwork != null) {
                hasImage = true;
                imageLink = details.images;

            }
             */
            var info = new ProgramInfo
            {
                ChannelId = channel,
                Id = newID,
                Overview = desc,
                StartDate = startAt,
                EndDate = endAt,
                Genres = new List<string>() { "N/A" },
                Name = details.titles[0].title120 ?? "Unkown",
                OfficialRating = "0",
                CommunityRating = null,
                EpisodeTitle = EpisodeTitle,
                Audio = audioType,
                IsHD = hdtv,
                IsRepeat = repeat,
                IsSeries = (details.eventDetails.subType == "Series"),
                ImageUrl = imageLink,
                HasImage = hasImage,
                IsNews = false,
                IsKids = false,
                IsSports = false,
                IsLive = false,
                IsMovie = false,
                IsPremiere = false,

            };
            //logger.Info("Done init");
            if (null != details.originalAirDate)
            {
                info.OriginalAirDate = DateTime.Parse(details.originalAirDate);
            }

            if (details.genres != null)
            {
                info.Genres = details.genres.Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
                info.IsNews = details.genres.Contains("news", StringComparer.OrdinalIgnoreCase);
                info.IsMovie = details.genres.Contains("Feature Film", StringComparer.OrdinalIgnoreCase) || (details.movie != null);
                info.IsKids = false;
                info.IsSports = details.genres.Contains("sports", StringComparer.OrdinalIgnoreCase) ||
                    details.genres.Contains("Sports non-event", StringComparer.OrdinalIgnoreCase) ||
                    details.genres.Contains("Sports event", StringComparer.OrdinalIgnoreCase) ||
                    details.genres.Contains("Sports talk", StringComparer.OrdinalIgnoreCase) ||
                    details.genres.Contains("Sports news", StringComparer.OrdinalIgnoreCase);
            }
            return info;
        }
        public bool checkExist(object obj)
        {
            if (obj != null)
            {
                return true;
            }
            return false;
        }
        public async Task<string> getLineups()
        {
            if (username.Length > 0 && password.Length > 0)
            {

                apiUrl = "https://json.schedulesdirect.org/20141201";
                await refreshToken();
                _logger.Info("Lineups on account ");
                var httpOptions = new HttpRequestOptionsMod()
                {
                    Url = apiUrl + "/lineups",
                    UserAgent = "Emby-Server",
                    Token = token
                };
                string Lineups = "";
                var check = false;
                using (Stream responce = await _httpClient.Get(httpOptions).ConfigureAwait(false))
                {
                    var root = _jsonSerializer.DeserializeFromStream<ScheduleDirect.Lineups>(responce);
                    _logger.Info("Lineups on account ");
                    if (root.lineups != null)
                    {
                        foreach (ScheduleDirect.Lineup lineup in root.lineups)
                        {
                            _logger.Info("Lineups ID: " + lineup.lineup);
                            if (lineup.lineup == _lineup) { check = true; }
                            if (String.IsNullOrWhiteSpace(Lineups))
                            {
                                Lineups = lineup.lineup;
                            }
                            else { Lineups = Lineups + "," + lineup.lineup; }
                        }
                        if (!String.IsNullOrWhiteSpace(_lineup) && !check) { await addHeadEnd(); }
                    }
                    else
                    {
                        _logger.Info("No lineups on account");
                    }
                    return Lineups;
                }
            } return "";
        }
        public async Task<Dictionary<string, string>> getHeadends(string zipcode)
        {
            Dictionary<string, string> lineups = new Dictionary<string, string>();
            if (username.Length > 0 && password.Length > 0)
            {
                apiUrl = "https://json.schedulesdirect.org/20141201";
                await refreshToken();
                _logger.Info("Headends on account ");
                var httpOptions = new HttpRequestOptionsMod()
                {
                    Url = apiUrl + "/headends?country=USA&postalcode=" + zipcode,
                    UserAgent = "Emby-Server",
                    Token = token
                };

                using (Stream responce = await _httpClient.Get(httpOptions).ConfigureAwait(false))
                {
                    var root = _jsonSerializer.DeserializeFromStream<List<ScheduleDirect.Headends>>(responce);
                    _logger.Info("Lineups on account ");
                    if (root != null)
                    {
                        foreach (ScheduleDirect.Headends headend in root)
                        {
                            _logger.Info("Headend: " + headend.headend);
                            foreach (ScheduleDirect.Lineup lineup in headend.lineups)
                                if (!String.IsNullOrWhiteSpace(lineup.name))
                                {
                                    _logger.Info("Headend: " + lineup.uri.Substring(18));
                                    lineups.Add(lineup.name, lineup.uri.Substring(18));
                                }
                        }
                    }
                    else
                    {
                        _logger.Info("No lineups on account");
                    }
                }
            }
            return lineups;
        }

        public async Task addHeadEnd()
        {
            if (username.Length > 0 && password.Length > 0 && String.IsNullOrWhiteSpace(_lineup))
            {
                apiUrl = "https://json.schedulesdirect.org/20141201";
                await refreshToken();
                _logger.Info("Adding new LineUp ");
                var httpOptions = new HttpRequestOptionsMod()
                {
                    Url = apiUrl + "/lineups/" + _lineup,
                    UserAgent = "Emby-Server",
                    Token = token
                };

                using (var response = await _httpClient.SendAsync(httpOptions, "PUT"))
                {
                    
                }
            }
        }
    }
}