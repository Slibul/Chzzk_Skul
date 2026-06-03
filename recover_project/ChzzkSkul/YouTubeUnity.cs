using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace ChzzkSkul;

public class YouTubeUnity : MonoBehaviour
{
	public UnityEvent<string, string> onMessage = new UnityEvent<string, string>();

	public UnityEvent<string, string, int> onDonation = new UnityEvent<string, string, int>();

	private string _apiKey;

	private string _videoId;

	private string _liveChatId;

	private string _nextPageToken = "";

	private float _pollingIntervalMillis = 3000f;

	private bool _isRunning = false;

	public void Connect(string apiKey, string videoId)
	{
		_apiKey = apiKey;
		_videoId = videoId;
		_isRunning = true;
		((MonoBehaviour)this).StartCoroutine(FetchLiveChatIdRoutine());
	}

	private IEnumerator FetchLiveChatIdRoutine()
	{
		string url = "https://www.googleapis.com/youtube/v3/videos?id=" + _videoId + "&part=liveStreamingDetails&key=" + _apiKey;
		UnityWebRequest request = UnityWebRequest.Get(url);
		try
		{
			yield return request.SendWebRequest();
			if (request.isNetworkError || request.isHttpError)
			{
				Debug.LogError((object)("[YouTubeUnity] Failed to get liveChatId: " + request.error + "\n" + request.downloadHandler.text));
				_isRunning = false;
				yield break;
			}
			try
			{
				JObject json = JObject.Parse(request.downloadHandler.text);
				JArray items = (JArray)json["items"];
				if (items != null && items.Count > 0)
				{
					JToken streamingDetails = items[0]["liveStreamingDetails"];
					if (streamingDetails != null)
					{
						_liveChatId = (string?)streamingDetails["activeLiveChatId"];
					}
				}
				if (string.IsNullOrEmpty(_liveChatId))
				{
					Debug.LogError((object)("[YouTubeUnity] Could not find activeLiveChatId for Video ID: " + _videoId + ". Is it a live stream?"));
					_isRunning = false;
					yield break;
				}
				Debug.Log((object)("[YouTubeUnity] Successfully connected to YouTube Live Chat! (Chat ID: " + _liveChatId + ")"));
				((MonoBehaviour)this).StartCoroutine(PollMessagesRoutine());
			}
			catch (Exception ex)
			{
				Debug.LogError((object)("[YouTubeUnity] Error parsing liveChatId: " + ex.Message));
				_isRunning = false;
			}
		}
		finally
		{
			((IDisposable)request)?.Dispose();
		}
	}

	private IEnumerator PollMessagesRoutine()
	{
		while (_isRunning)
		{
			string url = "https://www.googleapis.com/youtube/v3/liveChat/messages?liveChatId=" + _liveChatId + "&part=snippet,authorDetails&key=" + _apiKey;
			if (!string.IsNullOrEmpty(_nextPageToken))
			{
				url = url + "&pageToken=" + _nextPageToken;
			}
			UnityWebRequest request = UnityWebRequest.Get(url);
			try
			{
				yield return request.SendWebRequest();
				if (request.isNetworkError || request.isHttpError)
				{
					Debug.LogError((object)("[YouTubeUnity] Fetch error: " + request.error + "\n" + request.downloadHandler.text));
				}
				else
				{
					ParseMessages(request.downloadHandler.text);
				}
			}
			finally
			{
				((IDisposable)request)?.Dispose();
			}
			yield return (object)new WaitForSeconds(_pollingIntervalMillis / 1000f);
		}
	}

	private void ParseMessages(string jsonResponse)
	{
		try
		{
			JObject jObject = JObject.Parse(jsonResponse);
			if (jObject["pollingIntervalMillis"] != null)
			{
				_pollingIntervalMillis = (float)jObject["pollingIntervalMillis"];
			}
			bool flag = string.IsNullOrEmpty(_nextPageToken);
			if (jObject["nextPageToken"] != null)
			{
				_nextPageToken = (string?)jObject["nextPageToken"];
			}
			JArray jArray = (JArray)jObject["items"];
			if (jArray == null || flag)
			{
				return;
			}
			foreach (JToken item in jArray)
			{
				JToken jToken = item["snippet"];
				JToken jToken2 = item["authorDetails"];
				if (jToken == null || jToken2 == null)
				{
					continue;
				}
				string text = (string?)jToken["type"];
				if (text == "textMessageEvent")
				{
					string text2 = (string?)jToken2["displayName"];
					string text3 = (string?)jToken["displayMessage"];
					onMessage?.Invoke(text2, text3);
				}
				else if (text == "superChatEvent")
				{
					string text4 = (string?)jToken2["displayName"];
					string text5 = (string?)jToken["displayMessage"];
					JToken jToken3 = jToken["superChatDetails"];
					if (jToken3 != null)
					{
						long num = (long)jToken3["amountMicros"];
						int num2 = (int)(num / 1000000);
						onDonation?.Invoke(text4, text5, num2);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[YouTubeUnity] Parse error: " + ex.Message));
		}
	}

	private void OnDestroy()
	{
		_isRunning = false;
	}
}
