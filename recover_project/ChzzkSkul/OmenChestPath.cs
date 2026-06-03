using System.Threading.Tasks;
using Characters.Gear.Synergy.Inscriptions;
using HarmonyLib;
using Level;
using UnityEngine;
using UnityEngine.Events;

namespace ChzzkSkul;

public class OmenChestPath
{
	private static ChzzkUnity streamingConnection;

	public static bool ForceNextOmen { get; set; }

	[HarmonyPostfix]
	[HarmonyPatch(typeof(HardmodeChest), "TryToChangeOmenChest")]
	private static void OmenChest(ref HardmodeChest __instance, ref bool ____isOmenChest)
	{
		if (ForceNextOmen)
		{
			____isOmenChest = true;
			ForceNextOmen = false;
			Debug.Log((object)"[OmenChestPath] !omen 커맨드로 흉조 상자가 강제 활성화되었습니다.");
		}
		else
		{
			____isOmenChest = true;
		}
	}

	[HarmonyPostfix]
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	private static void Inscription_isSuper_Postfix(Inscription __instance, ref bool __result)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		if (ChzzkGameMode.CustomSuperInscriptions.Contains(__instance.key))
		{
			__result = true;
		}
	}

	public static async Task StartChzzk(ChzzkGameMode gameModeHandler, string channelId)
	{
		GameObject go = new GameObject("ChatIntegrationHost");
		Object.DontDestroyOnLoad((Object)(object)go);
		if (!string.IsNullOrEmpty(channelId))
		{
			streamingConnection = go.AddComponent<ChzzkUnity>();
			streamingConnection.onMessage.AddListener((UnityAction<ChzzkUnity.Profile, string>)delegate(ChzzkUnity.Profile profile, string msg)
			{
				gameModeHandler.OnChatMessage(profile?.nickname, msg);
			});
			streamingConnection.onDonation.AddListener((UnityAction<ChzzkUnity.Profile, string, ChzzkUnity.DonationExtras>)delegate(ChzzkUnity.Profile profile, string msg, ChzzkUnity.DonationExtras donation)
			{
				gameModeHandler.OnDonation(profile?.nickname, donation?.payAmount ?? 0);
			});
			streamingConnection.Connect(channelId);
		}
		if (Plugin.EnableYoutube.Value && !string.IsNullOrEmpty(Plugin.YoutubeApiKey.Value) && !string.IsNullOrEmpty(Plugin.YoutubeVideoId.Value))
		{
			YouTubeUnity ytConnection = go.AddComponent<YouTubeUnity>();
			ytConnection.onMessage.AddListener((UnityAction<string, string>)delegate(string nickname, string msg)
			{
				gameModeHandler.OnChatMessage(nickname, msg);
			});
			ytConnection.onDonation.AddListener((UnityAction<string, string, int>)delegate(string nickname, string msg, int amount)
			{
				gameModeHandler.OnDonation(nickname, amount);
			});
			ytConnection.Connect(Plugin.YoutubeApiKey.Value, Plugin.YoutubeVideoId.Value);
			Debug.Log((object)"[OmenChestPath] YouTube Live Chat integration started.");
		}
		await Task.CompletedTask;
	}
}
