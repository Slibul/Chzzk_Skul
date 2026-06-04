using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Threading.Tasks;
using UnityEngine;
using ChzzkSkul;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public static ConfigEntry<string> ChannelID;

    // YouTube
    public static ConfigEntry<bool> EnableYoutube;
    public static ConfigEntry<string> YoutubeApiKey;
    public static ConfigEntry<string> YoutubeVideoId;

    public static ConfigEntry<bool> AllowHeal;
    public static ConfigEntry<bool> AllowBuffCurse;
    public static ConfigEntry<bool> AllowItem;
    public static ConfigEntry<bool> AllowSkull;
    public static ConfigEntry<bool> AllowSynergy;
    public static ConfigEntry<bool> AllowOmen;
    public static ConfigEntry<bool> AllowBoss;
    public static ConfigEntry<bool> AllowDarkAbility;
    public static ConfigEntry<bool> AllowRandomStat;
    public static ConfigEntry<bool> AllowNpc;
    public static ConfigEntry<bool> AllowFood;
    public static ConfigEntry<bool> AllowBone;
    public static ConfigEntry<bool> AllowGold;
    public static ConfigEntry<bool> AllowDarkQuartz;
    public static ConfigEntry<bool> AllowQuintessence;
    public static ConfigEntry<bool> AllowFragment;
    public static ConfigEntry<bool> AllowDefense;

    // 투표 기능 설정
    public static ConfigEntry<bool> EnableVote;
    public static ConfigEntry<string> VoteTriggerMode;
    public static ConfigEntry<float> VoteIntervalSeconds;
    public static ConfigEntry<float> VoteDurationSeconds;

    public static ConfigEntry<bool> AllowStreamerCommand;
    public static ConfigEntry<string> StreamerNickname;
    public static ConfigEntry<float> CommandCooldown;

    public static ConfigEntry<string> CmdHealString;
    public static ConfigEntry<string> CmdBuffCurseString;
    public static ConfigEntry<string> CmdItemString;
    public static ConfigEntry<string> CmdSkullString;
    public static ConfigEntry<string> CmdSynergyString;
    public static ConfigEntry<string> CmdOmenString;
    public static ConfigEntry<string> CmdBossString;
    public static ConfigEntry<string> CmdDarkAbilityString;
    public static ConfigEntry<string> CmdRandomString;
    public static ConfigEntry<string> CmdRandomStatString;
    public static ConfigEntry<string> CmdNpcString;
    public static ConfigEntry<string> CmdFoodString;
    public static ConfigEntry<string> CmdBoneString;
    public static ConfigEntry<string> CmdGoldString;
    public static ConfigEntry<string> CmdDarkQuartzString;
    public static ConfigEntry<string> CmdQuintessenceString;
    public static ConfigEntry<string> CmdFragmentString;
    public static ConfigEntry<string> CmdDefenseString;

    public static ConfigEntry<bool> EnableChatCommands;
    public static ConfigEntry<bool> EnableChaosMode;
    public static ConfigEntry<float> ChaosIntervalSeconds;

    private void Awake()
    {
        ChannelID = Config.Bind<string>("General", "치지직 채널 ID", "", "연결할 치지직 스트리머의 채널 ID입니다.");

        EnableYoutube = Config.Bind<bool>("YouTube", "유튜브 연동 켜기", false, "유튜브 실시간 채팅 연동 기능을 활성화합니다.");
        YoutubeApiKey = Config.Bind<string>("YouTube", "유튜브 API 키", "", "구글 클라우드에서 발급받은 YouTube Data API v3 키를 입력하세요.");
        YoutubeVideoId = Config.Bind<string>("YouTube", "유튜브 방송 Video ID", "", "현재 방송 중인 유튜브 스트리밍의 Video ID를 입력하세요.");
        
        AllowHeal = Config.Bind<bool>("Commands", "힐 활성화", true, "!heal / !힐 명령어 활성화 여부");
        AllowBuffCurse = Config.Bind<bool>("Commands", "버프저주 활성화", true, "!buff / !curse 명령어 활성화 여부");
        AllowItem = Config.Bind<bool>("Commands", "아이템 활성화", true, "!item / !아이템 명령어 활성화 여부");
        AllowSkull = Config.Bind<bool>("Commands", "스컬 활성화", true, "!skull / !해골 명령어 활성화 여부");
        AllowSynergy = Config.Bind<bool>("Commands", "각인 활성화", true, "!synergy / !각인 명령어 활성화 여부");
        AllowOmen = Config.Bind<bool>("Commands", "흉조 활성화", true, "!omen / !흉조 명령어 활성화 여부");
        AllowBoss = Config.Bind<bool>("Commands", "보스 활성화", true, "!boss / !보스 명령어 활성화 여부");
        AllowDarkAbility = Config.Bind<bool>("Commands", "검은능력 활성화", true, "!dark / !검은능력 명령어 활성화 여부");
        AllowRandomStat = Config.Bind<bool>("Commands", "랜덤스탯 활성화", true, "!랜덤스탯 명령어 활성화 여부");
        AllowNpc = Config.Bind<bool>("Commands", "NPC 활성화", true, "!npc 명령어 활성화 여부");
        AllowFood = Config.Bind<bool>("Commands", "음식 활성화", true, "!food 명령어 활성화 여부");
        AllowBone = Config.Bind<bool>("Commands", "뼈 활성화", true, "!뼈 명령어 활성화 여부");
        AllowGold = Config.Bind<bool>("Commands", "골드 활성화", true, "!골드 명령어 활성화 여부");
        AllowDarkQuartz = Config.Bind<bool>("Commands", "마석 활성화", true, "!마석 명령어 활성화 여부");
        AllowQuintessence = Config.Bind<bool>("Commands", "정수 활성화", true, "!정수 명령어 활성화 여부");
        AllowFragment = Config.Bind<bool>("Commands", "파편 활성화", true, "!파편 명령어 활성화 여부");
        AllowDefense = Config.Bind<bool>("Commands", "방어력 활성화", true, "!방어력 명령어 활성화 여부");

        EnableVote = Config.Bind<bool>("Vote", "채팅 투표 활성화", true, "자동 채팅 투표 시스템을 켤지 끌지 설정합니다.");
        VoteTriggerMode = Config.Bind<string>("Vote", "투표 시작 방식", "Scene", "투표를 여는 방식: 'Timer'(시간마다) 또는 'Scene'(맵 이동 시마다)");
        VoteIntervalSeconds = Config.Bind<float>("Vote", "타이머 간격 (초)", 300f, "타이머 방식일 경우, 몇 초마다 투표를 열지 설정합니다.");
        VoteDurationSeconds = Config.Bind<float>("Vote", "투표 진행 시간 (초)", 30f, "투표가 열린 후 닫힐 때까지의 시간입니다.");

        AllowStreamerCommand = Config.Bind<bool>("General", "스트리머 명령어 허용", true, "스트리머 본인의 명령어 사용을 허용할지 여부");
        StreamerNickname = Config.Bind<string>("General", "스트리머 닉네임", "스트리머", "스트리머의 치지직 닉네임을 입력하세요.");
        CommandCooldown = Config.Bind<float>("General", "명령어 쿨타임", 5f, "일반 유저의 명령어 쿨타임 (초)");

        CmdHealString = Config.Bind<string>("CommandStrings", "힐 명령어", "!heal,!힐", "쉼표(,)로 구분하세요.");
        CmdBuffCurseString = Config.Bind<string>("CommandStrings", "버프저주 명령어", "!buff,!버프,!curse,!저주", "쉼표(,)로 구분하세요.");
        CmdItemString = Config.Bind<string>("CommandStrings", "아이템 명령어", "!item,!아이템", "쉼표(,)로 구분하세요.");
        CmdSkullString = Config.Bind<string>("CommandStrings", "스컬 명령어", "!skull,!해골,!머리,!대머리,!skul", "쉼표(,)로 구분하세요.");
        CmdSynergyString = Config.Bind<string>("CommandStrings", "각인 명령어", "!synergy,!각인", "쉼표(,)로 구분하세요.");
        CmdOmenString = Config.Bind<string>("CommandStrings", "흉조 명령어", "!omen,!흉조", "쉼표(,)로 구분하세요.");
        CmdBossString = Config.Bind<string>("CommandStrings", "보스 명령어", "!boss,!보스", "쉼표(,)로 구분하세요.");
        CmdDarkAbilityString = Config.Bind<string>("CommandStrings", "검은능력 명령어", "!dark,!검은능력", "쉼표(,)로 구분하세요.");
        CmdRandomString = Config.Bind<string>("CommandStrings", "랜덤 명령어", "!random,!랜덤", "쉼표(,)로 구분하세요.");
        CmdRandomStatString = Config.Bind<string>("CommandStrings", "랜덤스탯 명령어", "!randomstat,!랜덤스탯", "쉼표(,)로 구분하세요.");
        CmdNpcString = Config.Bind<string>("CommandStrings", "NPC 명령어", "!npc,!엔피씨,!상인,!npc스폰", "쉼표(,)로 구분하세요.");
        CmdFoodString = Config.Bind<string>("CommandStrings", "음식 명령어", "!food,!음식,!밥", "쉼표(,)로 구분하세요.");
        CmdBoneString = Config.Bind<string>("CommandStrings", "뼈 명령어", "!bone,!뼈", "쉼표(,)로 구분하세요.");
        CmdGoldString = Config.Bind<string>("CommandStrings", "골드 명령어", "!gold,!골드,!돈", "쉼표(,)로 구분하세요.");
        CmdDarkQuartzString = Config.Bind<string>("CommandStrings", "마석 명령어", "!darkquartz,!마석", "쉼표(,)로 구분하세요.");
        CmdQuintessenceString = Config.Bind<string>("CommandStrings", "정수 명령어", "!quintessence,!정수", "쉼표(,)로 구분하세요.");
        CmdFragmentString = ((BaseUnityPlugin)this).Config.Bind<string>("CommandStrings", "파편 명령어", "!파편,!fragment", "쉼표(,)로 구분하세요.");
        CmdDefenseString = Config.Bind<string>("CommandStrings", "방어력 명령어", "!defense,!방어력", "쉼표(,)로 구분하세요.");
        EnableChatCommands = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "채팅 명령어 활성화", true, "모든 채팅 명령어를 켜거나 끕니다.");
        EnableChaosMode = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "카오스 모드 활성화", false, "정해진 시간마다 무작위 명령어가 자동 실행됩니다.");
        ChaosIntervalSeconds = ((BaseUnityPlugin)this).Config.Bind<float>("General", "카오스 모드 발동 간격 (초)", 60f, "카오스 모드가 발동될 주기(초)입니다.");

        Harmony.CreateAndPatchAll(typeof(OmenChestPath));
        Logger.LogInfo($"Mod {MyPluginInfo.PLUGIN_GUID} is loaded!");

        // ChzzkGameMode MonoBehaviour를 실행할 전용 GameObject 생성
        // DontDestroyOnLoad으로 씬 전환에도 유지
        GameObject gameModeObject = new GameObject("ChzzkGameModeHost");
        DontDestroyOnLoad(gameModeObject);

        ChzzkGameMode gameModeHandler = gameModeObject.AddComponent<ChzzkGameMode>();

        // Chzzk 채팅 연결 시작 (게임모드 핸들러 전달 및 Config 채널 ID 전달)
        Task startChzzkTask = OmenChestPath.StartChzzk(gameModeHandler, ChannelID.Value);

        Logger.LogInfo("[Plugin] Chzzk 채팅 게임 모드가 활성화되었습니다.");
    }

    private void Update()
    {
        // F5 키를 누르면 Config 파일을 실시간으로 다시 읽어들입니다.
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Config.Reload();
            Logger.LogInfo("[Plugin] Config 파일이 재로드 되었습니다!");
        }
    }
}

