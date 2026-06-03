using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace ChzzkSkul
{
    public class YouTubeUnity : MonoBehaviour
    {
        public UnityEvent<string, string> onMessage = new UnityEvent<string, string>();
        public UnityEvent<string, string, int> onDonation = new UnityEvent<string, string, int>();

        private string _apiKey;
        private string _videoId;
        private string _liveChatId;
        private string _nextPageToken = "";
        private float _pollingIntervalMillis = 3000f; // Default 3s
        private bool _isRunning = false;

        public void Connect(string apiKey, string videoId)
        {
            _apiKey = apiKey;
            _videoId = videoId;
            _isRunning = true;
            
            StartCoroutine(FetchLiveChatIdRoutine());
        }

        private IEnumerator FetchLiveChatIdRoutine()
        {
            string url = $"https://www.googleapis.com/youtube/v3/videos?id={_videoId}&part=liveStreamingDetails&key={_apiKey}";
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.isNetworkError || request.isHttpError)
                {
                    Debug.LogError($"[YouTubeUnity] Failed to get liveChatId: {request.error}\n{request.downloadHandler.text}");
                    _isRunning = false;
                    yield break;
                }

                try
                {
                    JObject json = JObject.Parse(request.downloadHandler.text);
                    JArray items = (JArray)json["items"];
                    if (items != null && items.Count > 0)
                    {
                        var streamingDetails = items[0]["liveStreamingDetails"];
                        if (streamingDetails != null)
                        {
                            _liveChatId = (string)streamingDetails["activeLiveChatId"];
                        }
                    }

                    if (string.IsNullOrEmpty(_liveChatId))
                    {
                        Debug.LogError($"[YouTubeUnity] Could not find activeLiveChatId for Video ID: {_videoId}. Is it a live stream?");
                        _isRunning = false;
                        yield break;
                    }

                    Debug.Log($"[YouTubeUnity] Successfully connected to YouTube Live Chat! (Chat ID: {_liveChatId})");
                    StartCoroutine(PollMessagesRoutine());
                }
                catch (Exception e)
                {
                    Debug.LogError($"[YouTubeUnity] Error parsing liveChatId: {e.Message}");
                    _isRunning = false;
                }
            }
        }

        private IEnumerator PollMessagesRoutine()
        {
            while (_isRunning)
            {
                string url = $"https://www.googleapis.com/youtube/v3/liveChat/messages?liveChatId={_liveChatId}&part=snippet,authorDetails&key={_apiKey}";
                if (!string.IsNullOrEmpty(_nextPageToken))
                {
                    url += $"&pageToken={_nextPageToken}";
                }

                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    yield return request.SendWebRequest();

                    if (request.isNetworkError || request.isHttpError)
                    {
                        Debug.LogError($"[YouTubeUnity] Fetch error: {request.error}\n{request.downloadHandler.text}");
                    }
                    else
                    {
                        ParseMessages(request.downloadHandler.text);
                    }
                }

                yield return new WaitForSeconds(_pollingIntervalMillis / 1000f);
            }
        }

        private void ParseMessages(string jsonResponse)
        {
            try
            {
                JObject json = JObject.Parse(jsonResponse);
                
                // Update polling interval
                if (json["pollingIntervalMillis"] != null)
                {
                    _pollingIntervalMillis = (float)json["pollingIntervalMillis"];
                }

                bool isFirstPoll = string.IsNullOrEmpty(_nextPageToken);
                
                if (json["nextPageToken"] != null)
                {
                    _nextPageToken = (string)json["nextPageToken"];
                }

                JArray items = (JArray)json["items"];
                if (items != null && !isFirstPoll) // Ignore messages from the first poll to avoid processing old backlog
                {
                    foreach (var item in items)
                    {
                        var snippet = item["snippet"];
                        var author = item["authorDetails"];

                        if (snippet != null && author != null)
                        {
                            string messageType = (string)snippet["type"];
                            if (messageType == "textMessageEvent")
                            {
                                string displayName = (string)author["displayName"];
                                string displayMessage = (string)snippet["displayMessage"];
                                
                                onMessage?.Invoke(displayName, displayMessage);
                            }
                            else if (messageType == "superChatEvent")
                            {
                                string displayName = (string)author["displayName"];
                                string displayMessage = (string)snippet["displayMessage"];
                                
                                var superChatDetails = snippet["superChatDetails"];
                                if (superChatDetails != null)
                                {
                                    long amountMicros = (long)superChatDetails["amountMicros"];
                                    int amountWon = (int)(amountMicros / 1000000); // 1,000,000 micros = 1 base currency unit. Assuming KRW context.
                                    onDonation?.Invoke(displayName, displayMessage, amountWon);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[YouTubeUnity] Parse error: {e.Message}");
            }
        }

        private void OnDestroy()
        {
            _isRunning = false;
        }
    }
}
