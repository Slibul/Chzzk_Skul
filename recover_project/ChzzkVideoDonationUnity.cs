using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Authentication;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using WebSocketSharp;

public class ChzzkVideoDonationUnity : MonoBehaviour
{
	private enum SslProtocolsHack
	{
		Tls = 192,
		Tls11 = 768,
		Tls12 = 3072
	}

	[Serializable]
	public class SessionUrl
	{
		[Serializable]
		public class Content
		{
			public string sessionUrl;
		}

		public string code;

		public object message;

		public Content content;
	}

	public class DonationControl
	{
		private int startSecond;

		private int endSecond;

		private bool stopVideo;

		private bool titleExpose;

		private string donationId;

		private int payAmount;

		private bool isAnonymous;

		private bool useSpeech;
	}

	[Serializable]
	public class VideoDonationList
	{
		public List<string> videoDonation;
	}

	[Serializable]
	public class VideoDonation
	{
		public int startSecond;

		public int endSecond;

		public string videoType;

		public string videoId;

		public string playMode;

		public bool stopVideo;

		public bool titleExpose;

		public string donationId;

		public string profile;

		public int payAmount;

		public string donationText;

		public bool isAnonymous;

		public int tierNo;

		public bool useSpeech;

		public override string ToString()
		{
			return JsonConvert.SerializeObject(this);
		}
	}

	[Serializable]
	public class Profile
	{
		[Serializable]
		public class ActivityBadge
		{
			public int badgeNo;

			public string badgeId;

			public string imageUrl;

			public bool activated;
		}

		[Serializable]
		public class StreamingProperty
		{
			[Serializable]
			public class Subscription
			{
				public class Badge
				{
					public string imageUrl;
				}

				public int accumulativeMonth;

				public int tier;

				public Badge badge;
			}

			[Serializable]
			public class NicknameColor
			{
				public string colorCode;
			}

			public Subscription subscription;

			public NicknameColor nicknameColor;
		}

		public string userIdHash;

		public string nickname;

		public string profileImageUrl;

		public string userRoleCode;

		public string badge;

		public string title;

		public bool verifiedMark;

		public List<ActivityBadge> activityBadges;

		public StreamingProperty streamingProperty;
	}

	private WebSocket socket = null;

	private float timer = 0f;

	private bool running = false;

	private const string HEARTBEAT_REQUEST = "2";

	private const string HEARTBEAT_RESPONSE = "3";

	public UnityEvent<Profile, VideoDonation> onVideoDonationArrive = new UnityEvent<Profile, VideoDonation>();

	public UnityEvent<DonationControl> onVideoDonationControl = new UnityEvent<DonationControl>();

	public UnityEvent onClose = new UnityEvent();

	public UnityEvent onOpen = new UnityEvent();

	private int closedCount = 0;

	private bool reOpenTrying = false;

	private string wssUrl;

	private Dictionary<string, KeyValuePair<Profile, VideoDonation>> activeVideo;

	private void Start()
	{
	}

	private void Update()
	{
		if (closedCount > 0)
		{
			UnityEvent obj = onClose;
			if (obj != null)
			{
				obj.Invoke();
			}
			if (!reOpenTrying)
			{
				((MonoBehaviour)this).StartCoroutine(TryReOpen());
			}
			closedCount--;
		}
		if (running)
		{
			timer += Time.unscaledDeltaTime;
			if (timer > 15f)
			{
				socket.Send("2");
				timer = 0f;
			}
		}
	}

	private void OnDestroy()
	{
		StopListening();
	}

	public string GetMissionWSSId(string url)
	{
		return url.Split('@')[1];
	}

	public async UniTask<string> GetSessionURL(string missionWSSId)
	{
		string url = "https://api.chzzk.naver.com/manage/v1/alerts/video@" + missionWSSId + "/session-url";
		UnityWebRequest request = UnityWebRequest.Get(url);
		request.SendWebRequest();
		SessionUrl sessionUrl = null;
		if ((int)request.result == 1)
		{
			sessionUrl = JsonUtility.FromJson<SessionUrl>(request.downloadHandler.text);
		}
		return sessionUrl.content.sessionUrl;
	}

	public string MakeWssURL(string sessionUrl)
	{
		string text = sessionUrl.Split(new string[1] { "auth=" }, StringSplitOptions.None)[1];
		string text2 = sessionUrl.Split(new string[1] { ".nchat" }, StringSplitOptions.None)[0].Substring(12);
		return "wss://ssio" + text2 + ".nchat.naver.com/socket.io/?auth=" + text + "&EIO=3&transport=websocket";
	}

	public async UniTask<string> GetWssUrlFromMissionUrl(string missionUrl)
	{
		string wssId = GetMissionWSSId(missionUrl);
		return MakeWssURL(await GetSessionURL(wssId));
	}

	public async void Connect(string url)
	{
		wssUrl = await GetWssUrlFromMissionUrl(url);
		Connect().Forget();
	}

	public async UniTask Connect()
	{
		if (socket != null && socket.IsAlive)
		{
			socket.Close();
			socket = null;
		}
		socket = new WebSocket(wssUrl);
		SslProtocols sslProtocolHack = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
		socket.SslConfiguration.EnabledSslProtocols = sslProtocolHack;
		socket.OnMessage += ParseMessage;
		socket.OnClose += CloseConnect;
		socket.OnOpen += onSocketOpen;
		socket.Connect();
		await UniTask.CompletedTask;
	}

	private void onSocketOpen(object sender, EventArgs e)
	{
		timer = 0f;
		running = true;
		socket.Send("2");
	}

	public void StopListening()
	{
		if (socket != null)
		{
			socket.Close();
			socket = null;
		}
	}

	private void ParseMessage(object sender, MessageEventArgs e)
	{
		Debug.Log((object)e.Data);
		if (e.Data == "2")
		{
			timer = 0f;
			socket.Send("3");
		}
		else
		{
			if (e.Data == "3" || e.Data == "40")
			{
				return;
			}
			VideoDonationList videoDonationList = JsonUtility.FromJson<VideoDonationList>(e.Data);
			string text = videoDonationList.videoDonation[0];
			string text2 = text;
			if (!(text2 == "donation"))
			{
				if (text2 == "donationControl")
				{
					List<DonationControl> list = new List<DonationControl>();
					for (int i = 1; i < videoDonationList.videoDonation.Count; i++)
					{
						DonationControl donationControl = JsonUtility.FromJson<DonationControl>(videoDonationList.videoDonation[i]);
						Debug.Log((object)donationControl);
						list.Add(donationControl);
						onVideoDonationControl.Invoke(donationControl);
					}
				}
			}
			else
			{
				for (int j = 1; j < videoDonationList.videoDonation.Count; j++)
				{
					VideoDonation videoDonation = JsonUtility.FromJson<VideoDonation>(videoDonationList.videoDonation[j]);
					Debug.Log((object)videoDonation);
					Profile profile = JsonUtility.FromJson<Profile>(videoDonation.profile);
					onVideoDonationArrive.Invoke(profile, videoDonation);
				}
			}
		}
	}

	private void CloseConnect(object sender, CloseEventArgs e)
	{
		Debug.LogError((object)"연결이 해제되었습니다");
		Debug.Log((object)e.Reason);
		Debug.Log((object)e.Code);
		Debug.Log((object)e);
		closedCount++;
	}

	private IEnumerator TryReOpen()
	{
		reOpenTrying = true;
		yield return (object)new WaitForSeconds(1f);
		if (!socket.IsAlive)
		{
			socket.Connect();
		}
		reOpenTrying = false;
	}
}
