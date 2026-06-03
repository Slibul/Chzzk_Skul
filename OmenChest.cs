using HarmonyLib;
using Level;
using System.Threading.Tasks;
using UnityEngine;
using Characters.Gear.Synergy.Inscriptions;

namespace ChzzkSkul;

public class OmenChestPath
{
    private static ChzzkUnity streamingConnection;

    /// <summary>
    /// true일 때 다음 HardmodeChest를 흉조 상자로 강제 변환합니다.
    /// !omen 커맨드에서 활성화되며, 한 번 변환 후 자동으로 false로 초기화됩니다.
    /// </summary>
    public static bool ForceNextOmen { get; set; } = false;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HardmodeChest), nameof(HardmodeChest.TryToChangeOmenChest))]
    static void OmenChest(ref HardmodeChest __instance, ref bool ____isOmenChest)
    {
        // 항상 흉조 상자 OR ForceNextOmen 플래그가 켜진 경우 강제 변환
        if (ForceNextOmen)
        {
            ____isOmenChest = true;
            ForceNextOmen = false; // 한 번만 적용
            Debug.Log("[OmenChestPath] !omen 커맨드로 흉조 상자가 강제 활성화되었습니다.");
        }
        else
        {
            ____isOmenChest = true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Inscription), "isSuper", MethodType.Getter)]
    static void Inscription_isSuper_Postfix(Inscription __instance, ref bool __result)
    {
        if (ChzzkGameMode.CustomSuperInscriptions.Contains(__instance.key))
        {
            __result = true;
        }
    }

    public static async Task StartChzzk(ChzzkGameMode gameModeHandler, string channelId)
    {
        var go = new GameObject("ChatIntegrationHost");
        UnityEngine.Object.DontDestroyOnLoad(go);
        
        if (!string.IsNullOrEmpty(channelId))
        {
            streamingConnection = go.AddComponent<ChzzkUnity>();
            streamingConnection.onMessage.AddListener((profile, msg) => gameModeHandler.OnChatMessage(profile?.nickname, msg));
            streamingConnection.onDonation.AddListener((profile, msg, donation) => gameModeHandler.OnDonation(profile?.nickname, donation?.payAmount ?? 0));
            streamingConnection.Connect(channelId);
        }

        if (Plugin.EnableYoutube.Value && !string.IsNullOrEmpty(Plugin.YoutubeApiKey.Value) && !string.IsNullOrEmpty(Plugin.YoutubeVideoId.Value))
        {
            var ytConnection = go.AddComponent<YouTubeUnity>();
            ytConnection.onMessage.AddListener((nickname, msg) => gameModeHandler.OnChatMessage(nickname, msg));
            ytConnection.onDonation.AddListener((nickname, msg, amount) => gameModeHandler.OnDonation(nickname, amount));
            ytConnection.Connect(Plugin.YoutubeApiKey.Value, Plugin.YoutubeVideoId.Value);
            Debug.Log("[OmenChestPath] YouTube Live Chat integration started.");
        }

        await Task.CompletedTask;
    }
}
