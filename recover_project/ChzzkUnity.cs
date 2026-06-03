using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using WebSocketSharp;

public class ChzzkUnity : MonoBehaviour
{
	private enum SslProtocolsHack
	{
		Tls = 192,
		Tls11 = 768,
		Tls12 = 3072
	}

	[Serializable]
	public class LiveStatus
	{
		[Serializable]
		public class Content
		{
			public string liveTitle;

			public string status;

			public int concurrentUserCount;

			public int accumulateCount;

			public bool paidPromotion;

			public bool adult;

			public string chatChannelId;

			public string categoryType;

			public string liveCategory;

			public string liveCategoryValue;

			public string livePollingStatusJson;

			public string faultStatus;

			public string userAdultStatus;

			public bool chatActive;

			public string chatAvailableGroup;

			public string chatAvailableCondition;

			public int minFollowerMinute;
		}

		public int code;

		public string message;

		public Content content;
	}

	[Serializable]
	public class AccessTokenResult
	{
		[Serializable]
		public class Content
		{
			[Serializable]
			public class TemporaryRestrict
			{
				public bool temporaryRestrict;

				public int times;

				public int duration;

				public int createdTime;
			}

			public string accessToken;

			public bool realNameAuth;

			public string extraToken;
		}

		public int code;

		public string message;

		public Content content;
	}

	[Serializable]
	public class Profile
	{
		[Serializable]
		public class StreamingProperty
		{
		}

		public string userIdHash;

		public string nickname;

		public string profileImageUrl;

		public string userRoleCode;

		public string badge;

		public string title;

		public string verifiedMark;

		public List<string> activityBadges;

		public StreamingProperty streamingProperty;
	}

	[Serializable]
	public class SubscriptionExtras
	{
		public int month;

		public string tierName;

		public string nickname;

		public int tierNo;
	}

	[Serializable]
	public class DonationExtras
	{
		[Serializable]
		public class WeeklyRank
		{
			public string userIdHash;

			public string nickName;

			public bool verifiedMark;

			public int donationAmount;

			public int ranking;
		}

		private object emojis;

		public bool isAnonymous;

		public string payType;

		public int payAmount;

		public string streamingChannelId;

		public string nickname;

		public string osType;

		public string donationType;

		public List<WeeklyRank> weeklyRankList;

		public WeeklyRank donationUserWeeklyRank;
	}

	[Serializable]
	public class ChannelInfo
	{
		[Serializable]
		public class Content
		{
			public string channelId;

			public string channelName;

			public string channelImageUrl;

			public bool verifiedMark;

			public string channelType;

			public string channelDescription;

			public int followerCount;

			public bool openLive;
		}

		public int code;

		public string message;

		public Content content;
	}

	private const string WS_URL = "wss://kr-ss3.chat.naver.com/chat";

	private const string HEARTBEAT_REQUEST = "{\"ver\":\"2\",\"cmd\":0}";

	private const string HEARTBEAT_RESPONSE = "{\"ver\":\"2\",\"cmd\":10000}";

	private string cid;

	private string token;

	public string channel;

	private WebSocket socket = null;

	private float timer = 0f;

	private bool running = false;

	public UnityEvent<Profile, string> onMessage = new UnityEvent<Profile, string>();

	public UnityEvent<Profile, string, DonationExtras> onDonation = new UnityEvent<Profile, string, DonationExtras>();

	public UnityEvent<Profile, SubscriptionExtras> onSubscription = new UnityEvent<Profile, SubscriptionExtras>();

	public UnityEvent onClose = new UnityEvent();

	public UnityEvent onOpen = new UnityEvent();

	private int closedCount = 0;

	private bool reOpenTrying = false;

	private void Start()
	{
		onMessage.AddListener((UnityAction<Profile, string>)DebugMessage);
		onDonation.AddListener((UnityAction<Profile, string, DonationExtras>)DebugDonation);
		onSubscription.AddListener((UnityAction<Profile, SubscriptionExtras>)DebugSubscription);
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
	}

	public IEnumerator TryReOpen()
	{
		reOpenTrying = true;
		yield return (object)new WaitForSeconds(3f);
		Connect().Forget();
		reOpenTrying = false;
	}

	private void FixedUpdate()
	{
		if (running)
		{
			timer += Time.unscaledDeltaTime;
			if (timer > 15f)
			{
				socket.Send("{\"ver\":\"2\",\"cmd\":0}");
				timer = 0f;
			}
		}
	}

	private void OnDestroy()
	{
		StopListening();
	}

	private void DebugMessage(Profile profile, string str)
	{
		Debug.Log((object)("| [Message] " + profile.nickname + " - " + str));
	}

	private void DebugDonation(Profile profile, string str, DonationExtras donation)
	{
		Debug.Log((object)(donation.isAnonymous ? $"| [Donation] 익명 - {str} - {donation.payAmount}/{donation.payType}" : $"| [Donation] {profile.nickname} - {str} - {donation.payAmount}/{donation.payType}"));
	}

	private void DebugSubscription(Profile profile, SubscriptionExtras subscription)
	{
		Debug.Log((object)$"| [Subscription] {profile.nickname} - {subscription.month}");
	}

	public void RemoveAllOnMessageListener()
	{
		((UnityEventBase)onMessage).RemoveAllListeners();
	}

	public void RemoveAllOnDonationListener()
	{
		((UnityEventBase)onDonation).RemoveAllListeners();
	}

	public void RemoveAllOnSubscriptionListener()
	{
		((UnityEventBase)onSubscription).RemoveAllListeners();
	}

	public async UniTask<ChannelInfo> GetChannelInfo(string channelId)
	{
		string url = "https://api.chzzk.naver.com/service/v1/channels/" + channelId;
		UnityWebRequest request = UnityWebRequest.Get(url);
		try
		{
			request.certificateHandler = (CertificateHandler)(object)new BypassCertificate();
			request.SendWebRequest();
			ChannelInfo channelInfo = null;
			while (!request.isDone)
			{
				await UniTask.Yield();
			}
			if ((int)request.result == 1)
			{
				channelInfo = JsonConvert.DeserializeObject<ChannelInfo>(request.downloadHandler.text);
			}
			return channelInfo;
		}
		finally
		{
			((IDisposable)request)?.Dispose();
		}
	}

	public async UniTask<LiveStatus> GetLiveStatus(string channelId)
	{
		string url = "https://api.chzzk.naver.com/polling/v2/channels/" + channelId + "/live-status";
		UnityWebRequest request = UnityWebRequest.Get(url);
		try
		{
			request.certificateHandler = (CertificateHandler)(object)new BypassCertificate();
			request.SendWebRequest();
			LiveStatus liveStatus = null;
			while (!request.isDone)
			{
				await UniTask.Yield();
			}
			if ((int)request.result == 1)
			{
				try
				{
					liveStatus = JsonConvert.DeserializeObject<LiveStatus>(request.downloadHandler.text);
					Debug.Log((object)("[ChzzkUnity] GetLiveStatus 성공: " + request.downloadHandler.text));
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					Debug.LogError((object)("[ChzzkUnity] GetLiveStatus JSON 파싱 실패: " + ex2.Message));
				}
			}
			else
			{
				Debug.LogError((object)("[ChzzkUnity] GetLiveStatus HTTP 요청 실패: " + request.error + " (URL: " + url + ")"));
			}
			return liveStatus;
		}
		finally
		{
			((IDisposable)request)?.Dispose();
		}
	}

	public async UniTask<AccessTokenResult> GetAccessToken(string cid)
	{
		string url = "https://comm-api.game.naver.com/nng_main/v1/chats/access-token?channelId=" + cid + "&chatType=STREAMING";
		UnityWebRequest request = UnityWebRequest.Get(url);
		try
		{
			request.certificateHandler = (CertificateHandler)(object)new BypassCertificate();
			request.SendWebRequest();
			AccessTokenResult accessTokenResult = null;
			while (!request.isDone)
			{
				await UniTask.Yield();
			}
			if ((int)request.result == 1)
			{
				try
				{
					accessTokenResult = JsonConvert.DeserializeObject<AccessTokenResult>(request.downloadHandler.text);
					Debug.Log((object)("[ChzzkUnity] GetAccessToken 성공: " + request.downloadHandler.text));
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					Debug.LogError((object)("[ChzzkUnity] GetAccessToken JSON 파싱 실패: " + ex2.Message));
				}
			}
			else
			{
				Debug.LogError((object)("[ChzzkUnity] GetAccessToken HTTP 요청 실패: " + request.error + " (URL: " + url + ")"));
			}
			return accessTokenResult;
		}
		finally
		{
			((IDisposable)request)?.Dispose();
		}
	}

	public async UniTask Connect()
	{
		try
		{
			if (socket != null && socket.IsAlive)
			{
				socket.Close();
				socket = null;
			}
			Debug.Log((object)("[ChzzkUnity] Connecting to channel: " + channel));
			LiveStatus liveStatus = await GetLiveStatus(channel);
			if (liveStatus == null)
			{
				Debug.LogError((object)"[ChzzkUnity] 라이브 상태 데이터를 가져오지 못했습니다. 연결을 중단합니다.");
				return;
			}
			if (liveStatus.content == null)
			{
				Debug.LogError((object)$"[ChzzkUnity] 라이브 상태 content가 null입니다. (코드: {liveStatus.code}, 메시지: {liveStatus.message}). 연결을 중단합니다.");
				return;
			}
			cid = liveStatus.content.chatChannelId;
			if (string.IsNullOrEmpty(cid))
			{
				Debug.LogError((object)"[ChzzkUnity] chatChannelId가 비어있습니다. 방송이 켜져있지 않거나 채널 ID가 틀렸습니다. 연결을 중단합니다.");
				return;
			}
			AccessTokenResult accessTokenResult = await GetAccessToken(cid);
			if (accessTokenResult == null)
			{
				Debug.LogError((object)"[ChzzkUnity] 액세스 토큰 데이터를 가져오지 못했습니다. 연결을 중단합니다.");
				return;
			}
			if (accessTokenResult.content == null)
			{
				Debug.LogError((object)$"[ChzzkUnity] 액세스 토큰 content가 null입니다. (코드: {accessTokenResult.code}, 메시지: {accessTokenResult.message}). 연결을 중단합니다.");
				return;
			}
			token = accessTokenResult.content.accessToken;
			if (string.IsNullOrEmpty(token))
			{
				Debug.LogError((object)"[ChzzkUnity] 액세스 토큰 값이 비어있습니다. 연결을 중단합니다.");
				return;
			}
			socket = new WebSocket("wss://kr-ss3.chat.naver.com/chat");
			socket.Log.Output = delegate
			{
			};
			SslProtocols sslProtocolHack = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
			socket.SslConfiguration.EnabledSslProtocols = sslProtocolHack;
			socket.SslConfiguration.ServerCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true;
			socket.OnMessage += ParseMessage;
			socket.OnClose += CloseConnect;
			socket.OnOpen += StartChat;
			socket.OnError += delegate(object sender, ErrorEventArgs e)
			{
				Debug.LogError((object)("[ChzzkUnity] WebSocket 오류: " + e.Message));
			};
			socket.ConnectAsync();
			Debug.Log((object)("[ChzzkUnity] WebSocket 연결 요청 완료 (채널 ID: " + cid + ")"));
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Debug.LogError((object)$"[ChzzkUnity] Connect 중 치명적 예외 발생: {ex2}");
		}
		await UniTask.CompletedTask;
	}

	public void Connect(string channelId)
	{
		channel = channelId;
		Connect().Forget();
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
		try
		{
			IDictionary<string, object> dictionary = JsonConvert.DeserializeObject<IDictionary<string, object>>(e.Data);
			Debug.Log((object)e.Data);
			switch ((long)dictionary["cmd"])
			{
			case 0L:
				socket.Send("{\"ver\":\"2\",\"cmd\":10000}");
				timer = 0f;
				break;
			case 93101L:
			{
				JArray jArray = (JArray)dictionary["bdy"];
				{
					foreach (JToken item in jArray)
					{
						try
						{
							JObject jObject = (JObject)item;
							string text = jObject["profile"]?.ToString();
							Profile profile = new Profile();
							profile.nickname = "익명";
							if (!string.IsNullOrEmpty(text) && text != "null")
							{
								try
								{
									JObject jObject3 = JObject.Parse(text);
									if (jObject3["nickname"] != null)
									{
										profile.nickname = jObject3["nickname"].ToString();
									}
									if (jObject3["userIdHash"] != null)
									{
										profile.userIdHash = jObject3["userIdHash"].ToString();
									}
								}
								catch (Exception ex5)
								{
									Debug.LogWarning((object)("[ChzzkUnity] 프로필 JObject 파싱 오류: " + ex5.Message + ". 원본: " + text));
								}
							}
							string text3 = jObject["msg"]?.ToString()?.Trim() ?? "";
							onMessage?.Invoke(profile, text3);
						}
						catch (Exception ex6)
						{
							Debug.LogError((object)("[ChzzkUnity] 개별 채팅 메시지 처리 중 오류: " + ex6.Message));
						}
					}
					break;
				}
			}
			case 93102L:
			{
				JArray jArray = (JArray)dictionary["bdy"];
				{
					foreach (JToken item2 in jArray)
					{
						try
						{
							JObject jObject = (JObject)item2;
							string text = jObject["profile"]?.ToString();
							Profile profile = new Profile();
							profile.nickname = "익명";
							if (!string.IsNullOrEmpty(text) && text != "null")
							{
								try
								{
									JObject jObject2 = JObject.Parse(text);
									if (jObject2["nickname"] != null)
									{
										profile.nickname = jObject2["nickname"].ToString();
									}
									if (jObject2["userIdHash"] != null)
									{
										profile.userIdHash = jObject2["userIdHash"].ToString();
									}
								}
								catch (Exception ex)
								{
									Debug.LogWarning((object)("[ChzzkUnity] 후원자 프로필 JObject 파싱 오류: " + ex.Message + ". 원본: " + text));
								}
							}
							int num = int.Parse(jObject["msgTypeCode"].ToString());
							string text2 = null;
							JToken value2;
							if (jObject.TryGetValue("extra", out JToken value))
							{
								text2 = value.ToString();
							}
							else if (jObject.TryGetValue("extras", out value2))
							{
								text2 = value2.ToString();
							}
							switch (num)
							{
							case 10:
							{
								DonationExtras donationExtras = null;
								if (!string.IsNullOrEmpty(text2))
								{
									try
									{
										donationExtras = JsonConvert.DeserializeObject<DonationExtras>(text2);
									}
									catch (Exception ex3)
									{
										Debug.LogError((object)("[ChzzkUnity] DonationExtras 파싱 오류: " + ex3.Message + ". 원본: " + text2));
									}
								}
								onDonation?.Invoke(profile, jObject["msg"]?.ToString() ?? "", donationExtras);
								break;
							}
							case 11:
							{
								SubscriptionExtras subscriptionExtras = null;
								if (!string.IsNullOrEmpty(text2))
								{
									try
									{
										subscriptionExtras = JsonConvert.DeserializeObject<SubscriptionExtras>(text2);
									}
									catch (Exception ex2)
									{
										Debug.LogError((object)("[ChzzkUnity] SubscriptionExtras 파싱 오류: " + ex2.Message + ". 원본: " + text2));
									}
								}
								onSubscription?.Invoke(profile, subscriptionExtras);
								break;
							}
							default:
								Debug.LogError((object)$"MessageTypeCode-{num} is not supported");
								Debug.LogError((object)jObject.ToString());
								break;
							}
						}
						catch (Exception ex4)
						{
							Debug.LogError((object)("[ChzzkUnity] 후원/구독 메시지 처리 중 오류: " + ex4.Message));
						}
					}
					break;
				}
			}
			case 10100L:
			{
				UnityEvent obj = onOpen;
				if (obj != null)
				{
					obj.Invoke();
				}
				break;
			}
			}
		}
		catch (Exception ex7)
		{
			Debug.LogError((object)ex7.ToString());
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

	private void StartChat(object sender, EventArgs e)
	{
		Debug.Log((object)("OPENED : " + cid + " + " + token));
		string data = "{\"ver\":\"2\",\"cmd\":100,\"svcid\":\"game\",\"cid\":\"" + cid + "\",\"bdy\":{\"uid\":null,\"devType\":2001,\"accTkn\":\"" + token + "\",\"auth\":\"READ\"},\"tid\":1}";
		timer = 0f;
		running = true;
		socket.Send(data);
	}
}
